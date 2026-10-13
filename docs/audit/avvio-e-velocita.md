# Audit di velocità: avvio a freddo e programma aperto

**Commit di main letto: `9170ccdb879730ee9b5c6b34bf7694831fbb8306`** (7 ottobre 2026).
Sessione online (Linux, senza Windows): lettura del codice e banchi di misura fuori dal repository. Il codice non è stato modificato. Il rapporto è l'unico file aggiunto.

## Come leggere il rapporto

- **MISURATO** vale solo per ciò che è stato eseguito qui, su questa macchina: container Linux, 4 CPU, .NET SDK 10.0.112 (pacchetti Ubuntu), runtime 10.0.12, Release. I numeri di questa macchina non si confrontano con quelli del PC dell'autore (Windows 11, 120 Hz): valgono per i confronti relativi e per l'andamento al crescere dei dati. Dove un punto dice MISURATO, dice anche che cosa è stato eseguito.
- **CONFERMATO** vale per ciò che è stato letto nel codice, con file e riga (le righe sono quelle del commit sopra).
- **SOSPETTO** vale per ciò che dipende da Windows, da WPF/WebView2/MSAL (codice non presente qui), dal disco o dalla rete; dice che cosa manca per confermarlo.
- Nessun millisecondo di Windows è stato misurato qui. I numeri del PC dell'autore citati nel rapporto sono quelli del mandato, di CLAUDE.md e di `docs/audit/ESITO.md`.
- Non si ripetono i punti già corretti dai due rapporti precedenti e dai dieci blocchi di ESITO.md, né quelli misurati e lasciati. Non si riprendono le scelte già prese: DirectManipulation, la riga disegnata da un solo elemento, `CacheLength` 0, `ScrollHitGuard`, il modello 3 del touchpad, il servizio di rete di WebView2 in un processo suo, il controllo dei nuovi messaggi ogni due minuti.
- I nomi di server, domini e indirizzi che compaiono nel codice non sono riportati: si dice "il server IMAP di Gmail", "il dominio di prova", e così via.

## Sommario

Sei cose da sapere prima dei dettagli.

1. **Il disegno continuo dopo lo scorrimento dura più del gesto** (B-01): il cursore della barra tiene sottoscritto `CompositionTarget.Rendering` per fino a circa un secondo dopo l'ultima tacca (calcolo dalle costanti del codice), e WPF in quel tempo non lascia passare le operazioni a priorità Input per più di 500 ms (limite di WPF, non di Missiva: sorgente di WPF `MediaContext.cs`). Proposta in B.0: un solo `FrameClock` che si spegne al primo tasto o clic, la coda del cursore chiusa a fine gesto e la ripresa del hit test dal fotogramma. Resta da confermare che i tasti veri non passino dalla coda Input (B-04): se sì, il ritardo riguarda solo le riprese interne e i comandi di prova.
2. **Il file da 266 MB è un composito ReadyToRun da 155 MB, e 30 MB sono la proiezione WinRT del Windows SDK** (A-11, MISURATO): escluderla dal ReadyToRun è la prova più economica; l'effetto a freddo si conosce solo con il piano A.7.
3. **Prima del primo disegno girano cose che non servono al primo disegno** (A-01..A-08, A-12, A-21): la lettura delle impostazioni dopo il tema, `ThemeMode` assegnato due volte, `CleanTemp` e i toast sincroni, certificati X.509 e `HttpClient` dei loghi, statici di MSAL e MailKit, un secondo layout. I 257 ms "dell'XAML" contengono anche altro (A-02): prima di ottimizzare l'XAML conviene leggere le righe "tema" e "rubrica e temporanei" che il registro scrive già.
4. **All'accesso a Windows con la rete in ritardo** (A-13..A-19): i timer di Posponi, invii pianificati e promemoria partono solo dopo che tutte le cartelle sono state lette o sono fallite; "Non in linea" e "Non raggiungibile" non si ripuliscono da soli e le cartelle non si rileggono; l'IDLE ritenta a 5, 15, 35, 75, 155, 315 s senza svegliarsi al ritorno della rete.
5. **Il codice aggiunto dagli audit non costa sulle email normali** (B-24, MISURATO): +3-7% di tempo e +8% di allocazioni nella pulizia dell'HTML a 788 KB, `ReadableText` più veloce del 15-25%, `CleanName`/`NameAddress`/`Skeleton` circa 1 µs per indirizzo.
6. **Una perdita di lavoro e un quadratico** (B-13, B-11): la coda delle azioni senza rete esce dal file prima di essere ripetuta (uscire a metà le perde); `IsLast` di `HtmlToFlow` è quadratico sui `<br>` fratelli (MISURATO come andamento: 2-3 s con 40.000 su Linux).

Il resto (circa 60 punti) è in ordine di parte e di sigla; la tabella per guadagno atteso è prima di "Guardato e trovato a posto".

## Che cosa è stato misurato qui

| Che cosa | Come | Dove nel rapporto |
|---|---|---|
| Contenuto e dimensione del pacchetto, con e senza ReadyToRun, file unico e cartella | `dotnet publish -c Release` per win-x64 (stesso comando di `installa.ps1`) e tre varianti; analisi dell'intestazione del file unico (`bundle` versione 6) | A-11 |
| Pulizia dell'HTML delle email: codice di oggi contro commit `3ae3675` (prima degli audit) | banco console net10.0 con copie delle funzioni pure, 5 dimensioni di email generate, 30 giri per misura, 5 di riscaldamento | B-24 |
| `CleanName`, `NameAddress`, `Skeleton` per indirizzo | stesso banco | B-24 |
| Deserializzazione JSON delle impostazioni e della copia locale | i tipi veri di Missiva (`SettingsJson`, `CacheJson`) caricati da `Missiva.dll`, dati generati | A-03 |
| Andamento di `IsLast` di `HtmlToFlow` con molti `<br>` | AngleSharp 1.8.2, ciclo di `NextSibling` | B-11 |

Il codice essenziale dei banchi è in fondo, nell'appendice (nei dati del banco di B-24 due nomi di prova sono stati sostituiti con equivalenti d'esempio per non scrivere domini veri; la struttura dei nomi e il percorso del codice sono gli stessi).

# Parte A: avvio a freddo

## A.0 Che cosa gira prima del primo disegno, in ordine

Tempi del PC dell'autore (mandato, 5 ottobre 2026, avvio a caldo, ms dall'avvio del processo): Main 92-116; tema Fluent 259; OnStartup 265; impostazioni lette 304-335; XAML della finestra 592; finestra creata 600; disposizione 680; finestra mostrata 719-730; primo disegno 766-790; cartelle dalla copia locale 817; elenco nel disegno 938-1015. Ogni riga sotto è CONFERMATA nel codice.

| # | Dove | Che cosa fa | Thread |
|---|---|---|---|
| 0 | `Missiva.csproj` | Avvio del runtime da file unico non compresso; al primo avvio dopo un'installazione le 8 librerie native (11,6 MB, vedi A-11) si estraggono in `%TEMP%\.net`. Mai misurato a parte. | processo |
| 1 | `Program.cs:20,33,52,56,72-74` | argomenti, `Process.GetCurrentProcess().SessionId` (carica `System.Diagnostics.Process` per il solo nome del canale), mutex, `Log.Startup("Main")`, `new UI.App()`, `Run()` | interfaccia |
| 2 | `UI/App.cs:19-24` (`App`, costruttore) | costruttore di `Application` e `ThemeMode = ThemeMode.System` (riga 21): carica le risorse Fluent, sincrono, prima di `OnStartup`. I 143 ms fra Main e "tema Fluent" contengono sia l'avvio di WPF sia questo; dal codice non si separano. | interfaccia |
| 3 | `UI/App.cs:28-34` | `UI/Styles.xaml` (57 righe, un solo stile `NavItem`), `WheelScrolling.Enable()` (`UI/WheelScrolling.cs:122`, due `RegisterClassHandler`) | interfaccia |
| 4 | `UI/App.cs:37` → `Core/Backup.cs:542-544` | `Backup.ApplyPending`: senza importazione in attesa è un `File.Exists` | interfaccia |
| 5 | `UI/App.cs:38` → `Core/Store.cs:664` | `Store.Load`: `Store.Dir` (campo statico, `GetFolderPath`, riga 648); `Read` (683) con due `File.Exists` (687); `TryParse` (715): `File.ReadAllText` e deserializzatore generato `SettingsJson` (539); una `ProtectedData.Unprotect` per ogni valore `dpapi:` (`ProtectedTextConverter.Open`, 440); `File.Copy` verso `impostazioni.bak` solo se manca (696); `SafeFile.SetAside` (595) solo se il file è rotto; `Settings.UpgradeImageSenders` (672 → 146) | interfaccia |
| 6 | `UI/App.cs:39,58,59` | `Store.SaveSoonOn`; `MicrosoftAuth.WarmUp` e `GmailProvider.Preconnect` (Task.Run: sul thread dell'interfaccia restano il `Where` e, per Gmail, `GoogleAuth.IsConfigured`, `Core/GoogleAuth.cs:184`) | interfaccia + pool |
| 7 | `UI/App.cs:60-61` | seconda assegnazione di `ThemeMode` (vedi A-02), log "tema" | interfaccia |
| 8 | `UI/App.cs:62-74` | `AddressBook.Preload` (pool); `Safety.CleanTemp(1 giorno)` **sincrono** (63, `Core/Safety.cs:153`); `MailCache.EncryptOld` a +20 s e `BodyCache.TrimAll` a +30 s (pool); `AuthenticateFromCopies` a +2 s (pool) | interfaccia + pool |
| 9 | `UI/App.cs:75-118` | sottoscrizioni; toast `OnActivated +=` (91); `StallLog.Start` (104); `ResetDark` (registro) e `SetTextColors` (`UI/App.cs:572`); `SystemEvents.UserPreferenceChanged +=` (110, crea la sua finestra e il suo thread); `WasCurrentProcessToastActivated` (114); `ProcessRenderMode` (118) | interfaccia |
| 10 | `UI/App.cs:119` → `UI/MainWindow.xaml.cs:36-` | `new MainWindow()`: campi (collezioni, `MessageRows`, `DispatcherTimer`), `InitializeComponent` (38) di `UI/MainWindow.xaml` (1031 righe) con `ReadingView` annidato (`UI/ReadingView.xaml.cs:100`), icona `missiva.ico` (60 KB) e `missiva-128.png`; log "XAML della finestra" (41); `FindResource("MessageRowNew")` (43); `ShowUnifiedGroup` (47); `CollectionViewSource.GetDefaultView` (48); `Logos.Arrived +=` (60: esegue i costruttori statici di `Logos` e di `SenderLogos`, vedi A-04) | interfaccia |
| 11 | `UI/App.cs:131` | `main.Show()`: HWND, WindowChrome, DWM, primo layout. Il `Loaded` fa partire `StartAsync` (`MainWindow.xaml.cs:95`) con il suo prefisso sincrono: `ApplySettings(reload:false)` (103 → 550: `ApplyLayout` 630, `GroupDescriptions`, `FitBars` che chiama `CommandOverflow.Fit` con `Measure`, `Reader.Refresh`), `ShowFolders` (104 → 758, `FitColumns`), log "disposizione", `Dispatcher.BeginInvoke(Reader.Prepare, ApplicationIdle)` (107), `await rendered.Task` (110) | interfaccia |
| 12 | `UI/App.cs:134-183` | dopo `Show()` ma prima del primo giro dei messaggi: `UpdateTray` (`UI/TrayIcon.cs:33-41`: `HwndSource`, `ExtractIconEx` sull'eseguibile, `Shell_NotifyIconW`), controlli di recupero (BeginInvoke), `ApplyStartWithWindows` (registro), `ListenForMailto`, timer da 2 minuti, `NetworkChange.NetworkAddressChanged +=` | interfaccia |
| 13 | dopo il primo disegno: `LoadAccountsAsync` (`MainWindow.xaml.cs:118` → 891) | provider (`NewProvider`), `UpdateQueued` (913, può leggere `coda.dat`), per ogni account `MailCache.LoadFolders` (file + DPAPI), `WatchInboxes`, `SelectStart → SelectInbox → SelectFolder → LoadMoreAsync` (2142: `LoadMessages` con file + DPAPI + 50 `PlainPreview`; `ApplyWoken`; `ActionQueue.Apply`; `rows.Append` con 50 `MessageItem`, ciascuno con `SenderLogos.Note`, `UI/ViewModels.cs:674`) | interfaccia |

Costruttori statici e inizializzatori toccati prima del primo disegno (CONFERMATI): `Store` (`Dir`), `SettingsJson`/`CacheJson` (generati, nessuna riflessione), `Logos` → `SenderLogos` (A-04), `MicrosoftAuth` (A-12), `GmailProvider.early` (A-12), `MailHtml` (solo `Color`, `HashSet`, `SearchValues`, array: `Core/MailHtml.cs:20,22,27,270-271,405`). Le `[GeneratedRegex]` di `MailHtml`, anche quelle `NonBacktracking`, costruiscono la regex al primo uso (`Instance` statico di una classe annidata nel codice generato, letto compilando il generatore qui), non nel costruttore statico: non pesano sull'avvio, pesano la prima volta che si apre un messaggio (vedi A-09).

## A.1 Letture dal disco all'avvio

Le dimensioni sono stime dal codice e da CLAUDE.md, non misure; "interfaccia" è il thread dell'interfaccia, "pool" un `Task.Run`.

| File | Chi lo legge | Quando | Thread | Dimensione (stima) | DPAPI |
|---|---|---|---|---|---|
| `importazione.dat` | `Core/Backup.cs:544` | prima di `Store.Load` | interfaccia | di solito assente: un `File.Exists` | 0 |
| `impostazioni.json` | `Core/Store.cs:687,720` | `OnStartup` | interfaccia | 13 KB sul PC dell'autore (ESITO.md); cresce con `RulesSeen` (fino a 2.000 impronte), contrassegni, regole, firme e modelli RTF (100-200 KB l'uno con un logo) | uno per valore `dpapi:` (A-03) |
| `impostazioni.bak` | `Core/Store.cs:687` | `File.Exists`; letto solo se il principale è rotto; `File.Copy` una volta se manca (696) | interfaccia | uguale al principale | 0 |
| `rubrica.json` | `Core/AddressBook.cs:105-109` (`Preload`) | subito | pool | da decine a centinaia di KB; senza il file scandisce le copie locali (114-116) | 1 |
| `contatti.dat` e copie dei contatti per account | `AddressBook.cs:94` (`Preload`) | subito | pool | decine di KB | 1 + 1 per account |
| cache MSAL | `MicrosoftAuth.WarmUp` (`Core/MicrosoftAuth.cs:187`) | subito | pool | decine di KB | interna a MSAL |
| `token-google.dat` | `GoogleAuth.TokenAsync` da `Preconnect` (`Core/GmailProvider.cs:71-90`) | subito | pool | meno di 1 KB | 1 |
| `cache\<account>\cartelle.json` | `Core/MailCache.cs:30`, `MainWindow.xaml.cs:924` | dopo il primo disegno | interfaccia | pochi KB per account | 1 per account |
| `cache\<account>\<hash>.json` (prima pagina) | `MailCache.cs:33`, `MainWindow.xaml.cs:2142` | dopo il primo disegno | interfaccia | 50 messaggi con anteprima: circa 70 KB (misurato in A-03 con dati finti) | 1 |
| `coda.dat` | `Core/ActionQueue.cs:46` via `UpdateQueued` | dopo il primo disegno | interfaccia | meno di 1 KB | 1 |
| `loghi\domini.dat` | `Core/SenderLogos.cs:226-240`, dal primo `MessageItem` | dopo il primo disegno | interfaccia | una voce per dominio visto, centinaia di KB dopo anni (stima) | 1 sul file intero |
| `loghi\<dominio>.png` | `UI/ViewModels.cs:1340-1353` | per ogni riga con dominio autenticato nuovo | interfaccia | 5-15 KB l'uno, decodifica `BitmapImage` | 0 |
| tutti i `cache\**\*.json` | `MailCache.EncryptOld` (`MailCache.cs:80-103`) | +20 s | pool | legge **per intero** ogni file per guardarne il primo byte | solo se in chiaro |
| `cache\<account>\corpi\*.bin` | `BodyCache.TrimAll` (`Core/Offline.cs:490-504`) | +30 s | pool | fino a 200 MB per account; due enumerazioni e un ordinamento per data | 0 |
| copie dei corpi | `SenderLogos.AuthenticateFromCopies` (`SenderLogos.cs:199-202`) | +2 s, una volta sola | pool | dopo la prima volta è un `File.Exists` sul segno | 1 per copia la prima volta |
| `WebView2\` (profilo) | WebView2 | `Prepare` o primo messaggio | processo del browser | vedi B-21 | 0 |

## A.2 Punti trovati: percorso fino al primo disegno

### A-01 La lettura delle impostazioni aspetta il tema Fluent, e con lei partono in ritardo i riscaldamenti di rete
- **Dove:** `UI/App.cs:21` (tema nel costruttore) contro `:37-38` (`Backup.ApplyPending`, `Store.Load`) e `:58-59` (`WarmUp`, `Preconnect`); `Program.cs:73`.
- **Scenario:** la sequenza è seriale sul thread principale: avvio di WPF e tema Fluent (fino a 259 ms), `Styles.xaml`, `Store.Load` (fino a 304-335 ms) e solo dopo partono il token MSAL e la connessione IMAP di Gmail. `Store.Load` non usa il Dispatcher né WPF (`SaveSoonOn` è già separato, `App.cs:39`): potrebbe girare in parallelo al costruttore di `App`. Il collegamento a Gmail con XOAUTH2 costa da 1 a 2 s (CLAUDE.md): quella parte si sposta indietro nel tempo per intero.
- **Gravità:** Media. Non accorcia da sola il primo disegno; accorcia il tempo fino ai dati del server (elenco di Gmail 1,0-1,1 s, cartelle di Graph 0,13-0,2 s) di quanto vale il ritardo con cui partono `WarmUp` e `Preconnect` (150-200 ms, ricavato dai tempi sopra). Non cresce con i dati, salvo la lettura (A-03).
- **Stato:** CONFERMATO l'ordine. SOSPETTO il guadagno: mancano gli istanti assoluti di `WarmUp` e `Preconnect` nel registro.
- **Proposta:** in `Program.Main`, prima di `new UI.App()`, `Task.Run(() => { Backup.ApplyPending(...); Store.Load(); })` con join in `OnStartup` (tenendo l'esito di `ApplyPending`; l'ordine "importazione, poi Load" resta, `App.cs:35`). `WarmUp` e `Preconnect` subito dopo il join. Prima di farlo controllare che `Store.Recovery`, `StoreRecovery.Blocked` e `ProtectedTextConverter` (statici) non siano toccati da altri thread nel frattempo; `Log.Startup` è già thread-safe (lucchetto, `Core/Store.cs:1060-1064`).
- **Verifica su Windows:** Release di prova, 5 avvii a freddo (A.7) con una riga dopo il join e le righe "token MSAL preso in anticipo" e "collegamento IMAP principale in anticipo": confronto degli istanti assoluti prima e dopo.

### A-02 `ThemeMode` assegnato due volte, e i 257 ms "dell'XAML" contengono altro
- **Dove:** `UI/App.cs:21` e `:60`; il log "XAML della finestra" è a `UI/MainWindow.xaml.cs:41`.
- **Scenario (a):** chi ha il tema "Chiaro" o "Scuro" in Impostazioni: il costruttore carica Fluent per il tema di sistema e `OnStartup` lo ricarica per quello scelto, se il setter di `Application.ThemeMode` ricarica le risorse a ogni cambio di valore. Con "Come Windows" il valore è uguale e probabilmente non succede nulla; il setter è codice di WPF, non leggibile qui.
- **Scenario (b):** l'intervallo "impostazioni lette" (304-335) → "XAML della finestra" (592) di 257 ms **non è solo XAML**: contiene `App.cs:53-118` (riga 60 di (a), `CleanTemp` sincrono alla 63, la sottoscrizione ai toast alla 91, `SystemEvents` alla 110, `SetTextColors`) e solo poi l'`InitializeComponent`. Nel registro ci sono già due righe che dividono l'intervallo, "tema" (`App.cs:61`) e "rubrica e temporanei" (`:74`), non citate nel mandato. Rischio: ottimizzare l'XAML invece del lavoro vero.
- **Gravità:** Media per (a) se si verifica, stesso ordine del tema Fluent iniziale; per (b) è un errore di attribuzione. Non cresce con i dati.
- **Stato:** SOSPETTO (a): manca il comportamento del setter. CONFERMATO che le assegnazioni sono due e che le righe 53-118 stanno dentro i 257 ms.
- **Proposta:** con A-01 il valore è noto prima di `new App()`: assegnare `ThemeMode` una volta sola (portare `ModeOf` fuori da `App`) ed eliminare `App.cs:60`; altrimenti almeno `if (ThemeMode != wanted) ThemeMode = wanted`.
- **Verifica su Windows:** dal registro, su 5 avvii, "tema Fluent", "OnStartup", "stili", "impostazioni lette", "tema", "rubrica e temporanei", "XAML della finestra", con tema "Come Windows" e con "Scuro". Se "tema" meno "impostazioni lette" supera 50 ms con tema fisso, (a) è confermato.

### A-03 Firme e modelli RTF decifrati all'avvio con DPAPI, e tenuti due volte in memoria
- **Dove:** `Core/Store.cs:297` (`Signature.StoredRtf`), `:338` (`NamedSignature.StoredRtf`), `:361` (`MailTemplate.Rtf`), `:359-360`, `:105` (`RecentSearches`), `:123` (`ImageSenders`), `:184` (`HiddenCalendars`), `Core/Rules.cs:20-49` (fino a 10 testi per regola), `Store.cs:348,377,388` (contrassegni e posposti: 2 testi ciascuno); convertitore `Store.cs:400-470`, `Open` a `:440`, cache `known` a `:403`.
- **Scenario:** dal blocco 10 (S-26, 5 ottobre) le firme sono cifrate come i modelli. Ogni firma con logo ha un RTF che CLAUDE.md dà di 100-200 KB, e ogni cifrato è una `ProtectedData.Unprotect` all'avvio sul thread dell'interfaccia dentro `JsonSerializer.Deserialize`, anche con `--nascosta`. Testo in chiaro e cifrato restano in `known` (due copie) fino al primo `KeepOnlyWritten`, che scatta solo dopo un `Store.Save`. La misura che ha chiuso V-11 (ESITO.md: 1 testo decifrato, 3,1-5,2 ms) è del 4 ottobre, prima di S-26.
- **Misura qui (MISURATO, Linux, .NET 10.0.12, senza DPAPI):** `SettingsJson` e `CacheJson` veri, caricati da `Missiva.dll` pubblicato, dati generati con le proprietà riempite per riflessione, `ProtectedTextConverter.Portable` acceso (nessuna DPAPI). Impostazioni di 689 KB (3 account, 2.000 `RulesSeen`, 200 contrassegni, 30 posposti, 3 firme con RTF finto da 150 KB): 19 ms la prima lettura (codice JIT, non ReadyToRun), 2,9 ms a regime (mediana di 30). Impostazioni quasi vuote (1 KB): prima lettura 40 ms, regime 0,04 ms (la prima lettura è dominata dal JIT del codice generato; su Windows il codice è ReadyToRun, CLAUDE.md dà 41-44 ms contro 104-109 con il JIT). Copia locale: 50 messaggi 70 KB, prima 7,3 ms, regime 1,15 ms; 500 messaggi 699 KB, regime 16,9 ms (lineare). **La deserializzazione non è il costo: lo è il numero di DPAPI**, che qui non si misura.
- **Gravità:** Media. Il costo cresce con numero e dimensione di firme e modelli, di contrassegni e di regole.
- **Stato:** CONFERMATO il percorso; SOSPETTO il peso: manca la riga "impostazioni lette (N testi decifrati con DPAPI in X ms ...)" (`Store.cs:668-669`) sulla copia dei dati dell'autore dopo S-26.
- **Proposta:** decifratura pigra per `Signature.StoredRtf`, `NamedSignature.StoredRtf`, `MailTemplate.Rtf` e per oggetto e mittente di contrassegni e posposti (servono a editor, scrittura, Impostazioni e notifiche): un tipo `LazyProtectedText` che tiene il cifrato e lo apre al primo `get`; `Seal` rimette il cifrato intatto se non è mai stato aperto. Le regole di Missiva non possono essere pigre (`Rules.Matches`, `MailRule.Unreadable` le leggono al primo controllo), ma si possono aprire in `Task.Run` insieme ad `AddressBook.Preload`.
- **Verifica su Windows:** leggere la riga "impostazioni lette (N testi ... in X ms)" con la copia dei dati vera, con e senza una firma da 150 KB; `GC.GetTotalMemory` prima e dopo `Store.Load`.

### A-04 Domini noti e costruttori statici dei loghi sul thread dell'interfaccia, prima del primo elenco
- **Dove:** `UI/MainWindow.xaml.cs:60` (`Logos.Arrived +=`) → `UI/ViewModels.cs:1327` (costruttore statico di `Logos`: `SenderLogos.Arrived +=`) → inizializzatori statici di `SenderLogos`: `Core/SenderLogos.cs:401-412` (`MarkRoots`: due `X509CertificateLoader.LoadCertificate` di certificati RSA a 4096 bit), `:562` (`Http = CreateHttp()`: `SocketsHttpHandler` con `ConnectCallback`, `576-600`), `:299`, `:300`, `:335`. Poi, al primo `MessageItem`: `ViewModels.cs:674` → `SenderLogos.Note` (`:115-131`) → `Load()` (`:226-240`): `File.ReadAllText` di `domini.dat`, `Store.Unprotect` (DPAPI), `JsonSerializer.Deserialize<Dictionary<string, Seen>>` **per riflessione** (nessun contesto generato). Poi `Logos.Get` (`ViewModels.cs:1340-1353`): `File.Exists` e, per ogni dominio autenticato nuovo, `BitmapImage` con `OnLoad` sul thread dell'interfaccia.
- **Scenario:** avvio visibile: i certificati e l'`HttpClient` servono solo alla ricerca di un logo nuovo (BIMI, icona del sito) e si costruiscono prima del primo disegno. Il primo `MessageItem` carica tutto `domini.dat` (una voce per dominio mai visto: centinaia di KB dopo anni, stima) con il serializzatore per riflessione alla prima chiamata. Avvio con `--nascosta`: lo stesso, prima dell'icona (A-14). `AddressBook` ha il suo `Preload` (`App.cs:62`); `SenderLogos` no. Era già proposto in velocita-2.md V-11; nel codice di oggi non c'è.
- **Gravità:** Media; il file cresce con i domini noti. Non quantificabile da qui (il blocco di 87-90 ms del primo disegno dell'elenco, CLAUDE.md, è un'ipotesi alternativa non dimostrata).
- **Stato:** CONFERMATO il percorso e il thread. SOSPETTO il costo.
- **Proposta:** `MarkRoots` e `Http` in `Lazy<T>` o in una classe annidata usata solo da `BimiAsync`/`Download`; `SenderLogos.Preload()` in `Task.Run` accanto ad `AddressBook.Preload` (`Load` è già sotto `seenLock`, quindi `Note` e `IsAuthenticated` aspettano il lucchetto senza altre protezioni); `JsonSerializerContext` generato per `Dictionary<string, Seen>`.
- **Verifica su Windows:** `Log.Time` attorno a `Load()` ("domini.dat letto in X ms, N domini"); `dotnet-trace` del thread dell'interfaccia fra "Posta in arrivo scelta" ed "elenco nel disegno"; ETW per il caricamento di `System.Security.Cryptography.X509Certificates` e `System.Net.Http` sul thread dell'interfaccia.

### A-05 Lavori di `OnStartup` che non servono al primo disegno ma lo precedono
- **Dove:** `UI/App.cs:63` (`CleanTemp` sincrono, `Core/Safety.cs:153`), `:91` e `:114` (Toolkit delle notifiche), `:110` (`SystemEvents`), `:134` → `:258-276` e `UI/TrayIcon.cs:33-41` (icona), `:158` (`ApplyStartWithWindows`: `Registry.CreateSubKey` in scrittura), `:161` (`ListenForMailto`), `Program.cs:52` (`Process.GetCurrentProcess()`).
- **Scenario:** (1) `CleanTemp(1 giorno)` enumera `%TEMP%\Missiva` e, per ogni sottocartella, tutti i file per le date; costa quanto sono pieni gli allegati aperti nelle sessioni precedenti; lo stesso lavoro gira già ogni minuto in `Task.Run` (`MainWindow.xaml.cs:139`). (2) `ToastNotificationManagerCompat.OnActivated +=` carica `Microsoft.Toolkit.Uwp.Notifications` e la proiezione WinRT sul thread dell'interfaccia; per un'app non pacchettizzata il pacchetto, per quanto ricordo (da confermare), registra anche l'attivatore COM e l'AUMID con scritture di registro. (3) `SystemEvents` crea una finestra nascosta e un thread alla prima sottoscrizione. (4) `Shell_NotifyIconW` chiama Esplora risorse e `ExtractIconEx` apre un eseguibile da 266 MB per leggerne l'icona; con `--nascosta` all'accesso Esplora può non avere ancora la barra (l'icona si rimette comunque da `TaskbarCreated`, `TrayIcon.cs:94-97`). (5) `Process.GetCurrentProcess().SessionId` carica `System.Diagnostics.Process` solo per il nome del canale.
- **Gravità:** Bassa-Media: costo fisso, tranne `CleanTemp` che cresce con i file in `%TEMP%\Missiva`. Tutti stanno prima del primo disegno.
- **Stato:** CONFERMATO che sono sincroni; SOSPETTO quanto costa ciascuno (nessuna riga di registro li isola).
- **Proposta:** `CleanTemp` in `Task.Run` (non tocca l'interfaccia); `SystemEvents` e `ApplyStartWithWindows` a `ApplicationIdle`; icona a `ApplicationIdle` se la finestra è visibile (nel caso nascosto è l'unico segno di vita, resta dov'è, ma vedi A-14); toast: iscrivere `OnActivated` dopo il primo disegno quando `startedForNotification` è falso. Rischio da controllare prima: un clic su una notifica nei primi secondi, prima dell'iscrizione, avvierebbe una seconda copia che porta in primo piano la prima (`Program.cs:61-69`) e l'azione andrebbe persa.
- **Verifica su Windows:** una riga `Log.Startup` prima e dopo ciascuna istruzione (`App.cs:63, 91, 110, 134, 158`), 5 avvii; una cartella `%TEMP%\Missiva` con 200 sottocartelle finte per `CleanTemp`; Process Monitor su `HKCU\Software\Classes\AppUserModelId` e `CLSID`.

### A-06 Un secondo giro di layout dopo il primo, e la barra dei comandi misurata due volte
- **Dove:** `UI/MainWindow.xaml.cs:103-104` (`ApplySettings`, `ShowFolders` in `StartAsync`, cioè nel `Loaded`), `:630-670` (`ApplyLayout` sostituisce `WorkArea.ColumnDefinitions/RowDefinitions`, cambia `Visibility` e `Grid.SetColumn`), `:758-790` (`ShowFolders`), `:838-864` (`FitColumns` cambia la larghezza della colonna 0), `:866` (`FitBars` → `UI/CommandOverflow.cs:23-50`, `Measure` con spazio infinito), `SizeChanged="OnBarSize"` di `HomeBar`/`ViewBar` in `UI/MainWindow.xaml`.
- **Scenario:** se il `Loaded` scatta dopo il primo layout (l'ordine esatto non si legge dal codice; dai tempi, "disposizione" 680 viene prima di "finestra mostrata" 719-730, quindi dentro `Show()`), `ApplyLayout`/`ShowFolders`/`FitColumns` cambiano colonne e visibilità di elenco, riquadro e cartelle dopo che WPF li ha già misurati con i valori dell'XAML, e il `SizeChanged` rilancia `FitBars`: un secondo giro di misura e disposizione dell'albero con i modelli Fluent applicati, prima del primo disegno. CLAUDE.md dà la disposizione a 0,4-0,5 s di CPU senza dire su quale passaggio.
- **Gravità:** Media se confermato; non cresce con i dati.
- **Stato:** SOSPETTO: manca il numero di passaggi di layout prima del primo disegno.
- **Proposta:** nel costruttore, dopo `InitializeComponent` e prima di `Show`, la parte di `ApplySettings` che non dipende da misure (`ApplyLayout` senza `FitColumns`, `GroupDescriptions`, `ShowZoom`, `Visibility`/larghezze di `ShowFolders`); `FitColumns` e `FitBars` (che vogliono `ActualWidth` > 0) restano dopo il primo layout. In alternativa i valori più frequenti già nell'XAML (riquadro a destra, cartelle aperte).
- **Verifica su Windows:** contare i `LayoutUpdated` di `MainWindow` dal costruttore al log "primo disegno" e cronometrare `ApplyLayout` + `ShowFolders` con `Stopwatch`, prima e dopo.

### A-07 `MainWindow.xaml` crea subito molti elementi che si vedono dopo o mai
- **Dove:** `UI/MainWindow.xaml`: `ViewBar` (`:316-352`, Collapsed, nove pulsanti con `StackPanel` e due `TextBlock` ciascuno); `SuggestPopup` (`:182-245`, quattro `RadioButton`, un `ListBox` con stile e modello); `JumpPopup` (`:654-668`, un `Calendar`); `OutboxBar`, `SendBar`, `UndoBar` (`:972-1018`, Collapsed, con `DropShadowEffect`); il vecchio `ItemTemplate` della riga (`:795-950`, 155 righe) che il costruttore sostituisce subito (`MainWindow.xaml.cs:43`); `FavoritesList`/`UnifiedList` e i loro modelli (`:413-484`) anche senza Preferiti o account multipli.
- **Scenario:** `InitializeComponent` istanzia gli elementi, risolve gli stili e analizza i BAML dei modelli anche per le parti Collapsed e per il contenuto dei `Popup` (che si crea subito anche se non si apre). I modelli Fluent dei controlli Collapsed non si applicano (nessun `Measure`), quindi il peso è per oggetto e per risoluzione di stile. Il vecchio `ItemTemplate` resta per `MISSIVA_RIGA_VECCHIA`, che CLAUDE.md dice "da togliere quando A ha finito".
- **Gravità:** Bassa-Media, da misurare: il totale delle parti nascoste non spiega i 257 ms da solo (A-02). Non cresce.
- **Stato:** SOSPETTO: nessuna misura per gruppo.
- **Proposta, dal più sicuro:** (1) togliere il vecchio `ItemTemplate` dall'XAML e mettere `MessageRowNew` come `ItemTemplate` nell'XAML, il vecchio solo dietro `MISSIVA_RIGA_VECCHIA`; (2) creare `SuggestPopup`, `JumpPopup` e le tre barre al primo uso, come già per WebView2 (`ReadingView.CreateWeb`); (3) `ViewBar` alla prima selezione di Visualizza (`FitBars`/`CommandOverflow` assumono che `ViewCommands` esista: da proteggere).
- **Verifica su Windows:** bisezione a freddo, 5 avvii per variante: XAML intero; senza i tre Popup/barre; senza `ViewBar`; senza il vecchio `ItemTemplate`; misura da "OnStartup" a "XAML della finestra".

### A-08 `LoadAccountsAsync` legge la copia locale di tutti gli account prima di mostrare la Posta in arrivo
- **Dove:** `UI/MainWindow.xaml.cs:891-935` (ciclo sincrono su `S.Accounts` con `MailCache.LoadFolders` per ogni account), `Core/MailCache.cs:30-31,120-134` (`File.Exists`, `File.ReadAllBytes`, `ProtectedData.Unprotect`, deserializzatore generato), `MainWindow.xaml.cs:2142-2146` (`LoadMessages`, `ApplyWoken`, `ActionQueue.Apply` sincroni).
- **Scenario:** con N account il tempo fino alla prima riga comprende N letture di `cartelle.json`, N costruzioni di provider, N `WatchInboxes`, anche se la Posta in arrivo scelta è una sola; `UpdateQueued` può leggere `coda.dat`. Con `--nascosta` il prefisso sincrono di `StartAsync` gira prima di `UpdateTray` (A-14).
- **Gravità:** Media con 3 o più account; lineare nel numero di account e nella dimensione di `cartelle.json` (pochi KB per account, stima). Cartelle a 817 ms ed elenco a 938-1015 ms sono i tempi del mandato.
- **Stato:** CONFERMATO il ciclo sincrono; SOSPETTO quanto della finestra 817 → 938-1015 sia questo.
- **Proposta:** in modo sincrono solo `cartelle.json` e il primo elenco dell'account della Posta in arrivo scelta (si sa dall'ordine di `Settings.Accounts` e dall'ultima scelta); gli altri `LoadFolders` con la lettura e la DPAPI in `Task.Run` e solo `SetFolders` sul thread dell'interfaccia, a `Background` dopo il primo disegno dell'elenco.
- **Verifica su Windows:** con 1, 3 e 5 account, tempo fra "account" e "Posta in arrivo scelta" (righe "tempi: avvio" già presenti, `MainWindow.xaml.cs:916-938`).

### A-09 Dalla finestra nascosta al primo messaggio: WebView2 e la pulizia dell'HTML partono a freddo e dopo il disegno
- **Dove:** `UI/MainWindow.xaml.cs:501-530` (`ShowFromTray`: `Show()` poi `Dispatcher.BeginInvoke(Reader.Prepare, ApplicationIdle)` a `:527`; con un messaggio selezionato chiama `ShowInReaderAsync` e salta `Prepare`), `:451` (`HideToTray` → `Reader.ReleaseWeb`), `UI/ReadingView.xaml.cs:83-98` (`ReleaseWeb` mette `environment = null` alla 97), `:1027-1036` (`SharedEnvironment`: `CoreWebView2Environment.CreateAsync` ogni volta da capo), `:1072-1090` (`InitWebAsync` → `EnsureCoreWebView2Async`), `:456-488` (`Prepare`; la pulizia compilata a `:466`), `:939` (`webReady ??= InitWebAsync()` in `Render`), `UI/TrayIcon.cs:52,85-92` (gestisce solo `NIN_SELECT`, `NIN_KEYSELECT`, `WM_CONTEXTMENU`; con `NOTIFYICON_VERSION_4` Windows manda anche `NIN_POPUPOPEN` al passaggio del mouse).
- **Scenario:** l'utente clicca l'icona o una notifica e, entro un secondo o poco più, un messaggio. Ordine di oggi: icona → `Show()` → disegno → `ApplicationIdle` → `Prepare` crea il controllo, `CreateAsync` (ambiente nuovo, dimenticato da `ReleaseWeb`), `EnsureCoreWebView2Async` avvia i processi, pagina finta. CLAUDE.md: primo messaggio 0,25-0,32 s con WebView2 preparato, 0,6-1,2 s senza. Dopo `--nascosta` non c'è stato nessun `Prepare`: si pagano anche la compilazione di `MailHtml.Prepare` e delle regex `NonBacktracking` (costruite al primo uso, `Core/MailHtml.cs:737-740, 818, 833`).
- **Gravità:** Media: è il costo visibile della riapertura dall'area di notifica, solo se si clicca un messaggio prima che `Prepare` finisca. Non cresce con i dati.
- **Stato:** CONFERMATO l'ordine e che l'ambiente si ricrea; SOSPETTO ogni risparmio sotto.
- **Proposta, dalla più promettente (nessuna lascia processi WebView2 a riposo):** (1) `Reader.Prepare` direttamente in `ShowFromTray` prima o subito dopo `Show()`, non a `ApplicationIdle`: il browser parte mentre WPF disegna (da verificare che il controllo si crei con la finestra appena mostrata). (2) Non azzerare `environment` in `ReleaseWeb` (`:97`): l'oggetto `CoreWebView2Environment` non dovrebbe avviare il browser (documentazione, non verificato qui) e `CreateAsync` esce dal percorso del primo messaggio; se tenere il riferimento lascia vivo `msedgewebview2.exe`, non si fa. (3) Anticipare solo `SharedEnvironment()` su `NIN_POPUPOPEN` e quando si mostra una notifica di posta a finestra nascosta. (4) Compilare `MailHtml.Prepare` (AngleSharp e regex, senza WebView2) dopo `LayOutHidden` a priorità bassa nel pool: qualche MB di codice compilato a riposo, da misurare.
- **Verifica su Windows:** `memoria.ps1 -Detail` a finestra nascosta per 30 s con (a) comportamento attuale, (b) ambiente tenuto: numero di processi `msedgewebview2` e MB privati; "WebView2 pronto in anticipo" e "clic, pagina mostrata" per un clic a 150, 400 e 800 ms dalla comparsa, prima e dopo la proposta 1.

### A-10 Le righe "tempi: avvio" scrivono sul disco a ogni riga e perturbano le misure della versione di prova
- **Dove:** `Core/Store.cs:1060-1064` (`Log.Startup`), `:1066-1070` (`StartupClock` con `Process.GetCurrentProcess().StartTime`), `:1070-1085` (`Write`: `Directory.CreateDirectory`, `new FileInfo`, `File.AppendAllText` a ogni riga).
- **Scenario:** nelle versioni DEBUG e PROVA ci sono circa quindici righe prima del primo disegno (`Program.cs:72`; `App.cs:23,31,34,52,61,74,121,132`; `MainWindow.xaml.cs:41,105,916,927,932`), ognuna apre e chiude `missiva.log`. Con antivirus o Portmaster sul PC dell'autore ogni apertura può costare più di un'operazione in memoria. I numeri del mandato sono presi così; la versione installata non ha le righe (`[Conditional]`).
- **Gravità:** Bassa: serve a non fidarsi dei dettagli al millisecondo.
- **Stato:** CONFERMATO che le scritture esistono; SOSPETTO l'entità.
- **Proposta:** raccogliere le righe in memoria con il loro istante e scriverle tutte dopo il primo disegno. È anche il primo passo del piano di A.7.
- **Verifica su Windows:** avvio con la scrittura immediata e con la raccolta in memoria, 5 avvii: il "primo disegno" deve restare uguale o scendere.

## A.3 Il file unico da 266 MB e gli assembly

### A-11 Il pacchetto: composito ReadyToRun da 155 MB, e 30 MB che sono la proiezione WinRT di Windows
- **Dove:** `Missiva.csproj`: `PublishSingleFile`, `EnableCompressionInSingleFile` falso, `PublishReadyToRun`, `IncludeNativeLibrariesForSelfExtract`, `SelfContained`, `RuntimeIdentifier` win-x64; `TargetFramework net10.0-windows10.0.19041.0` (porta con sé `Microsoft.Windows.SDK.NET.dll`).
- **Scenario:** a freddo Windows deve mappare dal disco un file da 266 MB; l'antivirus lo controlla alla prima esecuzione dopo ogni installazione (CLAUDE.md: primo avvio di un eseguibile appena compilato, primo disegno 1250 ms contro 766-790). Che cosa contiene, assembly per assembly, è la parte che si poteva misurare qui.
- **Stato: MISURATO** (Linux, SDK 10.0.112, runtime pack 10.0.12, `dotnet publish -c Release`, stesso comando di `installa.ps1`; analisi dell'intestazione del bundle con un piccolo programma Python fuori dal repository; nessuna esecuzione di Missiva).

**Risultati.**

| Pubblicazione | Byte | File | Note |
|---|---|---|---|
| file unico, ReadyToRun (come `installa.ps1`) | 266.450.383 | 1 | coincide con i "266 MB" riferiti dall'autore: il build di qui riproduce il suo |
| file unico, senza ReadyToRun | 176.651.014 | 1 | |
| cartella, ReadyToRun, file non unito | 236.004.186 | 269 | |
| cartella, senza ReadyToRun | 183.501.554 | 269 | |

**Dentro il file unico ReadyToRun** (bundle versione 6, 265 voci): 255 assembly gestiti (244,7 MB), 8 librerie native (11,6 MB), `deps.json` e `runtimeconfig`. L'assembly più grande è **`Missiva.r2r.dll`, 155,4 MB**: nome e dimensione indicano un'immagine ReadyToRun **composita** (framework, WPF, librerie e app compilati insieme; gli altri assembly sono rimasti come IL con riferimenti al composito: `System.Private.CoreLib` 5,6 MB contro 16,0 della cartella, `PresentationFramework` 6,0 contro 15,8). Che sia composita non è verificato aprendo l'intestazione R2R. La cartella, invece, è ReadyToRun assembly per assembly, senza composito: è un'altra struttura, non solo un'altra confezione.

**Dimensioni per assembly nella cartella** (MB, ReadyToRun contro solo IL):

| Assembly | R2R | IL | Differenza |
|---|---|---|---|
| `Microsoft.Windows.SDK.NET.dll` (proiezione WinRT del Windows SDK) | 55,3 | 24,9 | **+30,4** |
| `System.Private.CoreLib.dll` | 16,0 | 16,0 | 0 (già R2R nel framework) |
| `PresentationFramework.dll` | 15,8 | 15,8 | 0 |
| `BouncyCastle.Cryptography.dll` | 12,3 | 5,2 | +7,1 |
| `PresentationCore.dll` | 8,3 | 8,3 | 0 |
| `System.Private.Xml.dll` | 7,8 | 7,8 | 0 |
| `Missiva.dll` | 7,6 | 3,4 | +4,2 |
| `MimeKit.dll` | 3,6 | 1,3 | +2,3 |
| `Microsoft.Identity.Client.dll` | 2,7 | 1,3 | +1,4 |
| `AngleSharp.dll` | 2,6 | 1,1 | +1,5 |
| `MailKit.dll` | 2,5 | 1,0 | +1,5 |
| `SharpVectors.Model.dll` (e altri 6 di SharpVectors) | 1,8 | 1,1 | +0,7 (totale gruppo 3,9 contro 1,9) |
| `WinRT.Runtime.dll` | 1,4 | 0,5 | +0,9 |

Per gruppo, nella cartella R2R (MB, file): framework .NET 90,5 (204); WPF 46,6 (34); proiezione WinRT 56,7 (2); MailKit, MimeKit e BouncyCastle 18,4 (3); Missiva 7,9 (4); MSAL 6,2 (6); SharpVectors 3,9 (7); AngleSharp 2,6 (1); WebView2 1,6 (4); Toolkit delle notifiche 0,3 (1). Il ReadyToRun aggiunge in tutto 52,5 MB sulla cartella, di cui **30,4 MB sono solo `Microsoft.Windows.SDK.NET.dll`**.

**Librerie native del file unico** (estratte in `%TEMP%\.net\Missiva\<id>` al primo avvio per `IncludeNativeLibrariesForSelfExtract`): `D3DCompiler_47_cor3.dll` 4,7 MB, `msalruntime.dll` 3,1, `wpfgfx_cor3.dll` 2,0, `PresentationNative_cor3.dll` 1,2, `WebView2Loader.dll` 0,16 (due volte), `PenImc_cor3.dll` 0,16, `vcruntime140_cor3.dll` 0,12. Il file unico non contiene `mscordbi` e `mscordaccore` (1,3 MB l'uno), che la cartella invece ha.

`Missiva.runtimeconfig.json` non imposta nulla di avvio (nessuna opzione di compilazione a livelli, nessun GC): CLAUDE.md dice che `TieredPGO` spento, GC non concorrente e `GCConserveMemory` sono stati provati senza effetto.

**Che cosa deve leggere Windows per arrivare al primo disegno** (CONFERMATO dal contenuto, SOSPETTO per le quantità lette): il file unico è mappato; le pagine toccate del composito (codice del framework, di WPF e dell'app) e dei metadati si leggono a richiesta; le 8 librerie native si leggono dalla cartella di estrazione (il primo avvio dopo un'installazione le scrive). Quante pagine dei 155 MB del composito si toccano prima del primo disegno non si sa da qui: lo dice `Process Monitor` (A.7).

**Alternative, con pro e contro** (nessuna misurata su Windows):

| Alternativa | Pro | Contro |
|---|---|---|
| Escludere `Microsoft.Windows.SDK.NET.dll` dal ReadyToRun (`PublishReadyToRunExclude`) | -30 MB nel file e nel composito; meno da controllare per l'antivirus | le chiamate WinRT all'avvio (Toolkit delle notifiche, A-05) passerebbero dal JIT: costo da misurare. Se l'assembly è nel composito, serve `PublishReadyToRunCompositeExclusions` |
| Cartella con `Missiva.exe` e le DLL al posto del file unico (non composito) | niente estrazione delle native al primo avvio; l'antivirus controlla ogni file una volta all'installazione, non a ogni avvio; gli aggiornamenti possono toccare solo i file cambiati | circa 100 assembly aperti e mappati all'avvio invece di uno (più `CreateFile` e controlli dell'antivirus a freddo); niente inlining fra assembly; 269 file da installare e disinstallare; `installa.ps1` e `disinstalla.ps1` da adattare |
| ReadyToRun composito solo per gli assembly dell'avvio (framework, WPF, Missiva) e il resto in IL | file più piccolo; meno pagine del composito fuori dal percorso di avvio | MailKit, MimeKit, MSAL, AngleSharp passano dal JIT alla prima apertura: costo alla prima richiesta, da misurare; configurazione delicata |
| Togliere `D3DCompiler_47_cor3.dll` | -4,7 MB nativi da estrarre | WPF lo cerca per gli effetti shader; con `RenderMode` software potrebbe non servire, ma non è verificabile qui. Rischio: effetti (`DropShadowEffect`) che non partono |
| Non estrarre le native ad ogni nuova versione | primo avvio dopo l'installazione più rapido | già così a regime; il costo è solo al primo avvio dopo ogni `installa.ps1` |
| Tagliare con il trimming | file molto più piccolo | non supportato da WPF; da non provare |

**Assembly caricati prima del primo disegno** (analisi statica; il caricamento effettivo si conferma con ETW, A.7). I Task di avvio (`WarmUp`, `Preconnect`, `Preload`) caricano le loro librerie su thread del pool, fuori dal percorso del disegno; quello che pesa è ciò che si carica sul thread dell'interfaccia:

| Libreria | Chi la tira dentro all'avvio | Thread | Prima del primo disegno? | Rimandabile? |
|---|---|---|---|---|
| Toolkit delle notifiche + proiezione WinRT + `WinRT.Runtime` | `UI/App.cs:91,114` | interfaccia | sì (CONFERMATO) | sì (A-05) |
| `System.Security.Cryptography.X509Certificates`, `System.Net.Http` | statici di `SenderLogos` (`Core/SenderLogos.cs:401-412, 562`) via `Logos` | interfaccia | sì (CONFERMATO nel codice) | sì (A-04) |
| `Microsoft.Identity.Client` | statici di `MicrosoftAuth` (`Core/MicrosoftAuth.cs:37,43,50,56,82,85,159`), toccati da `UI/App.cs:103` | interfaccia | probabile (SOSPETTO) | sì (A-12) |
| MailKit | `GmailProvider.early` (`Core/GmailProvider.cs:63,75`) in `Preconnect`, solo con account Gmail | interfaccia | probabile (SOSPETTO) | sì (A-12) |
| `System.Security.Cryptography.ProtectedData` | `Store.Load` | interfaccia | sì | no |
| `System.Text.Json` | contesti generati | interfaccia | sì | no |
| AngleSharp | `MailHtml.Prepare` (pool, in `Prepare`), `HtmlToFlow`, ricerca icone | pool | no: `MailHtml` non ha statici di AngleSharp e `PlainPreview` (per ogni riga della copia, `MailCache.cs:41`) non lo carica | già pigro |
| SharpVectors | solo `Core/SenderLogos.cs:685-690` | thread STA dedicato | no | già pigro |
| WebView2 (Core e Wpf) | `ReadingView.Prepare`, `CreateWeb` | interfaccia | non dovrebbe (SOSPETTO: fixup R2R) | già pigro |
| MimeKit / BouncyCastle | dentro `ImapEngine` di MailKit e metodi eseguiti più tardi | pool | MimeKit probabilmente non sul thread dell'interfaccia; per BouncyCastle nessun riferimento diretto nel codice (da confermare) | n/d |
| `msalruntime.dll`, `.Broker`, `.Extensions.Msal` | `MicrosoftAuth.AppAsync` (`:127-133`) da `SilentAsync` | pool | no | già fuori |

Con il ReadyToRun, il runtime può caricare gli assembly dei tipi che compaiono nelle firme dei metodi preparati: per questo molti caricamenti sono SOSPETTI e non CONFERMATI.

- **Gravità:** Media come insieme (il file e il composito sono la parte del costo a freddo che nessun correttore di codice tocca); le singole voci sono Basse.
- **Proposta:** (1) provare `PublishReadyToRunExclude` su `Microsoft.Windows.SDK.NET.dll` e misurare file, primo avvio dopo installazione e avvio a freddo; (2) provare la cartella al posto del file unico come esperimento A/B a freddo; (3) A-04, A-05 e A-12 per ciò che si carica sul thread dell'interfaccia.
- **Verifica su Windows:** A.7, in particolare PerfView con i caricamenti del runtime e Process Monitor sulle letture di `Missiva.exe` e su `%TEMP%\.net`.

### A-12 Costruttori statici che portano MSAL e MailKit sul thread dell'interfaccia
- **Dove:** `Core/MicrosoftAuth.cs:37,43` (`Owner`, `Quiet` con inizializzatore), `:50` (`AsyncLocal`), `:56`, `:82`, `:85` (`Dictionary<string, IPublicClientApplication>`), `:159` (`ConcurrentDictionary<string, AuthenticationResult>`), `:274-292`; `UI/App.cs:103` (`MicrosoftAuth.Quiet = ...`, prima di `new MainWindow()`). `Core/GmailProvider.cs:63` (`ConcurrentDictionary<string, Task<ImapClient?>>`) e `:75` (`early[account.Id] = Task.Run(...)` nel ciclo di `Preconnect`, eseguito da `UI/App.cs:59`).
- **Scenario:** scrivere `MicrosoftAuth.Quiet` esegue il costruttore statico di `MicrosoftAuth` sul thread dell'interfaccia (o su quello del pool di `WarmUp`, chi arriva prima; l'altro aspetta): istanze di tipi generici chiusi su tipi di MSAL caricano `Microsoft.Identity.Client.dll` anche per chi non ha account Microsoft. Per Gmail, `Preconnect` indicizza `early` sul thread dell'interfaccia: costruttore statico di `GmailProvider` e caricamento di `MailKit.dll` prima del primo disegno; poi `new GmailProvider` costruisce cinque `ImapClient` per account (`GmailProvider.cs:103,108,1498`) nel prefisso sincrono di `StartAsync`.
- **Gravità:** Bassa-Media; costo fisso per assembly caricato.
- **Stato:** CONFERMATO che gli statici ci sono e dove girano; SOSPETTO che il caricamento avvenga in quel momento (dipende dal runtime e dai fixup R2R).
- **Proposta:** i campi di tipo MSAL (`apps`, `tokens`, `cache`, `AppAsync`) in una classe annidata `MsalState`, lasciando in `MicrosoftAuth` solo tipi BCL; `Quiet`/`Owner` assegnati dopo il primo disegno (le richieste prima del primo disegno sono già `InBackground`). Per Gmail la riga 75 e il dizionario in un metodo `[MethodImpl(NoInlining)]` chiamato dal pool: `Task.Run(() => Early.Start(accounts))`; il dizionario è usato solo da `RunAsync` (`:179`).
- **Verifica su Windows:** ETW (A.7) dei caricamenti di `Microsoft.Identity.Client.dll`, `MailKit.dll` e `MimeKit.dll` con thread e istante, contro "finestra creata" e "primo disegno".

## A.4 Avvio con Windows (`--nascosta`, `App.ApplyStartWithWindows`)

Cronologia di ciò che parte, CONFERMATA nel codice. Non c'è nessuna differenza di comportamento fra `--nascosta` all'accesso e `--nascosta` lanciata a mano: nessun punto del codice scagliona in funzione dell'avvio automatico.

**t = 0, thread dell'interfaccia (`UI/App.cs`, `OnStartup`):** stili, `WheelScrolling.Enable`, `Backup.ApplyPending`, `Store.Load` (28-39); `MicrosoftAuth.WarmUp` (58: un `Task.Run` per account Microsoft con `HomeAccountId`, tiene `TokenGate` per tutta la durata, `Core/MicrosoftAuth.cs:187-214`); `GmailProvider.Preconnect` (59: TLS e XOAUTH2 verso il server IMAP di Gmail, `Core/GmailProvider.cs:71-96`); `AddressBook.Preload` (62); `Safety.CleanTemp` sincrono (63); toast, `SystemEvents`, `new MainWindow()` (91-119); `EnsureHandle` e `_ = main.StartAsync()` (122-127). `StartAsync` (`MainWindow.xaml.cs:95`) non aspetta `rendered` perché la finestra non è visibile (110). Il suo pezzo sincrono: `ApplySettings`, `ShowFolders` (103-104); `LoadAccountsAsync` (891) che costruisce i provider (Gmail: cinque `new ImapClient()`), `UpdateQueued`, `App.SetProviders` (`UI/App.cs:658-664`: `ScanSentLaterAsync` con `Task.Delay` di 15 s e `StartWatchers` 696-716, che lancia **subito** l'IDLE di ogni Gmail/IMAP in `Task.Run` e, per Graph, `CheckDeltasAsync` come punto di partenza della delta), la copia locale delle cartelle e della prima pagina (DPAPI, JSON, 50 `PlainPreview`, 50 `MessageItem` con `SenderLogos.Note`), `SelectFolder`; solo dopo `await Task.WhenAll(accounts.Select(LoadFoldersAsync))` (944). Solo dopo `StartAsync`: `UpdateTray` (App.cs:134), `ListenForMailto` (158-161), timer da 2 minuti (168-174), `NetworkAddressChanged` (183).

**Quando `LoadFoldersAsync` ha finito** (`MainWindow.xaml.cs:118-148`): `snoozeTimer` da 1 minuto con `CheckSnoozedAsync`, `SendScheduledAsync`, `CheckFlagReminders`, `CleanTemp(1 h)`; `CheckSnoozedAsync` e `SendScheduledAsync` subito; `StartReminders` (timer da 30 s); `StartOfflineCopy` (timer da 1 minuto); `LayOutHidden` a `ApplicationIdle`. Da `LoadFoldersAsync` (1070-1097), per ogni account: categorie, risposte automatiche, `ReplayQueueAsync`. Da `ReloadAsync`: anteprime di Gmail sulla seconda connessione e i primi 10 corpi (2269-2340) con `BodyCache.Save`: tutto gira anche a finestra nascosta.

**Dopo t = 0:**

| Quando | Che cosa | Dove | Risorsa |
|---|---|---|---|
| +2 s | `AuthenticateFromCopies`: dopo la prima volta un `File.Exists` | `App.cs:69-73` | trascurabile |
| +15 s | `ScanSentAsync` account per account (Posta inviata sulla connessione in sottofondo, che serve anche anteprime e anticipi), poi `ContactBook.RefreshRemoteAsync` (solo con consenso già dato, salta se la copia ha meno di 12 ore) | `App.cs:667-677`, `Core/AddressBook.cs:484-515`, `Core/Contacts.cs:292-308` | rete, pool |
| +20 s | `EncryptOld`: legge per intero ogni `.json` della cache per guardarne il primo byte | `Core/MailCache.cs:80-103` | disco |
| +30 s | `TrimAll`: due enumerazioni di tutti i `.bin` dei corpi e ordinamento per data, sotto un lucchetto | `Core/Offline.cs:449-504` | disco |
| 20-30 s dopo la fine di `LoadFoldersAsync` | primo giro dei promemoria del calendario (`nextCalendarFetch` a costruzione + 20 s, ma il timer parte al punto precedente con periodo 30 s) | `MainWindow.Calendar.cs:116-118,128-134,145-166` | rete |
| +1 minuto dal punto precedente | primo tick di `offlineTimer`: con la finestra nascosta `WindowIdle` è vero (`MainWindow.Offline.cs:297`), quindi parte subito il primo giro della copia dei 30 giorni, un account alla volta; CLAUDE.md: Gmail 376 corpi in 88-94 s, l'account Outlook personale 47 in 4-5 s, 37 MB su disco, e una raccolta compattata alla fine | `MainWindow.Offline.cs:251-335`, `Core/Offline.cs:535-583` | rete, disco, DPAPI |
| +2 minuti | primo `CheckNewAsync(periodic)` per gli account non "Pushed" | `App.cs:168-174, 834-861` | rete |
| ogni 30 s | delta di Graph | `App.cs:709-715, 764-794` | rete |

Non c'è nessun controllo dei nuovi messaggi all'avvio: `lastCheck` parte da "adesso" (`App.cs:661`). La posta arrivata a PC spento compare in elenco ma non dà notifica: è una scelta di progetto, non un difetto di velocità, ma è da conoscere.

**Come si accavalla.** Nei primi 1-2 s partono insieme `WarmUp`, `Preconnect`, `Preload`, le connessioni IMAP di ogni account (principale adottata da `Preconnect`, sottofondo, IDLE), la prima pagina dal server, le anteprime e il prefetch dei 10 corpi. La manutenzione di disco (`EncryptOld`, `TrimAll`) è scaglionata a +20 e +30 s, proprio quando Windows ha ancora l'avvio di altre applicazioni in corso; il primo giro della copia dei 30 giorni cade 1-2 minuti dopo l'accesso.

### A-13 I timer di Posponi, invii pianificati, promemoria e copia dei 30 giorni partono solo quando tutte le cartelle sono state lette o sono fallite
- **Dove:** `UI/MainWindow.xaml.cs:944` (`await Task.WhenAll(accounts.Select(LoadFoldersAsync))` in `LoadAccountsAsync`, chiamato da `StartAsync` a `:118`); le righe `:120-148` (`snoozeTimer`, `SendScheduledAsync`, `StartReminders`, `StartOfflineCopy`, `LayOutHidden`) vengono dopo.
- **Scenario:** accesso a Windows con Wi-Fi o VPN non ancora pronti, o con un firewall (CLAUDE.md cita Portmaster) che trattiene i collegamenti dei programmi nuovi, o una richiesta Graph in attesa di `TokenGate` (`WarmUp` lo tiene durante `AcquireTokenSilent`). Se la connessione fallisce subito, il ritardo è trascurabile. Se resta appesa, aspetta il timeout: 60 s per richiesta per Graph (`Core/GraphProvider.cs:39`), 120 s per MailKit di Gmail (`Core/GmailProvider.cs:30`), 60 s per IMAP (`Core/ImapProvider.cs:32`), MSAL con il suo. Fino ad allora: nessun ritorno da Posponi, nessun invio pianificato, nessun promemoria del calendario, nessuna copia dei 30 giorni, nessuna disposizione della finestra.
- **Gravità:** Media quando succede (raro ma alla cieca, all'accesso); costo pari al timeout più lungo fra gli account; cresce con il numero di account (la durata è del più lento).
- **Stato:** CONFERMATO (struttura); SOSPETTO la durata reale con firewall o VPN.
- **Proposta:** avviare timer e promemoria prima di aspettare le cartelle, oppure fare il `WhenAll` dopo averli avviati: le funzioni controllano già `Special(account, Inbox)` null e `KnownOffline`; il codice dopo la riga 944 è solo `if (folder == null) SelectStart(); accountsReady.TrySetResult();` e non dipende dai timer.
- **Verifica su Windows:** `prova.ps1 -Release`, rete staccata al logon e riattaccata dopo 60 s; la riga "tempi: avvio, account" contro la prima riga di "ritorno da Posponi" o "copia degli ultimi 30 giorni"; oppure un firewall che scarta i pacchetti verso il server IMAP di Gmail.

### A-14 In avvio nascosto l'icona compare dopo il lavoro sincrono, e la Posta in arrivo si rilegge dal server per una finestra che nessuno guarda
- **Dove:** `UI/App.cs:122-127` (`_ = main.StartAsync()`) e `:134` (`UpdateTray()`); il pezzo sincrono di `StartAsync` è `MainWindow.xaml.cs:102-118` e `LoadAccountsAsync` 891-944. Server: `:1059` (`SelectInbox`) → `SelectFolder` → `ReloadAsync`/`LoadMoreAsync` (2126-2182), `FillPreviewsAsync` (2269-2297), `PrefetchListAsync` (2320-2340) con `BodyCache.Save` per corpo. `LayOutHidden`: `:148, 476-484`.
- **Scenario:** (a) all'accesso, prima dell'icona il thread dell'interfaccia costruisce i provider, legge e decifra la copia locale, fa `SelectFolder` (A-08): l'utente non vede Missiva nell'area di notifica finché non finisce. (b) Per ogni account, in parallelo con `Preconnect`, cartelle, IDLE e delta: la prima pagina dal server, le anteprime di Gmail e dieci `BODY.PEEK`. Serve alla prima apertura veloce e ai corpi su disco (poi `Has` li salta nel giro dei 30 giorni), ma è lavoro di rete e disco al momento peggiore. (c) `LayOutHidden` (`Measure` + `Arrange` dell'intero contenuto a `ApplicationIdle`, appena la coda è vuota: pochi secondi dopo l'accesso) costa 0,4-0,5 s di CPU sul thread dell'interfaccia (CLAUDE.md).
- **Gravità:** Bassa-Media (picco di connessioni e DPAPI nei primi secondi, non cresce); (a) Bassa.
- **Stato:** CONFERMATO il percorso; SOSPETTO il guadagno di rimandare (la prima apertura dall'area di notifica è già 99-150 ms con l'elenco pronto).
- **Proposta:** (a) `UpdateTray()` prima di `main.StartAsync()` (`App.cs:126-127`): crea solo un `HwndSource`, e `TaskbarCreated` copre il caso di Esplora non pronto. (b) In avvio nascosto leggere solo la copia locale (righe dalla cache, `:2139-2147`) e rimandare pagina dal server, anteprime e prefetch a +60-120 s o alla prima apertura (`ShowFromTray` ricarica già la selezione, `:521-527`); IDLE e delta tengono aggiornato l'elenco (`AddIncoming`). (c) `LayOutHidden` a +30-60 s dall'avvio o alla prima apertura del menu dell'area di notifica.
- **Verifica su Windows:** `Log.Startup` "icona nell'area di notifica" contro "finestra creata"; righe "tempi: elenco di ..." e "copia su disco di N messaggi scaricati in anticipo" nei primi 10 s; prima apertura dall'area di notifica con e senza (b); riga "finestra disposta senza mostrarla" e Resource Monitor nei primi 10 s.

### A-15 Dopo un guasto di rete all'avvio, lo stato "Non in linea" o "Non raggiungibile" non si ripulisce da solo e le cartelle non si rileggono
- **Dove:** `Core/Offline.cs:44-48` (`Network.Note`), `:58-64` (`KnownOffline`); `UI/App.cs:764-794` (`CheckDeltasAsync` non chiama `Network.Note` né `NoteAccountState`); `Core/Idle.cs:161-197` (nemmeno `RunAsync`); `UI/MainWindow.xaml.cs:1079,1093-1094` (stato impostato), `:1103-1128` (`NoteAccountState`, l'unico punto che ripulisce `Status`, ricarica le cartelle e fa ripartire la coda), `:1110-1116` (la condizione è `item.Status.StartsWith("Non in linea")`), `Core/Offline.cs:17,33-38` (`Network.Explain` riconosce solo i `SocketException` 10013, 10050, 10051, 10060, 10064, 10065, 11001, 11002, 11004); `UI/MainWindow.Offline.cs:27-36` (`RunOrQueueAsync`).
- **Scenario:** la rete torna 10-30 s dopo l'accesso. (1) L'account resta "non in linea" finché non riesce un controllo dei nuovi messaggi: né la delta di Graph (al giro dopo, 30 s) né un IDLE ricollegato lo notano; in quella finestra Elimina, Archivia, Sposta, Già letto e Contrassegna vanno in coda invece di partire ("N azioni in attesa"). Si ripianano da sole al primo controllo riuscito (`OnNetworkChanged` 5 s dopo l'evento di rete, `App.cs:203-212`, se `KnownOffline` o la coda è piena; oppure il tick a 2 minuti), ma fino a ~2 minuti dall'avvio l'utente vede l'azione "in sospeso" con la rete già tornata. (2) Una connessione resettata (10054) o rifiutata (10061), un `TimeoutException`, un `TaskCanceledException`, un handshake TLS di una rete con portale, un'eccezione di MSAL producono tutti "Non raggiungibile: <testo>" (`:1094`): al primo controllo riuscito `NoteAccountState(null)` non entra nel ramo di `LoadFoldersAsync`, quindi albero delle cartelle, conteggi, categorie e risposte automatiche restano quelli della copia fino al riavvio o a un'azione che ricarica le cartelle; `OnNetworkChanged` non fa nulla per questi account perché `KnownOffline` non è vero.
- **Gravità:** Media (stato sbagliato visibile per la sessione, caso 2); Media per ~2 minuti (caso 1). Non cresce.
- **Stato:** CONFERMATO (logica). SOSPETTO quali errori escano davvero all'accesso: lo dice il registro ("cartelle di ...").
- **Proposta:** `Network.Note(p.Account, null)` e `MainWindow.NoteAccountState(p, null)` a ogni delta riuscita (`App.cs:775`) e a ogni sessione IDLE collegata; un flag `FoldersFailed` per `AccountItem`, impostato nel `catch` di `LoadFoldersAsync`, che `NoteAccountState(null)` usa per rileggere le cartelle oltre al caso "Non in linea"; `Network.IsOffline` esteso a `TimeoutException` e ai codici 10054 e 10061 per la sola marca "non in linea".
- **Verifica su Windows:** avvio con rete staccata e riattaccata dopo 20 s; Elimina un messaggio appena la delta o l'IDLE hanno scritto la loro riga (la riga "l'azione va in coda", `MainWindow.Offline.cs:46`); poi controllare se la riga sotto l'account dice ancora "Non raggiungibile" dopo un "controllo di ... (controllo di ogni due minuti)" riuscito.

### A-16 L'IDLE ripreso con attese crescenti non si sveglia al ritorno della rete
- **Dove:** `Core/Idle.cs:116-117,161-197` (`Task.Delay(wait)` a 191-194); `UI/App.cs:203-212` (`OnNetworkChanged` non tocca `watchers`).
- **Scenario:** accesso senza rete: tentativi a 0, 5, 15, 35, 75, 155, 315 s. Se la rete torna a 80 s il prossimo tentativo è a 155 s; se torna a 160 s è a 315 s. Per Gmail l'errore subito (`SocketException`) non è catturato dal riprova interno di `GmailProvider.RunAsync`, che riguarda `IOException`, `ServiceNotConnectedException`, `ImapProtocolException` (`Core/GmailProvider.cs:213`). Nel frattempo `Pushed` è falso: il controllo di ogni due minuti include l'account, quindi la notifica non si perde, arriva col ritardo di prima dell'IDLE.
- **Gravità:** Bassa-Media (ritardo di notifica fino a 2 minuti nella finestra); non cresce.
- **Stato:** CONFERMATO.
- **Proposta:** un evento "riprova subito" per account in `InboxIdle`, azionato da `OnNetworkChanged` (dopo i suoi 5 s) e da `NoteAccountState(null)`; il ciclo azzera `wait` a `FirstRetry`.
- **Verifica su Windows:** le righe del registro "IDLE su INBOX ... interrotto ... di nuovo fra N s" e "i nuovi messaggi arrivano subito" dopo la riattivazione del Wi-Fi.

### A-17 Tentativi unici che non si ripetono: promemoria del calendario, contatti di Outlook, Posponi
- **Dove:** `UI/MainWindow.Calendar.cs:116-118` (`nextCalendarFetch = Now + 20 s`), `:151-164` (`nextCalendarFetch = Now + 30 min` **prima** di leggere; il `catch` scrive `Log.Error` con lo stack e va avanti), `:168-171` (`upcoming` vuoto; non usa la copia locale `Calendars.Save/Load` che usa la vista). `UI/App.cs:662,667-677` (`ScanSentLaterAsync`: unico punto che chiama `RefreshRemoteAsync`), `Core/Contacts.cs:292-308` (su errore solo `Log.Write`). `UI/MainWindow.xaml.cs:3932` (`CheckSnoozedAsync`), `catch` a `3975-3978` (`Log.Error("ritorno da Posponi", ex)`), chiamato dal `snoozeTimer` ogni minuto (`:133,141,3791`); `Log.Error` in `Core/Store.cs:1017-1023`, rotazione a 1 MB in `:1081`.
- **Scenario:** (1) avvio senza rete, rete a 30 s: gli eventi con promemoria nella mezz'ora successiva non notificano, perché la lettura è andata persa e il prossimo tentativo è a 30 minuti; `OnNetworkChanged` non la riavvia. (2) A +15 s senza rete la copia dei contatti di Outlook non si aggiorna per la sessione (a meno di aprire Persone, che la scrive). La Posta inviata della rubrica invece si ripete dal controllo dei nuovi messaggi (`App.cs:952`). (3) Un messaggio posposto scaduto con il PC senza rete (notte, VPN) o con l'account che chiede un nuovo accesso: `WakeAsync` solleva, il `catch` scrive l'eccezione intera con lo stack ogni minuto, per ogni messaggio scaduto, senza controllo di `Network.KnownOffline` né deduplica (la query delta ha `deltaErrorLogged`, `App.cs:689`; la barra dell'account `accountFixLogged`, `:797`). Ogni riga apre e chiude il file di registro sotto un lucchetto globale (`Store.cs:1075-1083`) sul thread dell'interfaccia; con la rotazione a 1 MB e un solo `.old`, una notte di errori può spingere fuori le righe utili del giorno prima.
- **Gravità:** (1) Media per l'utente (promemoria perso), costo nullo; (2) Bassa; (3) Bassa-Media (rumore, scritture, diagnosi). Il registro cresce linearmente con il tempo senza rete e con i messaggi posposti scaduti.
- **Stato:** CONFERMATO (1)-(3) nel codice; SOSPETTO che `WakeAsync` sollevi sempre senza rete (Graph: `UnreadSync.WriteAsync`, `Core/GraphProvider.cs:995`; Gmail e IMAP: `RunAsync`, `GmailProvider.cs:1205`, `ImapProvider.cs:1425`).
- **Proposta:** (1) su errore `nextCalendarFetch = Now + 1 min` (o riprova da `NoteAccountState(null)` e `OnNetworkChanged`), `upcoming` riempito dalla copia locale della settimana quando la rete manca, `Network.IsOffline(ex)` con una riga sola; (2) `RefreshRemoteAsync` anche al primo `CheckAccountAsync` riuscito per l'account (la guardia di 12 ore c'è già); (3) `if (Network.IsOffline(ex) || MailException.FixOf(ex) != None)` salta il giro e scrive una riga per motivo e account, come `queueWaitTold`.
- **Verifica su Windows:** (1) un evento con promemoria a +10 minuti, avvio senza rete, rete a 60 s; (2) data di modifica di `cache\contatti-<account>.dat` dopo 3 minuti; (3) rete staccata 10 minuti con un posposto scaduto, contare le righe "ERRORE (ritorno da Posponi)".

### A-18 Primo giro della copia dei 30 giorni a ~1 minuto dall'accesso, e riprova a 30 minuti per gli errori non riconosciuti
- **Dove:** `UI/MainWindow.Offline.cs:251-263` (timer da 1 minuto), `:297` (`WindowIdle` vero se non visibile), `:299-327` (`due` salta gli account `KnownOffline`; nel `catch` generico `317-322` `offlineDone[...] = Now`, cioè prossimo tentativo fra `OfflinePass` = 30 min); `Core/Offline.cs:535-583` (`RunAsync`).
- **Scenario:** (a) il giro più pesante in sottofondo (rete, DPAPI, centinaia di file; Gmail 376 corpi in 88-94 s, CLAUDE.md) cade 1-2 minuti dopo l'accesso, in concorrenza con il resto dell'avvio di Windows. (b) Se `GetRecentKeysAsync` fallisce con un errore non riconosciuto come "non in linea" (A-15), l'account non rientra per mezz'ora.
- **Gravità:** Bassa-Media: (a) competizione per disco e CPU all'accesso, (b) copia in ritardo; nessuna perdita.
- **Stato:** CONFERMATO tempistica e riprova; SOSPETTO l'impatto percepito.
- **Proposta:** (a) per gli avvii in autostart un ritardo minimo del primo giro di 3-5 minuti dall'avvio del processo (un account alla volta come ora); (b) su qualunque errore diverso da `Network.IsOffline`, `offlineDone = Now - OfflinePass + 2 min`.
- **Verifica su Windows:** ETW o Resource Monitor nei primi 3 minuti dopo il logon, con e senza il ritardo; righe "copia degli ultimi 30 giorni" nel registro.

### A-19 Rete che non cade ma non risponde: i lucchetti dei token accodano i timeout, e un controllo fermo ne blocca altri
- **Dove:** `Core/GoogleAuth.cs:109` (Http con timeout di 30 s), `:112` (`Gate` unico per tutti gli account), `:269-309` (il `Gate` è tenuto durante `PostTokenAsync(..., CancellationToken.None)`, `:287-293`); `Core/MicrosoftAuth.cs:82,193-214` (`TokenGate` tenuto durante `AcquireTokenSilent`), `:161-177`; `:216-259` (`FreshTokenAsync`: `catch (MsalUiRequiredException)` a `:230` → con `MayAsk` falso `MailException` con `Fix = SignIn`, `:238-239`; con finestra visibile e non in sottofondo apre la finestra di Windows, `:241-246`); `UI/App.cs:836` (`if (checking) return;`), `:847-853` (`Task.WhenAny` con `Task.Delay(StuckCheck)`), `:808` (`StuckCheck` 2 minuti), `:203-212`.
- **Scenario:** portale captive, VPN che sta salendo, firewall in attesa di decisione. Gmail ha quattro-cinque connessioni, ognuna chiede il token passando dal `Gate`: i timeout da 30 s si accodano uno dopo l'altro, senza un "fallimento ricordato" per qualche secondo; lo stesso per Microsoft con `TokenGate`. Se WAM o MSAL, senza rete, rispondono con `MsalUiRequiredException` invece di un errore di servizio, compare la barra falsa "Accedi di nuovo" (si ripulisce al primo successo, `MainWindow.xaml.cs:1123-1127`) e, con la finestra visibile e non in autostart, si apre la finestra account di Windows senza rete. Il primo controllo periodico (+2 minuti) resta fermo sul primo account fino a 2 minuti, poi sul secondo: con 3 account fino a 6 minuti con `checking` vero; un `OnNetworkChanged` in quel periodo vede `checking` e ritorna senza riprovare né scrivere (`networkCheckPending` è già falso, `:208`).
- **Gravità:** Media se succede (barra o finestra false, ritardi di minuti), Bassa se la rete cade subito.
- **Stato:** CONFERMATI i lucchetti, i timeout e la logica; SOSPETTO che eccezione dia WAM senza rete (il tipo è già scritto nel registro alla riga "token MSAL ... non preso in anticipo", `MicrosoftAuth.cs:209`) e quanto duri il blocco con un firewall.
- **Proposta:** ricordare l'errore di rete di `PostTokenAsync`/`AcquireTokenSilent` per 5-10 s e rilanciarlo subito ai waiter già in coda; in `FreshTokenAsync` non convertire in `Fix SignIn` un errore con `SocketException`/`HttpRequestException` dentro e verificare `NetworkInterface.GetIsNetworkAvailable()` prima di aprire la finestra interattiva; i controlli degli account in parallelo (`CheckAccountAsync` è già protetto per account da `checkingAccounts`) o un `checkAgain` globale che `OnNetworkChanged` imposta quando trova `checking` vero.
- **Verifica su Windows:** Wi-Fi collegato a una rete senza Internet (portale simulato), avvio, registro "token pronto" e "non preso in anticipo (Tipo)"; modalità aereo all'avvio e barra sotto l'account Microsoft; "controllo dei nuovi messaggi di ... fermo da più di 2 minuti".

### A-20 La manutenzione di disco a +20 e +30 s rilegge più del necessario
- **Dove:** `Core/MailCache.cs:80-103` (`EncryptOld`: `File.ReadAllBytes` per intero di ogni `.json` di `cache`, a ogni avvio; segnalato in velocita.md come V-23 tra i costi minori e non corretto, non compare in ESITO.md), `Core/Offline.cs:490-504,449-486` (`TrimAll`: `GetFiles` di tutti i `.bin`, sotto un lucchetto), `UI/App.cs:63` (`CleanTemp(1 giorno)` sincrono) contro `MainWindow.xaml.cs:139` (`CleanTemp(1 h)` ogni minuto in `Task.Run`, che comprende già l'altro), `App.cs:65` (`TrimAll(Store.Settings.Accounts)`: la lista viva viene enumerata dal pool 30 s dopo, `accounts.ToList()` a `Offline.cs:493`, fuori dal `try`: una modifica concorrente dell'elenco account dall'interfaccia in quella finestra lancerebbe `InvalidOperationException` non osservata).
- **Scenario:** all'accesso, con il disco ancora occupato dal resto di Windows e dall'antivirus, 20-30 s dopo l'avvio: lettura intera di decine di `.json` e enumerazione di migliaia di `.bin` (tetto 200 MB per account).
- **Gravità:** Bassa; cresce con il numero dei `.json` e dei corpi.
- **Stato:** CONFERMATO il codice; SOSPETTO il costo.
- **Proposta:** `EncryptOld` con un segno "fatto" (come `MigrationMark` di `SenderLogos`) o `FileStream.ReadByte` del solo primo byte, senza scendere in `corpi`; togliere `CleanTemp(1 giorno)` da `OnStartup` (il timer a un minuto fa lo stesso in `Task.Run`); passare a `TrimAll` una copia (`.ToList()` subito); `TrimAll` a +2-3 minuti e dopo il primo giro della copia.
- **Verifica su Windows:** Process Monitor sulla cartella dei dati a +20..+40 s dall'avvio: numero di `CreateFile`/`ReadFile` e durata.

### A-21 `BodyCache` costruisce due regex `NonBacktracking` nel costruttore statico anche se la ricerca non in linea non si usa
- **Dove:** `Core/Offline.cs:439-441` (`Blocks` con `Singleline | IgnoreCase`, `Tags`: campi `static readonly` di `BodyCache`, classe a `:137`, che ha anche `Gate` `:175`, `Trimming` `:179`, `sizes` `:185`). Primo accesso a un campo statico sul thread dell'interfaccia: `BodyCache.Save` (`:225`) chiamata da `UI/MainWindow.xaml.cs:2337` (Gmail, dopo l'elenco) e da `UI/ReadingView.xaml.cs:694` (primo messaggio aperto). `UI/App.cs:65` (`TrimAll`) non basta ad anticipare l'inizializzazione perché il corpo usa un `Task.Run`.
- **Scenario:** la regex serve solo a `PlainText` (`:436`), cioè alla ricerca non in linea; il costo di costruzione si paga a ogni sessione, sul primo thread che salva un corpo.
- **Misura (MISURATO, Linux, SDK 10.0.112, tre esecuzioni, solo relativa):** la prima regex `NonBacktracking` del processo costa circa 50-65 ms in più per l'infrastruttura (`Tags`); `Blocks` dopo di lei 18-20 ms; per confronto `OwnDarkMode` 7-8 ms, `CssUrl` 3-7 ms, `CssEscapedFunction` 3-5 ms, `SvgSelector` 2-4 ms. Su Windows il banco di ESITO.md dà lo stesso ordine con cifre più piccole; non rimisurato.
- **Gravità:** Bassa-Media, una volta per sessione.
- **Stato:** CONFERMATO che la costruzione è nel costruttore statico; SOSPETTO quale thread la subisca e quanto pesi con ReadyToRun su Windows.
- **Proposta:** `Blocks` e `Tags` dentro `static class SearchText { internal static readonly Regex Blocks, Tags; }` o in `Lazy<Regex>`: nascono solo alla prima ricerca non in linea. Nessun compromesso sul comportamento.
- **Verifica su Windows:** `Stopwatch` su `RuntimeHelpers.RunClassConstructor(typeof(BodyCache).TypeHandle)` in Release PROVA; `dotnet-trace` sul thread dell'interfaccia al primo `BodyCache.Save`.

### A-22 Il riscaldamento di `MailHtml` non copre `SpacerName` né `CssEscapedFunction`
- **Dove:** il campione in `UI/ReadingView.xaml.cs:456-465` (la `Prepare` di `ReadingView`, dal campione `:463-464`: `<img src="cid:logo">` e uno stile senza backslash); `Core/MailHtml.cs:731-738` (`IsSpacer`/`SpacerName`, richiamata solo per un'immagine remota bloccata che non è un pixel, `:117-121`) e `:843` (`CssEscapedFunction` solo se lo stile contiene "\"). La pulizia gira in `Task.Run` in `ReadingView.FetchBodyAsync` (`:717-722`), prima di `entry.Body = body` e di `Render`: è sul percorso di "testo pronto".
- **Scenario:** il primo messaggio della sessione con immagini esterne bloccate paga la costruzione. Misura Linux relativa (stessa serie di A-21): `SpacerName` 22-35 ms di costruzione (la più cara del gruppo, per `IgnoreCase` con `[\w-]`), più 3-5 ms al primo match.
- **Gravità:** Bassa-Media, una volta per sessione, ma sul primo messaggio con immagini che l'utente apre.
- **Stato:** CONFERMATO che non è nel riscaldamento; costo su Windows SOSPETTO.
- **Proposta:** nel campione un `<img src="https://example.invalid/spacer.gif" width="300">` (non pixel, nome da spaziatore) e un attributo `style` con un backslash, con `allowRemote: false`. In alternativa sostituire `SpacerName` con una scansione a mano che non costruisce nulla, confrontandola con la regex su un insieme di URL (esiste l'harness `HtmlAuditTests`).
- **Verifica su Windows:** riga "tempi: HTML pulito in sottofondo (N KB, da A a B ms)" del primo messaggio con immagini remote, prima e dopo, tre avvii.

## A.5 e A.6 Dalla finestra nascosta alla visibile, e letture dal disco

Il passaggio da finestra nascosta a visibile e il primo messaggio aperto sono in A-09; le letture dal disco all'avvio, con file, dimensioni stimate, numero di DPAPI e thread, sono nella tabella A.1 e nei punti A-03, A-04 e A-08. La deserializzazione JSON misurata qui è in A-03.

## A.7 Piano di misura per Windows

**Obiettivo.** Dire, con numeri, dove vanno i 766-790 ms a caldo e i ~1250 ms del primo avvio, e che cosa cambia a freddo.

### 1. Righe "tempi: avvio" da aggiungere

Un solo meccanismo: `Log.Startup(string)` che mette `(nome, Stopwatch.GetTimestamp())` in una lista in memoria sotto un lucchetto e le scrive tutte dopo il primo disegno (A-10), con l'istante dall'avvio del processo (`Process.GetCurrentProcess().StartTime`, già in `StartupClock`) e l'id del thread. Punti (file:riga di oggi):

| Nome della riga | Dove |
|---|---|
| "Main entrata" | `Program.cs` prima di tutto (prima del mutex) |
| "mutex preso" | `Program.cs:56` dopo il mutex |
| "App costruita" | `UI/App.cs:24` (c'è "tema Fluent": aggiungere anche una riga prima di `ThemeMode = ThemeMode.System`, `:21`) |
| "Backup.ApplyPending", "Store.Load fine (N testi DPAPI)" | `UI/App.cs:37-38` (la seconda c'è già a `:52`; aggiungere il numero di DPAPI e i ms dei soli `Unprotect`, A-03) |
| "tema assegnato 2" | `UI/App.cs:60` (c'è "tema"; segnare se `ThemeMode` cambia davvero valore) |
| "CleanTemp", "toast", "SystemEvents", "SetTextColors" | prima e dopo `UI/App.cs:63, 91, 110, 106-108` |
| "InitializeComponent inizio/fine" | `UI/MainWindow.xaml.cs:38` (c'è "XAML della finestra" alla 41; manca l'inizio) |
| "costruttore MainWindow fine" | fine del costruttore, dopo `Logos.Arrived +=` (`:60`) |
| "SenderLogos statici" | `RuntimeHelpers.RunClassConstructor(typeof(SenderLogos).TypeHandle)` con `Stopwatch` (A-04) |
| "BodyCache statici" | idem su `BodyCache` (A-21) |
| "domini.dat letto (N domini, X ms)" | `Core/SenderLogos.cs:226-240` |
| "LayoutUpdated N" | un contatore di `MainWindow.LayoutUpdated` dalla costruzione al primo disegno (A-06) |
| "ApplyLayout", "ShowFolders", "FitColumns", "FitBars" | `UI/MainWindow.xaml.cs:630, 758, 838, 866` |
| "Show() fine" | `UI/App.cs:131` |
| "primo disegno" | c'è; il suo `ContentRendered` |
| "icona nell'area di notifica" | `UI/App.cs:134` (A-14) |
| "account: cartelle da copia, N" per ogni account | `MainWindow.xaml.cs:924` (A-08) |
| "assembly caricato: nome" | `AppDomain.CurrentDomain.AssemblyLoad` sottoscritto in `Main`, riga con nome, istante e thread (per A-11 e A-12; costo trascurabile; solo in PROVA) |
| "WarmUp inizio/fine", "Preconnect inizio/fine" | `Core/MicrosoftAuth.cs:187-214`, `Core/GmailProvider.cs:71-96` (A-01) |
| "primo giro: <nome>" | prima chiamata di ogni timer (`StartReminders`, `StartOfflineCopy`, `CheckSnoozedAsync`, `EncryptOld`, `TrimAll`, `ScanSentAsync`) con l'istante e la durata (A.4) |

### 2. Come ottenere un avvio davvero a freddo senza riavviare ogni volta

Il "freddo" ha tre componenti separabili: la cache del file system (pagine del file in memoria), i controlli dell'antivirus (di solito per file e per primo accesso) e l'estrazione delle librerie native in `%TEMP%\.net`.

1. **Cache del file system:** RAMMap di Sysinternals da riga di comando, con Missiva chiusa. Da una PowerShell amministrativa:
   `RAMMap64.exe -Ew` (working set), poi `RAMMap64.exe -Es` (working set di sistema), `RAMMap64.exe -Em` (pagine modificate), `RAMMap64.exe -Et` (lista standby) e `RAMMap64.exe -E0` (lista standby a priorità 0). Il file torna dal disco alla prima lettura. Verificare con Process Monitor che le `ReadFile` di `Missiva.exe` abbiano durata di disco (decine di µs invece di pochi µs per lettura dalla cache).
2. **Antivirus:** copiare `Missiva.exe` con un nome nuovo prima di ogni prova (`Copy-Item Missiva.exe "$env:TEMP\prova-$(Get-Random).exe"`): un file mai visto subisce di nuovo il controllo del primo accesso. È anche la condizione di "primo avvio dopo un'installazione" (1250 ms di primo disegno nei dati dell'autore).
3. **Estrazione delle native:** `Remove-Item -Recurse -Force "$env:TEMP\.net\Missiva"` prima dell'avvio.
4. **Tre scenari per ogni variante:** (A) caldo: due avvii di fila; (B) freddo di cache: RAMMap, stesso file; (C) primo avvio: RAMMap, nome nuovo, `.net\Missiva` cancellata. Cinque avvii per scenario, mediana e massimo, con il PC a riposo (chiudere le altre applicazioni; annotare se Portmaster è attivo).
5. **Il caso dell'accesso a Windows, una volta sola:** `wpr -boottrace -addboot GeneralProfile -addboot CPU -addboot DiskIO -addboot FileIO`, riavviare, accedere (con "Avvia Missiva all'accesso a Windows" acceso), poi `wpr -stopboot C:\Temp\accesso.etl`.

### 3. Strumenti e comandi esatti

**WPR + WPA (tempi, CPU, disco):**
```
wpr -start GeneralProfile -start CPU -start DiskIO -start FileIO -filemode
Start-Process "$env:LOCALAPPDATA\Programs\Missiva\Missiva.exe"      # oppure con --nascosta
# attendere la riga "primo disegno" nel registro, poi altri 30 s per i lavori di A.4
wpr -stop C:\Temp\avvio.etl "avvio a freddo"
```
In WPA (Windows Performance Analyzer): *Computation → CPU Usage (Sampled)* filtrato su `Missiva.exe`, raggruppato per thread e per stack: guardare il thread dell'interfaccia (thread principale, il primo dopo `Main`) fino all'istante del primo disegno, e i thread del pool nei primi 30 s. *Storage → File I/O* e *Disk Usage*: byte letti da `Missiva.exe` e dalle DLL native prima del primo disegno, e durata di ogni lettura. *Computation → CPU Usage (Precise) → Waits*: i tempi in attesa del thread dell'interfaccia (DPAPI, file, caricamento).

**PerfView (caricamento degli assembly e JIT):**
```
PerfView.exe /AcceptEULA /NoGui /ThreadTime /Merge:true /Zip:false /BufferSizeMB:512 run "%LOCALAPPDATA%\Programs\Missiva\Missiva.exe"
```
Chiudere Missiva dopo 60 s. In `PerfViewData.etl`: *Events* con filtro `Microsoft-Windows-DotNETRuntime/Loader` (caricamenti di assembly, con istante e thread) e `Microsoft-Windows-DotNETRuntime/Method` (metodi compilati dal JIT, utile per vedere che cosa il ReadyToRun non copre: `MethodJittingStarted` per metodi di Missiva e del framework); *JIT Stats* (numero e tempo del JIT per assembly); *Thread Time Stacks* per il thread dell'interfaccia (dove aspetta, e su che cosa); *CPU Stacks* con `GroupPats`.

**Process Monitor (che cosa tocca del disco):** filtri `Process Name is Missiva.exe` e `Process Name is msedgewebview2.exe`; escludere `Process Profiling` e `Thread Create`; colonna *Duration* attiva. Guardare: le `CreateFile`/`ReadFile` di `Missiva.exe` (quanto del file unico), `%TEMP%\.net\Missiva\*` (estrazione), `%LOCALAPPDATA%\Missiva\*` (impostazioni, cache, loghi, rubrica), le scritture su `missiva.log` (A-10), le chiavi `HKCU\Software\Classes\AppUserModelId\*` e `CLSID\*` (toast, A-05) e le letture del registro per `StartupApproved`. Per l'accesso a Windows: *Options → Enable Boot Logging*, riavvio, accesso, poi salvare il file.

**Che cosa guardare nel risultato (domande, in ordine):**
1. Fra "Main entrata" e "primo disegno", quanto è sul thread dell'interfaccia in CPU, quanto in attesa di disco, quanto in attesa di altro (lucchetti)? Le tre colonne dicono se conviene tagliare lavoro (A-01..A-08) o ridurre il file (A-11).
2. Quanti assembly si caricano sul thread dell'interfaccia prima del primo disegno e quali (A-05, A-12)? Tolti quelli del framework e di WPF, i candidati sono MSAL, MailKit, X509, Http, Toolkit/WinRT.
3. Quanti metodi del JIT prima del primo disegno e di che assembly? Se compaiono metodi di Missiva, il ReadyToRun non li ha coperti (generici su tipi di altri assembly, lambda): si possono precompilare.
4. Quanti byte e quante pagine del file unico si leggono prima del primo disegno, e quanto costa in più la lettura a freddo? Confronto A/B fra file unico e cartella (A-11).
5. I lavori dei primi 30 s (A.4): quando parte ciascuno, quanti byte di disco e quanta CPU, e se coincidono con i picchi del sistema all'accesso.
6. Con `--nascosta` all'accesso: istante dell'icona, istante in cui l'IDLE e la delta sono pronti, e con la rete staccata per 30 s: le righe "controllo di ...", "IDLE ... di nuovo fra N s" e lo stato sotto l'account (A-15, A-16).

# Parte B: a programma aperto

## B.0 Rendering, animazioni e priorità del Dispatcher (punto 7)

Distinzione usata: **sorgente di WPF** = letto nel sorgente pubblico di WPF (ramo main di dotnet/wpf, scaricato in sessione); può non coincidere riga per riga con il runtime 10.0.12 del PC dell'autore. **Mia conoscenza** = non riletto qui. **Codice di Missiva** = letto nel repository. Niente di questo è stato eseguito.

### Tutte le sottoscrizioni a `CompositionTarget.Rendering`

| # | File:riga | Quando si sottoscrive | Quando si annulla | Quanto resta attiva | A riposo | Nella versione installata |
|---|---|---|---|---|---|---|
| 1 | `UI/WheelScrolling.cs:171` (`Accept`), gestore `OnFrame` `:220-241` | alla prima tacca o evento fine se `gliding.Count == 0` e `SystemParameters.ClientAreaAnimation` è vero (`:161`) | `:240`, quando `gliding` è vuoto dopo un fotogramma; una voce esce da `gliding` da `Step` (`:309`) o da `OnFrame` se `!viewer.IsVisible` (`:230`) | durata del gesto più la coda della molla (`Hold` + 20 ms, `:285`) | spenta | sì |
| 2 | `UI/StableScrollBar.cs:84` (`Update`), gestore `:90-106` | a ogni `ScrollChanged` dell'elenco (`:49`) se `!Converged()` e `!animating` (`:80-85`) | `:102`, quando posizione e dimensione del cursore distano meno di 0,0004 dalla meta (`:88`) | tutto il gesto più la coda di assorbimento: B-01 | spenta | sì |
| 3 | `UI/MainWindow.xaml.cs:519` (`ShowFromTray`) | alla riapertura se la finestra era nascosta (`:513-519`) | alla prima chiamata (`:516`) | un fotogramma | spenta | sì (solo misura "tempi:") |
| 4 | `UI/MainWindow.xaml.cs:940` (`LoadAccountsAsync`) | solo avvio, dentro `#if DEBUG \|\| PROVA` | alla prima chiamata (`:937`) | un fotogramma | spenta | no |
| 5 | `Core/WheelRecorder.cs:89,110`, `Core/DevTools.cs:749,761,784,853,871`, `Core/DevTools.Persone.cs:64,70` | comandi di prova | fine della misura | secondi | spenta | no: i tre file sono interamente in `#if DEBUG` |

Nessun'altra sottoscrizione a `Rendering` né a `LayoutUpdated` (grep su tutto il repository). Non c'è un gestore condiviso: 1 e 2 sono indipendenti. `Core/StallLog.cs:16-18` evita apposta di sottoscriverlo. A riposo, con la finestra visibile, ridotta a icona o nell'area di notifica, nessuna sottoscrizione resta attiva: CONFERMATO per 1 e 2 (le uscite sono quelle in tabella; l'unica coda lunga è B-01). Resta un caso limite SOSPETTO: se la finestra si nasconde (`HideToTray`, `MainWindow.xaml.cs:448`) a gesto in corso, `OnFrame` si disiscrive al fotogramma dopo (`:230`), ma con `Rendering` sottoscritto il ciclo di disegno non si ferma da solo (sorgente di WPF, `MediaContext.cs:553-559, 1818-1832`), quindi quel fotogramma arriva anche a finestra nascosta.

**Altri hook e timer a intervallo breve.** `Core/StallLog.cs:50-52` (`OperationStarted` e `OperationCompleted`, sempre attivi, spenti solo da `MISSIVA_SENZA_STALL=1`; non c'è `OperationPosted` né `OperationPriorityChanged`, utili: B-03). `DispatcherTimer`: `UI/ScrollHitGuard.cs:98` 25 ms a priorità **Input**, solo durante la sospensione del hit test; `UI/App.cs:534` (`themeCheck`) 250 ms, solo dopo un cambio di tema; tutti gli altri sono a minuti o a colpo singolo e a `Background` (tabella B.5).

**Animazioni.** `UI/MainWindow.xaml:713-733`: `DataTrigger` `Arrived` sul modello della riga, `DoubleAnimation` di `LayoutTransform.ScaleY` da 0 a 1 e di `Opacity`, 180 ms, `Timeline.DesiredFrameRate="120"` (`:721`); nessuna `ExitAction` né `RemoveStoryboard`: l'animazione resta in Fill sul contenitore finché lo si ricicla; accesa da `MessageItem.Arrived` (`MainWindow.xaml.cs:2718-2725`, spenta dopo 400 ms). `:655` `PopupAnimation="Fade"` del popup di salto. Dentro WebView2 (processo di Chromium, non fanno disegnare WPF): `UI/ReadingPage.cs:573-579` animazione `missiva-show` ritardata di 0,6 s (rete di sicurezza) e transizione di opacità di 0,15 s. Nessun spinner, `ProgressBar`, `ProgressRing`, `IsIndeterminate` in UI e Core; `UI/CalendarView.cs` non ha animazioni, timer o `Rendering`. Non guardate: le animazioni interne dei modelli Fluent di WPF (menu, tooltip, pulsanti).

### Il meccanismo, con le priorità

- **Priorità** (mia conoscenza, enumerazione pubblica): Inactive 0, SystemIdle 1, ApplicationIdle 2, ContextIdle 3, Background 4, **Input 5**, Loaded 6, **Render 7**, DataBind 8, Normal 9, Send 10. Input sta sotto Render. Dal sorgente di WPF (`Dispatcher.cs:2819-2820`): intervallo "foreground" = Loaded..Send, "background" = Background..Input.
- **Code** (sorgente di WPF, `Dispatcher.cs:1972-2010, 2402-2428`): `ProcessQueue` toglie una operazione per messaggio `WM_USER` e richiama `RequestProcessing`. Le operazioni foreground (compresa Render) si richiedono con `PostMessage`. Le operazioni background (Background e Input) partono solo se `IsInputPending()` è falso, altrimenti passano da un `SetTimer` di 1 ms (`WM_TIMER`, l'ultimo nell'ordine di `GetMessage`). Quindi Input e Background aspettano sempre le operazioni foreground e cedono il passo all'input Win32 grezzo in coda.
- **Il disegno** (sorgente di WPF, `MediaContext.cs`): con almeno un gestore `Rendering` sottoscritto, `ScheduleNextRenderOp` non chiede al `TimeManager` quando serve il prossimo tick ma tratta `Rendering` come un'animazione sempre attiva (`:553-559`, commento "same as having an active animation"): pianifica subito una `BeginInvoke(Render, ...)` e, dopo ogni disegno, `RenderMessageHandlerCore` (`:1759-1898`) ne ripianifica un'altra (`:1862-1880`). Il gestore gira dentro quell'operazione (`:1812-1816`), dopo il layout e prima di `Render(...)`.
- **La salvaguardia di WPF contro la fame di Input** (`MediaContext.cs:606-626`, costante `MaxTicksWithoutInput` = mezzo secondo, `:2889`): a ogni pianificazione immediata WPF mette in coda un'operazione "marcatore" a priorità Input; se il marcatore non è partito da più di 500 ms, il disegno successivo si pianifica con priorità Input invece di Render, "so that input can flush before we start another render". Spiega esattamente il "fino a mezzo secondo" e il "WPF stesso ha abbassato a Input la priorità dei disegni" di ESITO.md (blocco 9, punto 12): non è un difetto di Missiva, è il tetto di WPF. Con `Rendering` sempre sottoscritto, qualsiasi operazione a Input o sotto può aspettare fino a 500 ms.
- **Che cosa resta indietro** (sorgente di WPF): le riprese interne a Input: `InputManager` rivaluta il hit test (`HitTestInvalidatedAsyncCallback`, `InputManager.cs:418,487`), `MouseDevice` mouse-over e capture (`MouseDevice.cs:628,679`), `KeyboardDevice` il fuoco (`KeyboardDevice.cs:558`), `InputManager.RequestContinueProcessingStagingArea` (`:907`); e il lavoro a Background (cache di `VirtualizingStackPanel`). Dal codice di Missiva: il timer di ripresa di `ScrollHitGuard` (B-02), tutti i `DispatcherTimer` a Background, `UpdateBadge` a Background (`MainWindow.xaml.cs:1242`), `SettingsSaver` a Background (`Core/Store.cs:949`), i `Reader.Prepare` a `ApplicationIdle`.
- **Che cosa non resta indietro** (mia conoscenza, da confermare): i messaggi Win32 grezzi (`WM_KEYDOWN`, `WM_LBUTTONDOWN`, `WM_MOUSEWHEEL`) arrivano nel `WndProc` di `HwndSource` durante `GetMessage` e WPF li trasforma subito in eventi di input (`HwndKeyboardInputProvider` e `HwndMouseInputProvider` chiamano `InputManager.ProcessInput` in modo sincrono; i due file non si sono potuti scaricare dal sorgente pubblico, quindi **da confermare**). Se è vero, un tasto o un clic veri arrivano ai gestori anche con `Rendering` attivo; in ritardo è ciò che il gestore mette in coda a Input o sotto (o a Normal dietro layout lunghi). Le continuazioni di `await` sul thread dell'interfaccia sono a Normal (mia conoscenza), sopra Render: non soffrono di questa fame.
- **Coerenza con le misure del progetto:** ESITO.md blocco 9, punto 12: operazioni a Input messe in coda a 170 e 263 ms partite a 577, vicino alla soglia di 500 ms; `Core/DevTools.cs:842`: "un DispatcherTimer (WM_TIMER) non scatta mentre si disegna di continuo", coerente con le code sopra.

### Raccomandazione per tenere reattivi clic e tasti durante lo scorrimento

| Opzione | Pro | Contro | Rischio |
|---|---|---|---|
| A. `DispatcherTimer` a priorità alta al posto di `Rendering` | nessuna sottoscrizione a `Rendering` | `WM_TIMER` ha risoluzione di 10-16 ms ed è l'ultimo messaggio nell'ordine di `GetMessage`; `DevTools.cs:842` documenta che non scatta mentre si disegna di continuo; non sincronizzato col vsync, quindi passi irregolari: è il problema per cui `Rendering` è stato scelto (CLAUDE.md, "Scorrimento animato") | Alto: peggiora la fluidità |
| B. Un solo gestore `Rendering` condiviso, con conteggio dei consumatori e spegnimento appena sono tutti fermi | un solo posto; permette la regola C | oggi due consumatori; la parte di `StableScrollBar` va pilotata dal fotogramma di `WheelScrolling` | Basso |
| C. Spegnere le animazioni al primo input vero: `InputManager.Current.PreProcessInput` (sincrono, nel `WndProc`) per `KeyDown` (non i soli modificatori) e `MouseDown` chiama `StopAll`: glide fermato, cursore della barra portato alla meta, `ScrollHitGuard` ripreso, `Rendering` annullato nella stessa chiamata | cura la causa: senza `Rendering` non c'è fame di Input | un clic che ferma l'inerzia può essere inatteso; da decidere: ferma il glide solo su tasto e pulsante, mai su movimento o rotella | Basso-Medio |
| D. `Dispatcher.Invoke(Input)` o `ProcessInput` dentro il fotogramma | lascia passare l'Input in apparenza | rientranza dentro `RenderMessageHandlerCore` (`_isRendering` vero, tempo del `TimeManager` bloccato, layout in corso): è il DoEvents di WPF, noto per eccezioni di layout | Alto: da non fare |
| E. Hook di `HwndSource` o `ComponentDispatcher.ThreadFilterMessage` su `WM_KEYDOWN`, `WM_LBUTTONDOWN` | reagisce prima di WPF | stessa cosa di C ma più fragile (messaggi grezzi, un secondo punto per ogni finestra) | Medio: nessun vantaggio su C |
| F. `GetQueueStatus`/`GetInputState` in `OnFrame` per saltare lavoro | evita lavoro inutile | non toglie la fame: l'input Win32 grezzo entra comunque; quello che aspetta è l'Input del Dispatcher, che l'API non vede | Basso, ma inutile |
| G. Ridurre il lavoro per fotogramma | meno ms per fotogramma | non c'entra con la soglia di 500 ms (il marcatore conta il tempo, non il lavoro); già fatto molto | Basso |

**Raccomandazione: B + C + chiusura della coda del cursore a fine gesto (B-01) + ripresa del hit test dal fotogramma (B-02).** In concreto un `FrameClock` statico in `UI/` con un solo `CompositionTarget.Rendering`, il conteggio dei consumatori (`WheelScrolling`, `StableScrollBar`), `IsActive` e `StopAll(motivo)`, agganciato una volta in `App.OnStartup` a `InputManager.Current.PreProcessInput`. Il `Storyboard` di B-07 dura 180 ms e non passa da lì. Ordine di lavoro e rischio: prima B-01 e B-02 (poche righe), poi `FrameClock` e C. Non raccomandati: A (peggiora la fluidità, già scartata nei fatti da CLAUDE.md), D (rientranza), E (nessun vantaggio su C), né toccare la soglia di WPF.

**Verifica su Windows (comune ai punti B-01..B-04):** (1) in `Core/StallLog.cs` aggiungere `OperationPosted` e `OperationPriorityChanged`: con la prima salvare l'istante di messa in coda in una `ConditionalWeakTable<DispatcherOperation, Box<long>>` e in `OperationStarted` scrivere "attesa di Input N ms" per le operazioni a Input o sotto oltre 100 ms (senza testi né nomi di oggetti, come ora); con la seconda "disegni abbassati a Input" quando un'operazione Render passa a Input: è l'evidenza diretta della soglia di 500 ms nella versione installata, senza strumenti di prova. (2) "scorri 120" e PresentMon per 2 s dopo il gesto. (3) Latenza di un comando di `MISSIVA_KEYS` a 50, 200, 500 ms dalla fine del gesto, e di un `PostMessage(WM_KEYDOWN, VK_DOWN)` verso la finestra principale (B-04).

## B.1 Punti trovati: scorrimento, animazioni, priorità

### B-01 Il cursore della barra continua a far disegnare WPF per circa un secondo dopo ogni gesto
- **Dove:** `UI/StableScrollBar.cs:22` (`Tau = 200`), `:66` (`still && !animating`), `:80-85` (sottoscrive `Rendering`), `:88` (`Converged`, soglia 0,0004), `:90-106` (`OnFrame`).
- **Scenario:** scorrimento veloce su righe mai misurate (la stima dell'altezza cambia, commento del file `:9-17`). Durante il gesto l'errore fra posizione mostrata e vera si accumula; a fine gesto `WheelScrolling` si disiscrive ma `StableScrollBar.OnFrame` resta sottoscritto finché l'errore non scende sotto 0,0004 della pista. Calcolo dalle costanti del codice (non misura): tempo = 200 ms × ln(e0 / 0,0004): per e0 = 0,005 circa 0,5 s; 0,02 circa 0,78 s; 0,1 circa 1,1 s. ESITO.md riporta salti grezzi del cursore fino a 114-124 px, che su una pista di qualche centinaio di pixel sono errori dell'ordine di 0,1. In tutto quel tempo WPF disegna a ogni fotogramma (B.0) e le operazioni a Input o sotto restano indietro fino a 500 ms. Inoltre la correzione già fatta per Canc e Ctrl+Z (`:66`: `still` solo se `!animating`) non vale se arrivano entro quel secondo da un gesto: `animating` è ancora vero, il cursore si anima di nuovo per 0,65 s e torna il problema del blocco 9.
- **Gravità:** Media: fino a circa 1 s di disegno continuo dopo ogni scorrimento veloce, con la latenza di Input di 0,35-0,5 s già documentata; non cresce con le righe ma con la distanza percorsa su territorio non misurato. ESITO.md (blocco 9, "Trovato in più"): "un clic o un tasto mentre l'elenco scorre ancora non è stato misurato".
- **Stato:** CONFERMATO (codice e sorgente di WPF); l'effetto sulla latenza vera SOSPETTO finché non misurato.
- **Proposta:** (1) a fine gesto chiudere la coda: `WheelScrolling` espone `IsGliding`; quando l'ultimo glide termina, `StableScrollBar` fa `position = targetPosition; size = targetSize; Show()` e si disiscrive (un salto di pochi pixel, nascosto dal fatto che lo scorrimento si è fermato). (2) Soglia di convergenza in pixel: `0,5 / bar.Track.ActualHeight` al posto di 0,0004 (toglie anche circa 0,3 s di coda senza gesto: 200 ms × ln(0,002/0,0004) su una pista di 800 px, calcolo). (3) `Update(still)`: trattare come `still` anche `animating` quando nessun glide è attivo.
- **Verifica su Windows:** `OperationPosted` in `StallLog` (B.0) durante e dopo "scorri 120"; PresentMon: fotogrammi presentati nei 1,5 s dopo la fine di "scorri N" (oggi circa un centinaio, dopo la correzione zero); conteggio delle righe "interfaccia lenta".

### B-02 La ripresa del hit test dopo lo scorrimento aspetta un timer a priorità Input
- **Dove:** `UI/ScrollHitGuard.cs:32` (`RestMs = 60`), `:98-103` (timer a Input da 25 ms), `:108-127` (`Resume`), `:92-95` e `:137-142` (riprese immediate con un movimento vero, un pulsante o un tasto).
- **Scenario:** scorrimento col touchpad, fermata, clic senza muovere il mouse. La ripresa spontanea è nel Tick del timer, a Input: finché WPF disegna di continuo (B-01) il Tick può aspettare fino a 500 ms. Il commento del file (`:24-26`) documenta che un clic durante la sospensione "si perde": la finestra in cui si può perdere, nelle intenzioni di 60 ms, può essere molto più lunga.
- **Gravità:** Media (clic ignorato), poco probabile; non cresce con i dati.
- **Stato:** SOSPETTO: manca la durata fra l'ultimo fotogramma veloce e `Resume` (`CursorProbe.Hit(C.Resume)` è solo DEBUG).
- **Proposta:** ripresa dal fotogramma: in `WheelScrolling.OnFrame`, dopo `Step`, se un glide è appena finito chiamare `ScrollHitGuard.EndGesture(viewer)`, che controlla `RestMs` con il tempo del fotogramma e riprende senza timer; il timer resta di riserva e a priorità Normal (sopra Render, non affamabile).
- **Verifica su Windows:** in DEBUG/PROVA scrivere nel registro la durata `Fast` → `Resume`; confronto con la stessa prova dopo la proposta e dopo B-01.

### B-03 Due sottoscrizioni indipendenti e nessuna regola "si spegne al primo input"
- **Dove:** `UI/WheelScrolling.cs:171`, `UI/StableScrollBar.cs:84`, animazione di `UI/MainWindow.xaml:713-733`.
- **Scenario:** nessuno sa quanti consumatori tengono viva la coda Render; non c'è un punto solo in cui dire "c'è un tasto o un clic in attesa, ferma tutto".
- **Gravità:** Media (struttura), non un difetto del singolo gesto. **Stato:** CONFERMATO (grep).
- **Proposta:** la raccomandazione di B.0 (`FrameClock` B + C). **Verifica su Windows:** quella di B.0.

### B-04 `MISSIVA_KEYS` misura la latenza di Input, non quella dei tasti veri
- **Dove:** `Core/DevTools.cs:170` (`DispatcherTimer(DispatcherPriority.Input)` che legge il file dei comandi); CLAUDE.md: "I comandi girano a priorità Input come i tasti veri".
- **Scenario:** ogni misura di "tempo dal comando a testo pronto" durante "scorri N" o dopo un gesto passa per un'operazione Input, che per costruzione può aspettare fino a 500 ms con `Rendering` sottoscritto. Un tasto vero, se entra sincrono dal `WndProc` (B.0), non aspetta così. Cambia l'interpretazione di "Aperto" in ESITO.md: "clic e tasti possono arrivare con fino a mezzo secondo di ritardo" potrebbe riguardare solo le riprese interne a Input e i comandi di prova.
- **Gravità:** Bassa (strumento). **Stato:** SOSPETTO (manca la conferma che `HwndKeyboardInputProvider` chiami `ProcessInput` in modo sincrono).
- **Proposta:** per misurare il percorso vero di notte (SendInput non arriva, CLAUDE.md "Prove"): `PostMessage(hwnd, WM_KEYDOWN, VK_DELETE...)` verso la finestra principale; per tasti semplici (Canc, frecce, Invio) passa dal `WndProc` di `HwndSource` come un tasto vero (con Ctrl e Maiusc no, CLAUDE.md). Misurare il ritardo fra il `PostMessage` e l'inizio di `OnMessageKeyDown` durante "scorri 120" e confrontarlo con `MISSIVA_KEYS`.
- **Verifica su Windows:** come sopra, con i timestamp all'entrata del gestore e al `PostMessage`.

### B-05 Dopo un cambio di tema `ListScroller`, `StableScrollBar` e `ScrollHitGuard` potrebbero riferirsi a uno `ScrollViewer` fuori dall'albero
- **Dove:** `UI/MainWindow.xaml.cs:2399-2402` (`listScroller ??= FindScroller(MessageList)`, in cache per sempre), `:39-40` (`MessageList.Loaded += ... Attach(ListScroller)`), `:577-590` (commento: dopo il cambio di tema "WPF rimette il modello della finestra"), `UI/StableScrollBar.cs:32-38` (cerca `PART_VerticalScrollBar` una volta), `UI/ScrollHitGuard.cs:34,64-68` (`ConditionalWeakTable` per `ScrollViewer`, `Moving` esce in silenzio alla `:73` se il viewer non è in tabella).
- **Scenario:** `App.ThemeChanged` (alba e tramonto automatici di Windows, giorni nell'area di notifica). Se il modello del `ListBox` si rifà, `ScrollViewer` e `ScrollBar` sono istanze nuove: `listScroller` resta il vecchio, `Loaded` non scatta di nuovo, quindi barra stabile e guardia restano sul vecchio e il nuovo funziona con la barra standard e senza guardia; anche `AppendKeepingPosition` (`:2411-2415`) e `ScrollToTop` userebbero il vecchio.
- **Gravità:** Media se vero (peggioramento silenzioso per tutta la sessione); non cresce con i dati.
- **Stato:** SOSPETTO: manca sapere se il cambio di tema ricrea il modello del `ListBox` (`--theme-flip` esiste, ma ESITO e CLAUDE.md lo usano per colori e riga, non per questi tre riferimenti).
- **Proposta:** in `OnThemeChanged` azzerare `listScroller` e ripetere `Attach` se `FindScroller(MessageList) != listScroller`.
- **Verifica su Windows:** `--theme-flip 20`; dopo il cambio, `ReferenceEquals` fra lo `ScrollViewer` di `MessageList` e quello delle guardie (riga di prova) e di nuovo "sonda"/"sospensione prova"/"scorri 120" con la riga "barra:" di `barra.ps1`.

### B-06 WebView2 (un `HwndHost`) lavora a ogni layout della finestra
- **Dove:** sorgente di WPF, `HwndHost.cs:822,955` (`LayoutUpdated += _handlerLayoutUpdated`), `:863-866` (`OnLayoutUpdated` → `UpdateWindowPos`), `:323-375` (`UpdateWindowPos`: `CalculateAssignedRC`, `OnWindowPositionChanged(...)`, `ShowWindowAsync(SW_SHOW)`; se non visibile `SW_HIDE`; commento `:349-355`: "we can't check the existing position because we use SWP_ASYNCWINDOWPOS"). Nel progetto: `UI/ReadingView.xaml.cs` (WebView2 creato dal codice) e CLAUDE.md (profilo: `WebView2.OnWindowPositionChanged` a ogni layout 7%).
- **Scenario:** `LayoutUpdated` è un evento per Dispatcher, non per sottoalbero: ogni layout che cambia qualcosa, e `WheelScrolling.Step` ne fa uno a fotogramma (`:296`), lo fa scattare. Ogni scatto = trasformazione fino alla radice + `OnWindowPositionChanged` (l'override di WebView2 non è leggibile; 7% dal profilo di CLAUDE.md) + un `ShowWindowAsync` che posta un messaggio. Vale anche con WebView2 `Hidden` e per le finestre secondarie.
- **Gravità:** Bassa-Media. **Stato:** CONFERMATO il meccanismo base (sorgente di WPF); il 7% è di CLAUDE.md, non rimisurato; il costo dell'override SOSPETTO.
- **Proposta:** (a) prima misurare: `dotnet-trace` come in CLAUDE.md, distinguendo `HwndHost.UpdateWindowPos`/`CalculateAssignedRC` dal `Bounds` del controller (se l'override scrive sempre `Bounds`, anche invariato, il costo è lì). (b) Esperimento A/B, non proposta ferma: durante lo scorrimento veloce togliere con riflessione `HwndHost._handlerLayoutUpdated` da `LayoutUpdated` del WebView2 (campo privato, `:1127` del sorgente di WPF) e rimetterlo a fine gesto chiamando `UpdateWindowPos()`; fragile. (c) `WebView2CompositionControl` scartato: cambia input, IME e accessibilità, scelte già approvate dall'autore.
- **Verifica su Windows:** contare le chiamate a `OnWindowPositionChanged` per fotogramma in "scorri 120" (una classe derivata che le conta, o ETW); lavoro per fotogramma con e senza l'esperimento.

### B-07 L'ingresso delle righe nuove fa un layout dell'elenco a ogni fotogramma per 180 ms; allocazioni a ogni fotogramma
- **Dove:** `UI/MainWindow.xaml:713-733` (animazione di `LayoutTransform`), `UI/MainWindow.xaml.cs:2718-2725` (`MarkArrived`); `UI/WheelScrolling.cs:227` (`gliding.ToList()`), `:269` (`Recent.RemoveAll(r => ...)` con una lambda che cattura `t` e `m`: chiusura e delegato nuovi a ogni fotogramma).
- **Scenario:** un messaggio nuovo mentre si scorre o si scrive: `LayoutTransform` cambia l'altezza della riga a ogni fotogramma, quindi layout di tutta la parte realizzata, e `Rendering` equivalente per 180 ms. Le allocazioni sono pochi byte a fotogramma: CLAUDE.md attribuisce i picchi di 22-64 ms alle raccolte di .NET durante lo scorrimento veloce, ma la causa principale sono le righe nuove.
- **Gravità:** Bassa. **Stato:** CONFERMATO come meccanismo; costo non misurato.
- **Proposta:** `RenderTransform` (non rifà il layout) con spostamento della vista, oppure saltare l'animazione se è in corso uno scorrimento (`IsGliding`) o c'è input recente (`lastActivity`, `MainWindow.Offline.cs:257`). `WheelScrolling.OnFrame`: `if (gliding.Count == 1)` iterare senza copia; `RemoveAll` con un ciclo a mano o una lambda statica con stato in un campo.
- **Verifica su Windows:** "arrivo di prova" in `MISSIVA_KEYS` con `lampi.ps1 -List` mentre gira "scorri 60"; righe di `StallLog`; allocazioni riportate da "scorri 120".

### B-08 Le intestazioni "Oggi" e "Ieri" restano quelle di quando la riga è stata inserita
- **Dove:** `UI/ViewModels.cs:719-734` (`GroupFor` con `DateTime.Today`), `:1130,1164,1282,1296` (`head.Group = ...` una volta all'inserimento), `UI/MainWindow.xaml.cs:2506-2512` (`PrependRows` rifà i gruppi solo se il nome del gruppo del messaggio nuovo non è fra quelli mostrati; il commento `2501-2505` descrive il caso "Oggi dopo mezzanotte"). Nessun timer di mezzanotte (grep su `UI/MainWindow*.cs` e `App.cs`).
- **Scenario:** Missiva aperta, o nell'area di notifica per giorni, a cavallo della mezzanotte con la Posta in arrivo selezionata: le righe di ieri restano "Oggi"; un messaggio arrivato dopo la mezzanotte ha "Oggi", che c'è già fra i gruppi (le vecchie righe), quindi non scatta il rifacimento e finisce nello stesso gruppo: il caso descritto dal commento non si verifica.
- **Gravità:** Bassa (etichette sbagliate fino al prossimo ricaricamento), nessun costo di prestazioni. **Stato:** CONFERMATO dalla lettura; l'effetto a schermo non l'ho visto.
- **Proposta:** nel `snoozeTimer` da un minuto (`MainWindow.xaml.cs:3791`) confrontare `DateTime.Today` con l'ultimo giorno visto e, se cambia, ricalcolare `Group` di tutte le righe e `RegroupKeepingSelection()` (`:2766-2775`): un `view.Refresh()` al giorno.
- **Verifica su Windows:** ora di sistema a 23:59 su una macchina di prova, con Missiva aperta e un "arrivo di prova".

### B-09 Raggruppamento dell'elenco: due rifacimenti per impostazione e costi che crescono con le righe
- **Dove:** `UI/MainWindow.xaml:672` (`IsVirtualizingWhenGrouping="True"`), `:751-794` (`GroupStyle` con `GroupItem` = `StackPanel` + `ContentPresenter` + `ItemsPresenter`, intestazione con `Grid`, due `Border`, `TextBlock`, `DataTrigger`), `UI/MainWindow.xaml.cs:553-554` (`GroupDescriptions.Clear()` poi `Add`), `:2506-2512` e `:2766-2775` (`PrependRows`, `RegroupKeepingSelection` → `view.Refresh()`), `UI/ViewModels.cs:1112-1133` (`Append`), `:1136-1156` (`Prepend`), `:1077-1084` (`PlaceOf`), `:1169-1174` (`PinnedEnd`), `:1198-1204` (`InsertChild`), `:1228-1247` (`Remove`).
- **Scenario e costi letti nel codice (tutti SOSPETTI come tempi, mancano misure con migliaia di righe):** (1) `view.Refresh()` rifà i gruppi per tutte le righe, ma accade solo quando nasce un gruppo nuovo (Fissati, mezzanotte se B-08 fosse corretto). (2) `ApplySettings` fa `Clear()` e `Add` uno dopo l'altro: **due rifacimenti completi** invece di uno; si evita con `using (view.DeferRefresh())`. (3) `Append` con `ObservableCollection.Insert` per riga: 40-50 notifiche per pagina; `AppendKeepingPosition` le tiene già insieme per il layout. (4) `PlaceOf` in modalità `ByDue` (Contrassegnati di tutti gli account): per ogni riga nuova scorre `Rows` dall'inizio e calcola `DueKey` per ogni riga (`:1082`): O(n) per riga, quadratico sull'elenco intero. (5) `InsertChild` e `Toggle` (`:1176-1191`): `Rows.IndexOf(anchor)` e `Rows.Remove(c)` O(n) per messaggio di una conversazione espansa; innocui con conversazioni piccole, O(m×n) con centinaia di messaggi. (6) Con `IsVirtualizingWhenGrouping` ogni `GroupItem` ospita un suo `VirtualizingStackPanel` annidato (non si ricicla in modalità Recycling, mia conoscenza), e le altezze stimate dei pannelli annidati sono la ragione dei salti del cursore che `StableScrollBar` nasconde. Una lista piatta con righe-intestazione, come in `UI/PeopleView.Sections.cs`, evita tutto questo, ma CLAUDE.md dice che per la `MessageList` è già stata tentata e lasciata: non la propongo, la segnalo come causa possibile se un profilo con migliaia di righe mostra i `GroupItem`.
- **Gravità:** Bassa-Media, sconosciuta finché non misurata; cresce con le righe ai punti 1, 2, 4. **Stato:** SOSPETTO (tempi), CONFERMATO (la struttura).
- **Proposta:** `DeferRefresh` in `ApplySettings` (correzione sicura). Il resto dopo una misura.
- **Verifica su Windows:** cartella finta da 5.000 righe (`TestAppend` in DEBUG): tempi di `view.Refresh()`, di `ApplySettings` e di una pagina in `ByDue`; "costo righe" e "aggiungi 40" esistenti.

### B-10 Calendario: ricostruzione completa a ogni arrivo di un account, e la vista torna alle 7:30
- **Dove:** `UI/CalendarView.cs:183-187` (`Show` = `Render` + `LoadAsync`), `:236` (`if (v == version) Render();` per ogni provider che risponde), `:261-274` (`Render` rifà `FillAccounts` e l'intero corpo), `:485-489` (ogni `WeekGrid` crea un `ScrollViewer` nuovo e in `Loaded` lo porta alle 7:30 o al primo evento del giorno), `:375-492` (`WeekGrid`), `:495-532` (`MonthGrid`: 42 celle, un `Button` per numero di giorno e fino a tre `Chip`), `:470` (`events.First(...)`), `:250` (`Visible()`).
- **Scenario:** con P account con calendario, ogni cambio di periodo o "Oggi" fa 1 + P ricostruzioni complete (una da dati vecchi o vuoti, poi una per ogni risposta, anche senza eventi nuovi). Mese: fino a circa 170 pulsanti-evento (tetto di 3 per cella); `events.Where(...)` per cella è O(42×E) con un array allocato per evento (`AllDayOn([x.Event], day)`, area `:418`). Settimana e Giorno: 24 righe `Border` per colonna (168 a sette giorni) più un pulsante per evento, senza tetto, e `events.First(...)` per ogni pulsante è O(E) per evento; `Visible()` rifà `ColorConverter.ConvertFromString` per ogni calendario a ogni `Render`. Se l'utente ha già scorso il pomeriggio e arriva il secondo account, la vista salta alle 7:30 (CONFERMATO dalla struttura). Nessun timer: la linea rossa dell'ora non si aggiorna senza un `Render`.
- **Gravità:** Bassa-Media: lavoro sul thread dell'interfaccia a ogni clic sulle frecce. Cresce con il numero di account (un `Render` ciascuno) e di eventi.
- **Stato:** CONFERMATO (struttura, chiamate, scorrimento perso); SOSPETTO i millisecondi della vista Mese (CLAUDE.md dà solo 16 MB di memoria in più).
- **Proposta:** (a) accorpare i `Render` in un solo giro (`Dispatcher.BeginInvoke` a Background con bandierina "render richiesto") e saltarlo quando i dati dell'account non sono cambiati; (b) portare alle 7:30 solo alla prima visualizzazione del periodo e tenere `VerticalOffset` fra due `Render` dello stesso periodo; (c) colori dei calendari calcolati una volta; dizionario per id in `:468-474` se la misura lo chiede.
- **Verifica su Windows:** `--calendario --calendario-vista Month --calendario-finto` con 600 eventi e `Log.Time` attorno a `Render`; contare i `Render` per clic con due account; scorrere il pomeriggio durante il caricamento.

## B.2 Punti trovati: dati, rete, memoria

### B-11 Bozza ripresa: `IsLast` di `HtmlToFlow` è quadratico sui `<br>` fratelli
- **Dove:** `Core/HtmlToFlow.cs:245` (`if (IsLast(e)) Current();` per ogni `<br>`) e `:369-376` (`IsLast`: `e.NextSibling`).
- **Scenario:** Modifica su una bozza (o risposta ripresa) il cui HTML ha migliaia di `<br>` come fratelli nello stesso elemento: testo incollato (log, elenco, poesia), un messaggio "solo testo" convertito, una citazione senza `<div>` per riga. In AngleSharp `NextSibling` cerca il nodo fra i fratelli dal primo (la stessa cosa che il blocco 1 ha trovato per `InnerHtml` e `Remove`). I tetti di `ParseGuard` (250.000 elementi) non lo fermano. Gira sul thread dell'interfaccia (`UI/ComposeWindow.xaml.cs:322`).
- **Gravità:** Media se capita, Bassa come frequenza. Quadratico nel numero di `<br>` fratelli.
- **Stato: MISURATO** su Linux (AngleSharp 1.8.2, .NET 10.0.12, un ciclo di `NextSibling` per ogni `<br>` di un `<body>` con N righe "line i<br>"; stesso ciclo di `IsLast`; due esecuzioni): 5.000 br 120-125 ms, 10.000 br 130-240 ms, 20.000 br 605-1.015 ms, 40.000 br 2.223-3.193 ms; l'enumerazione di `ChildNodes` sugli stessi dati: 0 ms. Solo l'andamento vale per Windows (raddoppiare i `<br>` moltiplica per 3-4). Non è stata eseguita `HtmlToFlow` vera (dipende da WPF).
- **Proposta:** calcolare "ultimo del blocco" in `Children(INode parent)` mentre si scorre già `ChildNodes` (una passata: posizione dell'ultimo elemento o testo non vuoto) e passare un flag al nodo, senza `NextSibling`: O(n).
- **Verifica su Windows:** `--prova-bozze` con una bozza da 40.000 `<br>` fratelli in un `<div>`; stesso test con 100 `<br>` e `FormatRoundTrip` per controllare che paragrafi e `LineBreak` restino uguali carattere per carattere.

### B-12 Bozza ripresa con corpo grande: tutto l'albero WPF sul thread dell'interfaccia
- **Dove:** `UI/ComposeWindow.xaml.cs:322` (`Editor.Document.Blocks.AddRange(HtmlToFlow.Blocks(body.Html, ...))` dopo `await provider.GetBodyAsync`, righe `295-322`), `Core/HtmlToFlow.cs:20-37` (analisi AngleSharp sul thread di chi chiama, poi un `Run`/`Paragraph`/`Table` WPF per nodo), `:83-92` (`Css(e)` alloca un dizionario e uno `Split` per ogni elemento).
- **Scenario:** Modifica su una risposta che porta con sé la citazione di una newsletter da 200 KB (Gmail la mette nella bozza; Graph nel corpo di `createReply`). I tetti di `ParseWithinLimits` ammettono 250.000 elementi e 512 livelli: migliaia di `Run` e tabelle WPF annidate create e poi impaginate dal `RichTextBox` in un colpo.
- **Gravità:** Media, SOSPETTO. Lineare nei nodi per la costruzione, più l'impaginazione del `FlowDocument` (non lineare con tabelle annidate). **Stato:** SOSPETTO: mancano il tempo di `HtmlToFlow.Blocks` e dell'impaginazione per 200 KB e 1 MB (il registro dice "bozza ripresa: ...", non il tempo).
- **Proposta (con rischio):** (a) misurare con `Log.Time` attorno a `:322`; (b) se serve, un tetto più basso per l'editor (per esempio 20.000 elementi, oltre cui la parte citata va in `PlainText`, come già oltre i tetti): l'editor non rende fedelmente una newsletter comunque; (c) analisi in `Task.Run` e costruzione WPF sul thread dell'interfaccia. Rischio: cambia l'aspetto della citazione nelle bozze riprese; la parte scritta dall'utente non va toccata.
- **Verifica su Windows:** Modifica su una risposta a una newsletter da 200 KB e da 1 MB: tempo dal clic al testo visibile e righe "interfaccia lenta: blocco".

### B-13 Coda delle azioni senza rete: durante la ripetizione le azioni non sono su disco, e con Graph partono una per volta
- **Dove:** `Core/ActionQueue.cs:217-226` (`ReplayAsync`: `Items.RemoveAll(...)` e `Save()` prima di agire; tornano in coda solo in `Wait`, `:235`); `UI/App.cs:305-312` (`ExitAll` non aspetta `ReplayAsync`); `Core/ActionQueue.cs:211-212` (Graph: `StillThereAsync` restituisce null), `:267-268` (`int end = i + 1; if (there != null) while (...SameAs...) end++;` → per Graph ogni gruppo è di un messaggio), `:280, 299, 304` (`MoveAsync`/`SetReadAsync`/`SetFlagAsync` una alla volta), `:294` (`Store.Save()` quando `Flags.Follow` ha cambiato).
- **Scenario:** (1) 300 spostamenti fatti senza rete con un account Graph; la rete torna, la ripetizione dura minuti. Se in quei minuti l'utente sceglie Esci, Windows chiude la sessione o Missiva cade, le azioni non ancora ripetute non sono né nel file né nella Posta in uscita: perse senza avviso. (2) Con 500 azioni in coda, Graph le ripete in sequenza, benché il provider sappia fare quattro richieste insieme per casella (`MailboxGate`): O(N × latenza di una richiesta); con N = 500 e 100-300 ms a richiesta (CLAUDE.md: delta 68-453 ms) sono dell'ordine di 1-2 minuti, un quarto con quattro insieme (stima, non misurata).
- **Gravità:** (1) Media, perdita di lavoro dell'utente, finestra tanto più larga quanto più lenta è la ripetizione; non è un costo di velocità. (2) Media, lineare nella coda.
- **Stato:** CONFERMATO (letto). SOSPETTO che la chiusura a metà ripetizione sia raggiungibile (manca la prova con Esci durante una coda lunga) e i tempi di (2).
- **Proposta:** (1) togliere le azioni dal file a gruppi, `Save` subito prima di ogni gruppo, oppure `try/finally` che rimette `mine.Skip(i)` in `Items` e salva se il processo sta uscendo (`Dispatcher.HasShutdownStarted`); il vincolo "una volta sola" si tiene con il segno del gruppo in corso. (2) Per Graph raggruppare comunque i consecutivi uguali in blocchi da 4-10 e, se un blocco fallisce con 404, ripetere i singoli; `Store.SaveSoon()` al posto di `Store.Save()` a `:294`, con un salvataggio alla fine.
- **Verifica su Windows:** (1) `--senza-rete` per accodare 50 azioni, poi togliere `--senza-rete` e, con il server IMAP finto con `ExamineDelay`, chiudere con "esci" a metà e riaprire; (2) l'account Outlook personale, 100 messaggi segnati letti senza rete, poi in linea: righe "coda di ...: N azioni fatte" con i tempi.

### B-14 Rubrica e contatti: salvataggio sotto il lucchetto che l'interfaccia prende a ogni messaggio aperto e a ogni lettera
- **Dove:** `Core/AddressBook.cs:518-540` (`Save`: `lock (Gate)` a `:520`, serializzazione `:532`, `ProtectedData.Protect` e `SafeFile.Write` a `:533`, tutto dentro), `Core/Store.cs:549-588` (`SafeFile.Write`: `Flush(true)` a `:555`, `Thread.Sleep(50)` fino a 3 volte a `:581` se il file è tenuto da un antivirus). Stesso `Gate` sul thread dell'interfaccia: `LookAlike` (`:437-453`, `:446`) da `SenderCheck.Of` (`UI/ReadingView.xaml.cs:927`, ogni `Render` di un messaggio aperto), `Received(...)` da `FetchBodyAsync` (`:731`) e dall'elenco (`MainWindow.xaml.cs:2154`), `Suggest` (`:223`, ogni lettera). `AddressBook.cs:83` (`entries`, nessun limite né potatura; `Add` `:217-248`), `:516` (`ScheduleSave`). `Core/Contacts.cs:199-212` (`Save`: serializza + DPAPI + `SafeFile.Write` con `Flush(true)` sotto `lock (gate)`), `:214-218` (`Put` lo chiama dal thread dell'interfaccia), `:244-262` (`SetRemote` lo stesso lavoro su un thread del pool), `:332-338` (`ForSuggestions` prende `gate` a ogni lettera nel campo A).
- **Scenario:** arriva un messaggio, `Add` lo conta (`:183-188`) e `ScheduleSave` fa partire un salvataggio 5 s dopo; se in quel momento si apre un messaggio o si scrive nel campo A, il thread dell'interfaccia aspetta serializzazione, DPAPI, fsync ed eventuali nuovi tentativi. Capita dopo ogni arrivo e dopo ogni lettura della Posta inviata (ogni ora). "Aggiungi ai contatti" o Nuovo contatto bloccano l'interfaccia per la durata della scrittura intera di `contatti.dat` (migliaia di contatti); se nel frattempo un `SetRemote` tiene `gate`, anche la digitazione nel campo A aspetta.
- **Gravità:** Bassa oggi (rubrica dell'autore: 364 voci, dal rapporto V-22), Media a regime: ogni mittente e destinatario entra per sempre (la sola esclusione sono i nomi "noreply/donotreply/mailerdaemon/postmaster", `Wanted` `:198-203`); decine di migliaia di voci dopo mesi di IDLE attivo (150-250 byte di JSON a voce, stima dalla struttura di `Entry`); il tempo sotto lucchetto cresce col file. È V-22 di velocita-2.md, non chiuso da nessun blocco.
- **Stato:** CONFERMATO nel codice (lucchetto condiviso, assenza di tetto, scrittura sul thread dell'interfaccia in `Contacts.Put`); l'entità SOSPETTA: nessuna misura della collisione o del salvataggio con 50.000 voci.
- **Proposta:** `Save` prende sotto `Gate` solo una copia (`book.Values` clonati in una lista e un clone di `sentScans`, decine di µs ogni mille voci, da misurare) e rilascia; serializzare (meglio con un contesto generato come `SavedBodyJson`; oggi `JsonSerializer` a riflessione, `:82`), cifrare e scrivere fuori da `Gate` sotto un secondo lucchetto `writing` con un numero di versione come `SettingsSaver`, così sul file arriva sempre l'ultima copia. Potatura al caricamento delle voci mai scritte (`Sent == 0`, poche `Received`, `LastReceived` più vecchio di 12-24 mesi). `Contacts.Put` su pool con un solo salvataggio per raffica.
- **Verifica su Windows:** rubrica finta da 20.000-50.000 voci (`AddressBook.TestBook` in Debug; per la Release creare il file); "clic N" ripetuto mentre `AddressBook.Save` gira e righe "interfaccia lenta: blocco" di `StallLog`; 5.000 contatti, tempo di "Aggiungi ai contatti"; digitare nel campo A durante una rilettura dei contatti.

### B-15 Copia locale degli elenchi scritta in modo sincrono sul thread dell'interfaccia, anche due volte per cartella
- **Dove:** `UI/MainWindow.xaml.cs:1862-1865` (`SaveFirstPage`) chiamata da `:2169` (prima pagina dal server), `:2295` (dopo le anteprime, `FillPreviewsAsync`), `:2488` (ogni arrivo con la cartella aperta, `KeepInSavedPage`), `:4058` (`RefreshListAsync`, dopo ogni avviso dell'IDLE che non è un eco), `:4180` (ogni categoria cambiata); `:1083` (`MailCache.SaveFolders` a ogni rilettura delle cartelle). `Core/MailCache.cs:133-146` (`Write`: `SerializeToUtf8Bytes`, `ProtectedData.Protect`, `File.WriteAllBytes`, `File.Move`, tutto sincrono). Se la cartella non è aperta, `Prepend` (`MailCache.cs:67-74`) rilegge, decifra e deserializza la pagina e rilancia `MailHtml.PlainPreview` su ogni anteprima (`:41`, codice di migrazione che gira ancora a ogni lettura).
- **Scenario:** apertura di una cartella Gmail o IMAP (due scritture: dopo l'elenco e dopo le anteprime), arrivo di un messaggio, evento dell'IDLE, assegnazione di una categoria. Le continuazioni dopo `await` sono sul thread dell'interfaccia.
- **Gravità:** Bassa-Media. Costo non misurato (50+ righe serializzate, DPAPI, file nuovo e rinomina; con antivirus il file nuovo può costare più del resto). Cresce con le righe in `firstPage` (salgono a ogni arrivo, `:2487`) e con la frequenza degli eventi. Non è nei rapporti precedenti (V-17 e il blocco 8 riguardano impostazioni e corpi, non la copia degli elenchi).
- **Stato:** CONFERMATO che è sul thread dell'interfaccia; il millisecondo SOSPETTO.
- **Proposta:** un `SaveMessagesSoon` come `SettingsSaver`: sul thread UI solo `ToList()` e serializzazione in byte (così gli oggetti mutabili non sono letti da un altro thread), DPAPI e scrittura in `Task.Run`, attesa di 300 ms con "vince l'ultima" per percorso; `:2169` e `:2295` diventano una scrittura. Generazione per account come in `BodyCache.Save`, perché `MailCache.Delete` non venga ricreata. Compromessi: con una chiusura brusca entro 300 ms resta la prima pagina di prima (innocuo, il server la rimpiazza); un po' di codice di coordinamento. Togliere o limitare a una esecuzione il `PlainPreview` di `LoadMessages` (`:41`) quando la copia è del formato nuovo.
- **Verifica su Windows:** `Stopwatch` attorno a `SaveFirstPage` in Release PROVA; "scorri 30" mentre arrivano messaggi finti ("arrivo di prova"); righe "interfaccia lenta" prima e dopo.

### B-16 Corpi in anticipo non puliti prima del clic, e logo riletto dal disco a ogni disegno
- **Dove:** `UI/MainWindow.xaml.cs:2320-2340` (`PrefetchListAsync`) e `:2361-2380` (`PrefetchNeighborsAsync`) scaricano ma non chiamano `ReadingPage.Warm`; `UI/ReadingView.xaml.cs:717-722` pulisce al clic (`await Task.Run(() => ReadingPage.Warm(...))`) anche per un corpo preso da `provider.KeptBody` (`:666`); cache per corpo `UI/ReadingPage.cs:451-477` (`ConditionalWeakTable<MessageBody, Prepared>`). Logo: `UI/ReadingView.xaml.cs:972-974` (`LogoOf`, per ogni entry in `Render`, `:927-928`) → `UI/ViewModels.cs:1356-1361` (`Logos.Bytes`: `File.ReadAllBytes` a ogni chiamata dopo `Get`).
- **Scenario:** (1) frecce o Ctrl+. su messaggi già scaricati: ogni passo aspetta la pulizia nel percorso del clic. Dai rapporti (ESITO.md:571-577): 11-22 ms su una newsletter da 211 KB; V-19 stima 7-60 ms per freccia (dal rapporto, non rimisurata). È la prima metà di V-19, ancora aperta. (2) Conversazione di molti messaggi di pochi mittenti, e `Render` due o tre volte per apertura (corpo, conversazione, anteprime): V-32 seconda parte, lasciato aperto; il blocco 9 ha misurato solo la decodifica; stima del rapporto 5-30 ms per `Render` di conversazioni lunghe (non rimisurata).
- **Gravità:** (1) Media, cresce con la dimensione dell'HTML; (2) Bassa-Media, lineare in messaggi × `Render`.
- **Stato:** CONFERMATO nel codice.
- **Proposta:** (1) dopo `PrefetchAsync` lanciare in `Task.Run` `ReadingPage.Warm(body, Reader.Light, remote)` per ogni corpo `KeptBody`, con `remote` calcolato dalla stessa `ReadingView.LoadsImages` di `:712` (da rendere chiamabile da fuori: dipende da `junk`, impostazioni del mittente e `imagesOnce`). La chiave della cache è l'oggetto `MessageBody`, quindi funziona solo se il provider restituisce la stessa istanza (`KeptBody` lo fa). Compromessi: CPU spesa su messaggi che forse non si apriranno (pool); un `Prepared` in più per corpo tenuto; da rifare se cambia tema o "Mostra immagini" (già coperto da `RenderWarmAsync` per i soli visibili). (2) Un `Dictionary<string, byte[]?> bytes` accanto a `images` in `Logos`, svuotato nello stesso punto (`images.Remove(domain)` alla `Arrived`, `ViewModels.cs:1327-1331`): pochi KB per dominio.
- **Verifica su Windows:** (1) "clic N" su 10 righe già scaricate, righe "tempi: HTML pulito in sottofondo" e "clic, testo pronto", prima e dopo; `memoria.ps1`. (2) Una conversazione di 20 messaggi dello stesso mittente (`--page-thread` o vera), "tempi: pagina costruita (N corpi)".

### B-17 Il riconteggio dei non letti parte anche dopo scritture che non li cambiano
- **Dove:** `Core/GmailProvider.cs:990-997` (`EachFolderAsync` avvolge sempre in `InboxIdle.OwnAsync`; vale per `SetReadAsync :999`, `SetFlagAsync :1005`, `CopyAsync :1217`, categoria `:309`), `Core/ImapProvider.cs:1270`, `Core/Idle.cs:63-75` (`WriteAsync` alza `Written` a ogni fine), `UI/App.cs:177-180` (`Written` → `RecountSoon`), `:992-1011`; il conteggio di Gmail e IMAP è `ImapProvider.GetInboxUnreadAsync` (`:966-971`): `FreshInboxAsync(counting: true)` + SEARCH sulla connessione in sottofondo, condivisa con anteprime, corpi in anticipo e copia dei 30 giorni. Graph lo alza solo per letto, sposta, elimina, Posponi (`Core/GraphProvider.cs:392, 922, 961-1015`), non per il contrassegno.
- **Scenario:** una raffica di 50 "segna letto" o 50 Canc va bene: una scrittura per cartella e un riconteggio (`recounting`, `Ticket` aspetta un secondo di quiete, `Core/Idle.cs:43,83-90`). Letti uno a uno con "segna letto subito" (predefinito): un riconteggio per messaggio letto a 1 s dalla scrittura; e un riconteggio per ogni contrassegno su Gmail e IMAP.
- **Gravità:** Bassa-Media. **Stato:** CONFERMATO che i riconteggi partono; il disturbo alla connessione in sottofondo SOSPETTO (due comandi per messaggio: NOOP se oltre 2 s dall'ultimo + SEARCH, in competizione con l'anticipo del vicino che parte nello stesso istante dal clic).
- **Proposta:** `OwnAsync(accountId, write, affectsUnread = true)` e `UnreadSync.WriteAsync(..., affectsUnread)`; `false` per `SetFlagAsync`, `CopyAsync` e categoria. Per `SetReadAsync` il numero locale (`CountRead`) è già esatto: ricontare con quiete di 5 s invece di 1. Dopo un segna letto un disaccordo col server si corregge al controllo successivo (IDLE o 2 minuti) invece che dopo un secondo; le scritture che eliminano o spostano senza aprire (il guasto B del 7 ottobre) restano `affectsUnread`.
- **Verifica su Windows:** `SpeedAuditTests`: conteggio dei comandi del server IMAP finto dopo 20 `SetRead` di fila e dopo 20 `SetFlag`; `loop\bug-I\prova-finta.ps1` con `-Do "letto N"`.

### B-18 L'indice dei suggerimenti si invalida a ogni messaggio che aggiorna un contatore
- **Dove:** `Core/AddressBook.cs:156-191` (`Add`: `bookVersion++` a `:164` per voce nuova e a `:189` per qualunque `changed`, anche solo `Received++`/`LastReceived`), `:226` (`Suggest` ricostruisce l'indice se `index.Book != bookVersion`), `:288-311` (`Build`: per ogni voce `ToLowerInvariant` ×2, `Words` con `Split`/`Concat`/`ToArray`).
- **Scenario:** si scrive nel campo A mentre arriva posta da mittenti già noti: la lettera dopo paga la ricostruzione intera. Dal rapporto: 21,8 ms con 4.500 voci (ESITO.md:1209), lineare nelle voci.
- **Gravità:** Bassa-Media, solo mentre si compone. **Stato:** CONFERMATO per la regola di invalidazione.
- **Proposta:** due contatori, `nameVersion` (voce nuova, nome cambiato, contatti cambiati: serve alla ricostruzione) e `bookVersion` (il resto). Le voci della rubrica nell'indice sono gli stessi oggetti `Entry` (`Build :291`), quindi `Score` e `Last` sono letti dal vivo; solo le voci dei contatti ne copiano i conteggi (`:303-307`), che restano indietro fino alla ricostruzione successiva (cambia l'ordine di pochi suggerimenti).
- **Verifica su Windows:** `Speed2AuditTests` esteso con un `Add` che cambia solo `Received` fra due `Suggest`, tempo della seconda.

### B-19 `Store.Save()` immediato su percorsi di clic: quali si possono accorpare
- **Dove:** tabella sotto. Garanzie di `SaveSoon`: `Core/Store.cs:892-1009` (400 ms dall'ultima richiesta, `MaxWait` 2 s, numerazione che fa vincere l'ultima); `Flush` a `UI/App.cs:100` (fine sessione di Windows), `:222` (`OnExit`), `UI/MainWindow.xaml.cs:456` (`HideToTray`), `Core/Store.cs:841` (`ProcessExit`). Costo di un `Save()`: 12-26 ms con il file dell'autore (ESITO.md:1135-1141).
- **Che cosa si perde al massimo:** l'ultima richiesta di `SaveSoon` se il processo muore senza passare da `Flush` (terminazione forzata, perdita di corrente, crash fatale): i cambi degli ultimi 0,4-2 s. Per le voci proposte sono preferenze o stato cosmetico (cartella, colore, ordine, vista, zoom, immagini consentite, fissati): al riavvio si rifanno con un clic. SOSPETTO a parte: il timer è un `DispatcherTimer` a Background (`:949`), quindi con disegno o input continui può scattare più tardi dei 2 s dichiarati; e `ProcessExit` non è garantito dopo un'eccezione non gestita. Nessuno dei due è provato.
- **Gravità:** Bassa per voce (12-26 ms a clic), ma sul percorso del clic. **Stato:** tabella CONFERMATA (righe lette una per una); la perdita massima è ragionamento.
- **Proposta:** `SaveSoon` per le righe segnate "SaveSoon"; `Save()` per account, token, contrassegni, posposti, invii pianificati, regole, modelli, firme e tutto ciò che altrove si legge subito.
- **Verifica su Windows:** riga "impostazioni scritte (N KB, accorpate, in sottofondo)" invece di "subito" dopo un clic su Mostra immagini, Fissa, Visualizza; test di uscita come V-10 (`esci` subito dopo un cambio).

Già a `SaveSoon` (non elencati): `UI/ViewModels.cs:36, 379`, `UI/MainWindow.Unified.cs:70, 88, 294`, `UI/MainWindow.xaml.cs:1614, 1623, 1649, 3877, 3972, 4456`, `UI/ReadingView.xaml.cs:630`, `UI/SettingsWindow.cs:750, 1017`, `Core/Rules.cs:616`.

| file:riga | funzione e quando | proposta |
|---|---|---|
| `Core/ActionQueue.cs:294` | `ReplayAsync`, solo se `Flags.Follow` ha cambiato | tenere (B-13) |
| `Core/GmailProvider.Calendar.cs:48` | `AllowAsync`, consenso a un ambito | tenere |
| `Core/GmailProvider.cs:301`; `Core/ImapProvider.cs:536`; `Core/GraphProvider.cs:667` | `CreateCategoryAsync` | tenere (rara) |
| `Core/MicrosoftAuth.cs:256, 357` | `HomeAccountId` dopo accesso interattivo | tenere (token) |
| `Core/Rules.cs:130, 136, 148` | `Put`, `Remove`, `Move` regola | tenere |
| `Core/ScheduledSending.cs:17, 42, 64` | pianifica e dopo ogni invio pianificato | tenere (un salvataggio perso rimanda un messaggio) |
| `UI/AccountWindow.xaml.cs:229, 247, 320, 526, 555, 593` | aggiunta di un account | tenere |
| `UI/App.cs:154` | `UnreadableTold` una volta | tenere |
| `UI/App.cs:618` | `SetTheme` | **SaveSoon** |
| `UI/CalendarView.cs:163` | `SetMode`, clic sulla vista | **SaveSoon** |
| `UI/CalendarView.cs:333` | mostra o nascondi un calendario | **SaveSoon** |
| `UI/ComposeWindow.xaml.cs:703` | `OnSaveTemplate` | tenere (testo scritto dall'utente) |
| `UI/ContextMenus.cs:59, 66` | personalizza o ripristina il menu | **SaveSoon** |
| `UI/MainWindow.Flags.cs:78, 98, 117, 136` | contrassegno con scadenza, promemoria, completa, cancella | tenere (dati con data) |
| `UI/MainWindow.Flags.cs:186` | `CheckFlagReminders` (esce prima se nessuno è scaduto) | tenere |
| `UI/MainWindow.Flags.cs:206, 216` | `FlagReminderActionAsync` dalla notifica | tenere |
| `UI/MainWindow.Notifications.cs:222` | Posponi un'ora dalla notifica | tenere (il processo può uscire subito) |
| `UI/MainWindow.xaml.cs:460` | `HideToTray`, `TrayExplained` una volta | tenere |
| `UI/MainWindow.xaml.cs:885` | `SaveAndApply` (Visualizza: conversazioni, densità, riquadro, zoom), prima del ricaricamento | **SaveSoon** |
| `UI/MainWindow.xaml.cs:970, 985` | `AddTest*` (solo prove) | tenere |
| `UI/MainWindow.xaml.cs:1189` | `AddSharedMailboxAsync` | tenere |
| `UI/MainWindow.xaml.cs:1573` | `SetFolderColor` | **SaveSoon** |
| `UI/MainWindow.xaml.cs:1761` | `MigrateFolderKeys` | tenere (cambia identificativi) |
| `UI/MainWindow.xaml.cs:1820` | `DeleteFolderAsync` | tenere |
| `UI/MainWindow.xaml.cs:3125` | `FollowFlags` solo se cambiato | tenere |
| `UI/MainWindow.xaml.cs:3196` | `DeleteAsync` definitivo | tenere |
| `UI/MainWindow.xaml.cs:3365` | `UndoAsync` solo se cambiato | tenere |
| `UI/MainWindow.xaml.cs:3540` | `TogglePin` (Fissa), sul clic prima di `Repin` | **SaveSoon** |
| `UI/MainWindow.xaml.cs:3858, 3883, 3900, 3983` | Posponi, `WakeNowAsync`, `CheckSnoozedAsync` | tenere (già accorpati dal blocco 9) |
| `UI/MainWindow.xaml.cs:4842` | `MoveAccount` | **SaveSoon** |
| `UI/MainWindow.xaml.cs:4929` | `RemoveAccountAsync` | tenere |
| `UI/ManageAccountWindow.cs:370, 447` | Gestisci account | tenere |
| `UI/ReadingView.xaml.cs:1565` | `ShowImages` ("Mostra immagini" per sempre), prima di `RenderWarmAsync` | **SaveSoon** (toglie 12-26 ms dal clic) |
| `UI/SettingsWindow.cs:149, 203, 219` | `Apply`, `Radios`, `Check`: ogni opzione | **SaveSoon** |
| `UI/SettingsWindow.cs:437, 538, 619, 626, 633` | pagine Avvio, Gestione messaggi, Formato | **SaveSoon** |
| `UI/SettingsWindow.cs:782, 794` | Modelli (nuovo, elimina) | tenere |
| `UI/SettingsWindow.cs:1059, 1071, 1084, 1094` | Firme (nuova, elimina, predefinite) | tenere |
| `UI/ViewModels.cs:351, 369, 394` | ordine cartelle, ripristino, nascondi cartella | **SaveSoon** |

### B-20 Intestazioni di "Annulla iscrizione" alla prima apertura di una copia già sul PC: quando partono, e perché non vale la pena cambiarle
- **Dove:** `Core/Offline.cs:345` (`ListHeadersUnread = !saved.ListHeadersRead && saved.ListUnsubscribe == null`, l'unico punto che lo mette a vero), `UI/ReadingView.xaml.cs:729` (parte solo se `OfflineCopy == null`, cioè la copia aperta in linea), `:1433-1458` (`FillListHeadersAsync`), `Core/ImapProvider.cs:1060-1064` e `Core/GmailProvider.cs:775-779` (`RunAsync(..., background, cancel)` + `SenderCheck.FetchAsync` `Core/SenderCheck.cs:482-487`: un `FETCH` di `HEADER.FIELDS (Authentication-Results List-Unsubscribe List-Unsubscribe-Post)` su una cartella aperta con EXAMINE, connessione in sottofondo), `Core/Offline.cs:279-317` (`SetListHeaders` riscrive la copia intera).
- **Quando parte e che cosa aspetta.** Non aspetta nessuno: è lanciata con `_ =` dopo che `entry.Body` è assegnato e prima di `Render` (`:727-731`), quindi non allunga "testo pronto". Partono solo i messaggi con una copia scritta prima dei blocchi 6 e 9, una volta ciascuno. Compete con l'anticipo del vicino, le anteprime e la copia dei 30 giorni sulla stessa connessione in sottofondo; il token dell'apertura toglie dalla coda le richieste non ancora partite quando si cambia messaggio. L'effetto visibile è la riga che entra nella pagina dopo il disegno (`:1449-1451`: attende `shownPage == page` con polling di 50 ms, poi uno script): uno scatto di layout su ogni newsletter vecchia alla prima apertura. I 155-180 ms (ESITO.md:1456, dal rapporto) sono il tempo del `FETCH`.
- **Perché la popolazione è finita.** Le copie nuove nascono con `ListHeadersRead = !body.ListHeadersUnread` (`Core/Offline.cs:229`) e i corpi scaricati da Gmail e IMAP hanno già le due intestazioni (`GmailProvider.cs:945-946`, `ImapProvider.cs:1216-1217`). Le copie vecchie escono dal limite di 30 giorni dalla loro ultima scrittura (`Trim`, `Offline.cs:458-470`), che per quelle fatte prima del 4 ottobre cade entro il 3 novembre 2026; `SetListHeaders` le riscrive, quindi non si ripete.
- **Si possono chiedere insieme all'elenco o allo scaricamento in anticipo?** Allo scaricamento in anticipo non serve: il `FETCH` intero del corpo contiene già le intestazioni; il vuoto è che `PrefetchAsync` salta i messaggi già `OnDisk` (`ImapProvider.cs:1066-1067`). All'elenco sì, aggiungendo le tre intestazioni a `MimeOptions.ImportanceHeaders` (`Core/MimeOptions.cs:15`, usata da tutte le `ListRequest`: `ImapProvider.cs:607, 637, 735, 793, 881, 889`, `GmailProvider.cs:413, 432, 469`): servirebbe solo se si volessero anche logo e fascia senza aprire il messaggio. Costo: byte in più su ogni pagina da 50 (le Authentication-Results di Gmail sono lunghe, con ARC ancora di più; non misurato) e il costo noto di 50-100 ms della richiesta delle intestazioni di importanza (CLAUDE.md) potrebbe salire; per privacy resterebbero in memoria (`SummaryCache`), non nella copia dell'elenco. Sconsigliata come soluzione a questo solo problema.
- **Gravità:** Bassa e in esaurimento. **Stato:** CONFERMATO nel codice (fire-and-forget, popolazione, scadenza); i 155-180 ms sono dal rapporto, non rimisurati.
- **Proposta, in ordine di consiglio:** (A) non cambiare niente: finisce da sola. (B) Se si vuole il layout fermo da subito: riservare lo spazio della riga mentre `ListHeadersUnread` è vero (CSS) oppure aspettare l'esito per al massimo 150 ms prima del disegno (`ShowDelay` prevede già 300 ms per il corpo): un messaggio che arriva 150 ms dopo. (C) Migrazione in blocco dentro `OfflineCopy.RunAsync` a finestra ferma: un solo `UID FETCH uidset (BODY.PEEK[HEADER.FIELDS (...)])` per cartella con fino a 100 UID, con un segno "fatto" come `SenderLogos.MigrationMark`; richiede di decifrare e riscrivere centinaia di file una volta sola (654 copie in 0,75 s per la sola lettura, dal rapporto, con `BodyCache.Each`).
- **Verifica su Windows:** contare le righe "intestazioni di Annulla iscrizione lette dopo il corpo" in tre giorni di uso e verificare che spariscano dopo il 3 novembre.

### B-21 Il profilo di WebView2 non ha controllo di dimensione
- **Dove:** `UI/ReadingView.xaml.cs:1027-1033` (`CreateAsync(null, Path.Combine(Store.Dir, "WebView2"), ...)`), `:1280` (le pagine di Missiva sono `Cache-Control: no-store`; le immagini esterne no).
- **Scenario:** `WebView2\EBWebView\Default\Cache` e `Code Cache` crescono con il numero di newsletter lette fino al tetto interno di Chromium, che qui non conosco.
- **Gravità:** Bassa (disco, non memoria). **Stato:** SOSPETTO: manca la dimensione reale sul PC dell'autore (il profilo persistente è stato valutato per sicurezza, S-08, non per spazio).
- **Proposta:** misurare; se supera qualche centinaio di MB, a `ReleaseWeb` o all'avvio `CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache)` con una soglia di età o di dimensione, oppure `--disk-cache-size=` negli argomenti.
- **Verifica su Windows:** `Get-ChildItem $env:LOCALAPPDATA\Missiva\WebView2 -Recurse | Measure-Object Length -Sum`, ripetuto dopo una settimana.

### B-22 Registro: apertura e chiusura del file a ogni riga, e `WithoutAddresses` ha un andamento quadratico
- **Dove:** `Core/Store.cs:1073-1099` (`Write`: `lock`, `Directory.CreateDirectory`, `new FileInfo(...).Length`, `File.AppendAllText`: tre operazioni sul file per riga, sul thread chiamante), `:1034-1041` (`WithoutAddresses`: regex con timeout 250 ms su `ex.ToString()`, usata da `Log.Error` nella versione installata). Il modello `[A-Za-z0-9._%+'\-]+@...` comincia con una ripetizione senza chiusura: su una riga lunga di caratteri ammessi senza "@" il tempo è quadratico nella lunghezza della sequenza; il timeout la ferma e la riga diventa "(testo dell'errore non registrato)".
- **Scenario:** quasi tutte le righe di `Write` sono nella versione installata ("numero sull'icona", "non letti ... -> ...", "elenco: ...", "controllo di ..."): in un uso normale due o tre righe per messaggio letto; con un antivirus attivo ogni apertura di file è più costosa. V-39 di velocita-2.md.
- **Gravità:** Bassa. **Stato:** CONFERMATO nel codice; costo SOSPETTO.
- **Proposta:** un `StreamWriter` aperto (`FileShare.ReadWrite` perché "Apri il registro" funzioni) con un thread scrittore su `Channel<string>`, svuotato ogni 200 ms e all'uscita; scrivere subito le righe `ERRORE` (sono le più utili in un arresto brusco, come l'overflow dello stack "senza riga nel registro") e accorpare le altre. `WithoutAddresses`: scansione a mano da ogni "@" a sinistra e a destra, lineare e senza regex.
- **Verifica su Windows:** Release PROVA con un client UI Automation che apre 100 messaggi; i millisecondi sul thread UI delle righe di log con un contatore temporaneo; `missiva.log` dell'autore per quante righe all'ora.

### B-23 `SummaryCache`: 2.000 voci con la struttura completa di MailKit per provider
- **Dove:** `Core/ImapProvider.cs:1724-1764` (struttura a posto: dizionario + `LinkedList`, operazioni O(1), un lucchetto per cache); contenuto `:986-990` (`known[...] = i`, un `IMessageSummary` con `Envelope`, `BodyStructure`, `Size`, intestazioni; con `SummaryItems` anche `PreviewText`, `:27`); uno per `ImapProvider` e per `GmailProvider`. Il blocco 4 ha portato il tetto da 1.000 a 2.000 e ha scritto "qualche MB in più al massimo" (ESITO.md:620) senza misura.
- **Gravità:** Media per la memoria (obiettivo dell'autore: 122-137 MB privati, CLAUDE.md), non per la CPU. L'albero di `BodyStructure` e le `Envelope` con liste di indirizzi hanno decine di oggetti per messaggio: 2.000 voci per tre provider possono pesare più di "qualche MB".
- **Stato:** SOSPETTO: ordine di grandezza non misurato.
- **Proposta:** se la misura lo conferma, tenere il tetto e conservare solo ciò che serve a `Summary`/apertura (struttura e dimensioni), oppure tornare a 1.000 con la politica LRU attuale (il problema originale era lo svuotamento totale, non il tetto).
- **Verifica su Windows:** `GC.GetTotalMemory(true)` prima e dopo 2.000 messaggi elencati in una cartella grande (o `dotnet-gcdump`), per un provider e per tre; `memoria.ps1`.

### B-24 Codice aggiunto dagli audit sulle email normali: pulizia dell'HTML, `CleanName`, `NameAddress`, `Skeleton`
- **Dove:** `Core/MailHtml.cs` (`ParseGuard` `:335-367`, `EscapeStrayLt` `:377-402`, `InnerHtml` e `TextOf` iterativi `:484-579`, regex `NonBacktracking` `:737, 740, 817, 833`); `Core/SenderCheck.cs:312-332` (`CleanName`), `:342-391` (`NameAddress`), `:406-427` (`Skeleton`); `Core/Models.cs:118,125` (`Address.Name`, `Address.Display`).
- **Stato: MISURATO** (Linux, 4 CPU, .NET 10.0.12, Release; banco console fuori dal repository, codice in appendice). Le copie di `MailHtml.cs` al commit di oggi e al commit `3ae3675` (3 ottobre, prima degli audit) compilate nello stesso programma con due spazi di nomi; `System.Windows.Media.Color`/`ColorConverter`/`Colors` sostituiti da un guscio minimo (stessi per le due copie; `ConvertFromString` lancia `FormatException` per i nomi sconosciuti come WPF); email generate con tabelle annidate, stili in linea, blocco `<style>`, immagini remote, pixel di tracciamento, link con parametri (5 dimensioni: 6, 26, 81, 159, 788 KB); `GC.Collect()` prima di ogni giro, 5 giri di riscaldamento, **30 giri misurati**, mediana e 95° percentile, byte allocati sul thread. Due esecuzioni complete. Limiti dichiarati: JIT e non ReadyToRun; Linux e non il PC dell'autore; email generate, non le 673 vere della copia di prova; il guscio di `Color` non ha la ricerca per nome di WPF.

Prima esecuzione (mediana / 95° percentile in ms; allocazioni in KB per chiamata):

| Funzione | Input | Vecchio ms | Nuovo ms | Alloc vecchio | Alloc nuovo |
|---|---|---|---|---|---|
| Prepare chiaro, immagini bloccate | 6 KB | 1,55 / 1,71 | 1,81 / 2,42 | 354 | 397 |
| Prepare scuro, immagini bloccate | 6 KB | 2,01 / 2,37 | 2,22 / 2,43 | 378 | 419 |
| ReadableText | 6 KB | 1,55 / 2,01 | 1,31 / 1,94 | 288 | 273 |
| Prepare chiaro, immagini bloccate | 26 KB | 12,60 / 16,14 | 15,39 / 24,30 | 2.037 | 2.073 |
| Prepare scuro, immagini bloccate | 26 KB | 18,64 / 27,71 | 13,44 / 21,37 | 2.596 | 2.563 |
| Prepare chiaro, immagini consentite | 26 KB | 4,60 / 5,49 | 6,34 / 14,97 | 1.156 | 1.213 |
| ReadableText | 26 KB | 3,69 / 6,25 | 2,42 / 2,84 | 706 | 728 |
| Prepare chiaro, immagini bloccate | 81 KB | 10,86 / 28,12 | 11,01 / 16,29 | 5.630 | 6.082 |
| Prepare scuro, immagini bloccate | 81 KB | 13,68 / 14,44 | 12,37 / 13,78 | 7.197 | 7.574 |
| ReadableText | 81 KB | 4,78 / 5,50 | 3,82 / 4,21 | 2.234 | 2.297 |
| Prepare chiaro, immagini bloccate | 159 KB | 15,27 / 15,59 | 16,98 / 17,81 | 11.117 | 12.075 |
| Prepare scuro, immagini bloccate | 159 KB | 21,25 / 27,99 | 22,39 / 25,80 | 14.119 | 14.970 |
| Prepare chiaro, immagini consentite | 159 KB | 10,31 / 18,54 | 11,74 / 12,19 | 6.242 | 7.200 |
| ReadableText | 159 KB | 9,30 / 11,51 | 7,88 / 13,53 | 4.421 | 4.534 |
| Prepare chiaro, immagini bloccate | 788 KB | 154,61 / 194,08 | 165,17 / 191,33 | 55.025 | 59.793 |
| Prepare scuro, immagini bloccate | 788 KB | 191,35 / 317,40 | 202,26 / 278,74 | 69.071 | 73.120 |
| Prepare chiaro, immagini consentite | 788 KB | 88,02 / 123,97 | 101,08 / 124,08 | 30.183 | 34.935 |
| ReadableText | 788 KB | 61,83 / 76,68 | 46,87 / 69,37 | 20.949 | 21.875 |
| PlainPreview (600 car.) | 600 car. | 34,7 / 57,3 µs | 35,7 / 41,0 µs | 5,6 | 5,6 |

La seconda esecuzione dà gli stessi segni: a 788 KB `Prepare` chiaro 150,0 contro 154,5 ms, scuro 190,3 contro 203,2, `ReadableText` 60,4 contro 45,1; a 159 KB `Prepare` chiaro 15,4 contro 18,7, scuro 21,1 contro 23,7; allocazioni identiche alla prima (deterministiche).

**Lettura.** (1) Le allocazioni di `Prepare` sono **più alte del 2-9% con il codice di oggi**, in modo riproducibile (a 159 KB 11.117 → 12.075 KB, +8,6%; a 788 KB 55.025 → 59.793 KB, +8,7%; con immagini consentite fino a +16% a 788 KB). (2) Il tempo di `Prepare` è più alto di circa 3-7% sulle email grandi (788 KB: +6,8% e +3,0% nelle due esecuzioni), cioè 4,5-10,6 ms, **in linea con "4-10 ms per MB" di CLAUDE.md**; sotto i 200 KB la differenza è dentro il rumore fra esecuzioni (le mediane a 26 KB variano da 12,6 a 15,4 ms con lo stesso codice), e a volte il codice nuovo è più veloce. (3) `ReadableText` è **più veloce del 15-25%** con il codice nuovo (le correzioni iterative di `TextOf`/`ToText`). (4) `PlainPreview` invariata. Nessun problema di velocità sulle email normali: il costo degli audit su `Prepare` è piccolo e già noto; resta il 9% di allocazioni in più (garbage sul pool, non sul thread dell'interfaccia).

Per indirizzo, `SenderCheck` (solo codice nuovo: queste funzioni prima non c'erano; 1.000 indirizzi con 18 nomi e 4 indirizzi realistici, compresi nomi con accenti, larghezza intera, cirillico e spazi invisibili; 30 giri):

| Funzione | Mediana per 1.000 | 95° pct | Alloc per 1.000 | Per indirizzo |
|---|---|---|---|---|
| `CleanName` | 0,475 ms | 0,698 ms | 86 KB | 0,48 µs |
| `NameAddress` (via `Address.Display`) | 1,079 ms | 1,319 ms | 290 KB | 1,08 µs |
| `Skeleton` (per nome, in `LookAlike`) | 1,075 ms | 1,234 ms | 195 KB | 1,08 µs |
| `NameAddress`, nome di 228 caratteri con molti punti | 2,071 ms | 10,855 ms | 1.750 KB | 2,07 µs (95°: 10,9 µs) |

Una pagina di 50 righe: circa 50 × (0,48 + 1,08) = 78 µs in tutto, una volta per indirizzo (`Address.Display` è calcolato una volta, `Models.cs:125`). **Trascurabile.**
- **Gravità:** Bassa (nessun problema).
- **Proposta:** nessuna per la velocità. Per `CleanName` e `Safety.SafeFileName` vedi B-32.
- **Verifica su Windows:** le stesse funzioni dentro `--prova-bozze` (`HtmlAuditTests`) con ReadyToRun; non serve altro.

### B-25 Loghi dei mittenti: dizionario senza tetto, e ogni avviso "logo cambiato" visita tutte le righe
- **Dove:** `UI/ViewModels.cs:1321-1322` (`images`, `asked`), `:1337-1347` (`Get`: carica e tiene un `BitmapImage` congelato per ogni dominio autenticato mostrato; `images[domain] = ...` anche null), `:1366-1378`; `UI/MainWindow.xaml.cs:572-576` (`OnLogo`: `foreach (var row in rows.Rows) if (row.IsOfDomain(domain))`), `UI/ViewModels.cs:836` (`IsOfDomain` → `SenderLogos.DomainOf(Person.Email)`, `Core/SenderLogos.cs:79-91`: `Trim`, `ToLowerInvariant`, `Split`, `Contains`/`All` con lambda, `Join`, per riga), `:1327-1331` (l'evento arriva sul thread UI con `BeginInvoke`); `NoteBody` `:173` e `:189` lo fanno partire una volta per messaggio e sessione e quando un dominio diventa autenticato.
- **Scenario:** (1) scorrere migliaia di righe (centinaia di domini con logo) lascia in memoria un PNG decodificato per dominio fino all'uscita; nell'area di notifica non si liberano (raggiungibili dal dizionario statico). Disegnato a 96×96 (CLAUDE.md): circa 36 KB decodificati per dominio come limite superiore teorico, da moltiplicare per i domini con logo mostrati. (2) Per righe × avvisi: ogni avviso di dominio ricalcola `DomainOf` per tutte le righe; per le righe visibili `MessageItem.Logo` (`:834`) rifà `DomainOf` e due `IsAuthenticated` sotto lucchetto a ogni `Compute` (`MessageRowElement.cs:546`). Dai rapporti la quota di messaggi non autenticati è piccola (620 su 624 autenticati nella copia di prova, ESITO.md:408-409), quindi gli avvisi sono pochi.
- **Gravità:** (1) Bassa-Media, cresce con i domini visti; (2) Bassa. **Stato:** CONFERMATO che manca un tetto e come si visitano le righe; le quantità SOSPETTE.
- **Proposta:** (1) LRU a 256-512 domini (una `LinkedList` come `SummaryCache`) o `WeakReference<ImageSource>` per le voci fuori vista; svuotare `images` in `HideToTray` (le righe si ricreano da disco in 1 ms ciascuna: da misurare prima). (2) Dominio memorizzato nel `MessageItem` (calcolato una volta, come `Sender`) o un `Address.Domain` in cache come `Display`; accorpare gli avvisi di dominio con un insieme in attesa e un solo passaggio sulle righe per volta; per l'avviso "dubbioso" rinfrescare solo la riga di quel messaggio.
- **Verifica su Windows:** `memoria.ps1` prima e dopo aver scorso tutta la Posta in arrivo di Gmail (un comando di `MISSIVA_KEYS` che conta le voci di `Logos.images`); "scorri 120" prima e dopo (allocazioni) e un caso con 2.000 righe caricate e 50 avvisi.

### B-26 Contenitori che crescono con il tempo (Missiva aperta per giorni)
- **Dove e che cosa:** `UI/App.cs:12` (`notified`: una stringa `account|id` per messaggio annunciato, mai tolta: 250-300 byte l'una con gli id lunghi di Graph, stima) con `:892` (filtro) e `:924` (`Add`); `UI/MainWindow.Calendar.cs:114` (`upcoming`), `:118-122` (`RememberEvents`: `kept = ... Where(e => e.Start < data.From || e.Start >= data.To)` tiene tutto ciò che è fuori dall'intervallo appena letto, quindi anche gli eventi di ieri), `:145-165` (`CheckRemindersAsync` itera tutto `upcoming` ogni 30 s); `UI/Templates.cs:35` (`plain`: chiave = tutto il testo RTF del modello, 100-200 KB con un logo, una voce nuova a ogni modifica del modello dopo una ricerca nel pannello); `Core/SenderLogos.cs:106` (`seen`, domini, anche su disco: `domini.dat` riscritto intero 5 s dopo ogni dominio nuovo, `:237-260`, con `File.WriteAllText` senza `SafeFile`), `:151` (`doubtful`, una voce per messaggio senza prova), `:48` e `:344-380` (`.none` e PNG per dominio, mai cancellati; i `.none` si riprovano dopo 14 giorni ma non si tolgono); `UI/NotificationAvatar.cs:20-46` (un PNG da 128 px per colore e iniziali, in `notifiche`, mai cancellato); `Core/Store.cs:1095` (`missiva-errori.log` senza rotazione; `lastFailure` è un solo valore, quindi un errore che alterna scrive di continuo); `Core/Store.cs:595-614` (`SafeFile.SetAside`: file messi da parte, mai tolti per scelta).
- **Scenario e gravità:** tutte crescono con la varietà dei dati più che col tempo. Gravità Bassa per ognuna (memoria o disco piccoli); `upcoming` è lineare in (eventi con promemoria per giorno × giorni di uptime) più un `Where` sull'intera lista ogni 30 s (mille oggetti `CalendarEvent` con partecipanti e descrizione in 100 giorni con 10 eventi al giorno); `notified` ha anche un difetto di correttezza: per IMAP, se il server cambia `UIDVALIDITY` (ricostruzione, migrazione) gli UID ripartono e un messaggio nuovo con UID uguale a uno già annunciato non riceve la notifica fino al riavvio, perché la chiave è `account|UID` senza `StableId` né validità. `domini.dat` non atomico: un crash a metà lascia il file troncato, `Load` ripartirebbe da zero (`SenderLogos.cs:226-241`) e i domini autenticati vanno persi finché non rientrano.
- **Stato:** CONFERMATO (letto, assenza di tetti verificata con grep: per `notified` solo `Contains` e `Add`); il caso `UIDVALIDITY` SOSPETTO (manca un server che cambia validità).
- **Proposta:** `notified` come coda con tetto (2.000, come `RulesSeen`) e chiave con `StableId` quando c'è; `RememberEvents` scarta i `kept` con `ReminderAt < DateTime.Now.AddMinutes(-1)`; `Templates.plain` con chiave modello + lunghezza o un hash e potatura a 50 voci; `SafeFile.Write` per `domini.dat`; potatura dei domini con `Last` più vecchio di 12 mesi; pulizia di `notifiche` oltre 500 file o 90 giorni; rotazione di `missiva-errori.log` come `missiva.log`.
- **Verifica su Windows:** dimensione e numero di file di `%LOCALAPPDATA%\Missiva\loghi` e `notifiche` sul PC dell'autore dopo settimane; `--imap-finto` con `UIDVALIDITY` cambiata e UID ripetuto, un solo messaggio nuovo; `--calendario-finto` con un promemoria ogni ora e la data del PC portata avanti.

### B-27 PEC: busta fino a 30 MB, copie in memoria, ultime buste trattenute per tutta la sessione
- **Dove:** `Core/Pec.cs:53` (`MaxSize` 30 MB), `:74-82` (`OpenAsync`: `raw` + `MimeMessage.Load(new MemoryStream(raw))` + `EmlProvider.BodyOf(inner)` che materializza gli allegati, `:168-171`), `:85-89` e `:112-120` (`opened`: 8 buste, 64 MB di allegati in tutto, trattenute finché Missiva è aperta). `OpenAsync` gira già su pool (`:78`).
- **Scenario:** apertura di una PEC con 25 MB di allegati: il picco contemporaneo è `raw` (fino a 30 MB) + l'albero MimeKit di una copia + gli allegati decodificati (circa tre quarti del base64) + il contenuto in `Attachment.Content`: 2-3 volte la dimensione della busta, nello heap dei grandi oggetti. Poi `Remember` tiene fino a 64 MB per tutta la sessione, anche nell'area di notifica (`CompactWhenIdle` non li libera: riferimenti vivi).
- **Gravità:** Bassa-Media per la memoria dopo l'uso; il picco è transitorio. Tetto: 64 MB + `Html` di 8 buste. **Stato:** CONFERMATO tetto e durata; picco SOSPETTO (stima dalla struttura).
- **Proposta:** età massima per `opened` (30 minuti dopo l'ultimo `Recall`) con pulizia dal timer del minuto; svuotare `opened` in `HideToTray`.
- **Verifica su Windows:** PEC vera da 20 MB, `memoria.ps1` prima, durante e dopo; poi chiudere nell'area di notifica e controllare i MB privati dopo 10 s.

### B-28 "Esegui ora" delle regole: fino a dieci pagine e 500 corpi, in fila, senza avanzamento né Annulla
- **Dove:** `UI/RulesView.cs:483-532` (`RunNowAsync`: `for (page < 10) await provider.GetMessagesAsync(folder, null, next)` una pagina alla volta), `Core/Rules.cs:452-530` (`RunAsync`: per ogni messaggio con regole sul testo `await provider.GetBodyAsync(m, cancel)` uno alla volta, `cancel` non passato da `RulesView`), `:384-405` (`MatchesBesidesText` ricostruisce un `HashSet` degli indirizzi propri per regola e per messaggio; `:386` `account.Identities.Select(...).ToHashSet()`).
- **Scenario:** regola con "il testo contiene" su una cartella da 500 messaggi non ancora sul PC: 10 viaggi per l'elenco più fino a 500 scaricamenti sequenziali (Gmail sulla connessione dei corpi, Graph uno per volta, benché `MailboxGate` ne ammetta quattro), mentre il testo dice solo "Esegui ora ... su cartella..." e le Impostazioni non hanno Annulla. Se si chiudono le Impostazioni, `Dialog.Ask(Owner...)` può sollevare su un proprietario chiuso (finisce nel `catch` generale). L'analisi del testo è già su pool (V-08).
- **Gravità:** Media come attesa, Bassa come rischio; lineare nei messaggi senza corpo sul PC. **Stato:** SOSPETTO: manca il tempo con una casella vera (i corpi in `KeptBody` sono pochi per le cartelle fuori dai 30 giorni).
- **Proposta:** due o tre corpi in parallelo (stesso limite di `MailboxGate`/Gmail), contatore nella riga di stato ("Letti 120 di 500"), `CancellationTokenSource` legato alla chiusura della finestra, e gli indirizzi propri calcolati una volta fuori dai cicli.
- **Verifica su Windows:** Gmail con una cartella di 500 messaggi vecchi e una regola sul testo: tempo di "Esegui ora" fino alla conferma, righe "regole: Esegui ora su N messaggi".

### B-29 Invio SMTP: una connessione nuova (TLS e XOAUTH2) a ogni invio, che si può preparare durante "Annulla invio"
- **Dove:** `Core/GmailProvider.cs:1250-1265` (`SmtpSendAsync`: `new DsnSmtpClient`, `ConnectAsync` STARTTLS, `AuthenticateAsync(await CredentialsAsync())`, `SendAsync`, `DisconnectAsync`), `:1300-1303` (`SendAsync`: `BuildAsync` poi SMTP, in fila); `Core/ImapProvider.cs:171-190` (`SmtpAsync` uguale), `:1498-1514` (`DeliverAsync`: SMTP poi `APPEND` in Posta inviata, in fila); `UI/ComposeWindow.xaml.cs:1644` (`WaitToSendAsync`, attesa di 5-10 s) e `:1679` (`provider.SendAsync`); `Core/MimeOptions.cs:69-73` (il flag `deliveryReceipt` è nel costruttore di `DsnSmtpClient`).
- **Scenario:** dopo l'attesa di Annulla invio, che non fa nulla di utile per la rete, parte `BuildAsync` (per le risposte può leggere dall'originale) e solo dopo si apre la connessione, ci si autentica (CLAUDE.md: XOAUTH2 con Gmail da 1 a 2 s) e si invia; con IMAP altri tempi per l'`APPEND` in Posta inviata, che l'utente aspetta (la barra "Invio in corso" resta fino alla fine) pur non servendo alla consegna.
- **Gravità:** Bassa come CPU; il guadagno è latenza di consegna e durata della barra. Stima da CLAUDE.md, 1-2 s per Gmail; non misurato sul solo SMTP. **Stato:** SOSPETTO il guadagno; sequenza CONFERMATA.
- **Proposta, per rischio crescente:** (1) in `SendAsync` di Gmail e IMAP avviare `ConnectAsync + AuthenticateAsync` e `BuildAsync` insieme (`Task.WhenAll`) e, se uno fallisce, `Dispose` dell'altro: non cambia la vita della connessione. (2) Durante `WaitToSendAsync` un `IMailProvider.PrepareSendAsync(CancellationToken)` che apre e autentica il client e lo consegna al `SendAsync` successivo; se l'utente preme Annulla o l'attesa supera 20 s si chiude. Vita massima di pochi secondi, quindi niente connessioni vecchie da verificare; `DsnSmtpClient` deve poter essere configurato dopo la creazione. (3) Connessione calda all'apertura della finestra di scrittura: **sconsigliata ora**: sessioni SMTP aperte per ogni finestra e per minuti o ore (limiti del server sulle connessioni contemporanee, chiusura per inattività non rilevabile da `IsConnected`, quindi il primo invio può fallire e va riprovato), credenziali e token vivi più a lungo, una connessione per ogni finestra aperta in sola lettura e per ogni `mailto:`; Graph non usa SMTP, nessun guadagno; l'`APPEND` resta in coda. Servirebbero un NOOP prima dell'uso, un tetto di vita di 2 minuti e un ripiego silenzioso.
- **Verifica su Windows:** tempo da "Invio" (fine attesa) a "partito" nel registro con Gmail e con un IMAP, prima e dopo i passi 1 e 2; Annulla durante la preparazione, con il registro del server o del provider per vedere che non restino sessioni.

### B-30 Le scritture nella Posta in uscita tengono un timer da 1 Hz
- **Dove:** `UI/ComposeWindow.xaml.cs:465` (`autoSave`, 1 s), `:476-480` (`Touched` lo avvia), `:1781-1785` (`PutInOutbox`: `IsEnabled = false`, `Hide()`, ma non ferma `autoSave`), `:497-500` (`autoSave.Stop()` solo dopo un salvataggio).
- **Scenario:** si scrive, si preme Invia subito (entro i 10 s di `AutoSaveIdle`), la rete manca: la finestra va nella Posta in uscita nascosta e spenta, e il timer scatta ogni secondo per tutto il tempo in cui il messaggio resta lì (ore, giorni nell'area di notifica); ogni tick esce subito (`!IsEnabled`), ma è una sveglia al secondo per finestra, contro l'obiettivo di riposo dell'area di notifica. Lo stesso, per pochi secondi, durante Annulla invio.
- **Gravità:** Bassa. **Stato:** CONFERMATO.
- **Proposta:** `autoSave.Stop()` in `PutInOutbox` e in `WaitToSendAsync`; `Touched()` lo riavvia già se la finestra si riapre (`LeaveOutbox`, Annulla).
- **Verifica su Windows:** Posta in uscita con una bozza, `dotnet-counters` o Process Explorer: sveglie al secondo dopo mezz'ora.

### B-31 Area di notifica per giorni: nessuna compattazione periodica dopo i giri in sottofondo
- **Dove:** `UI/MainWindow.Offline.cs:274-282` (`CompactWhenIdle`: `if (!IsVisible || ...) return;`, quindi mai con la finestra nascosta), `UI/MainWindow.xaml.cs:486-496` (`CompactLaterAsync`: una raccolta compattata 5 s dopo la chiusura), `UI/MainWindow.Offline.cs:305-316` (`CompactAfterCopy` solo se un giro ha salvato corpi).
- **Scenario:** Missiva nascosta per giorni; controllo ogni due minuti, delta ogni 30 s, sessione IDLE, giri della copia dei 30 giorni e loghi allocano e liberano; nessuna raccolta compattata dopo quella a 5 s dalla chiusura, tranne `CompactAfterCopy`. Le misure di CLAUDE.md (81 MB privati dopo 10 s; 65-81 MB avviata nascosta) sono di breve durata; a 24-48 ore non ce ne sono.
- **Gravità:** Bassa, SOSPETTO: la memoria privata può restare più alta del necessario per frammentazione dello heap dei grandi oggetti dopo i giri della copia o le PEC (B-27). **Stato:** SOSPETTO: manca una misura a 24-48 ore.
- **Proposta:** solo se la misura mostra crescita, estendere `CompactWhenIdle` alla finestra nascosta con un intervallo minimo di alcune ore e solo dopo attività vera (un controllo che ha trovato messaggi o un giro della copia), mai a ogni minuto.
- **Verifica su Windows:** `memoria.ps1` ogni ora per 48 ore con `--nascosta` o chiusa nell'area di notifica (privati, heap .NET dalla riga "memoria compattata", processi WebView2 = 0), con la casella vera attiva e un messaggio ogni tanto.

### B-32 Micro-allocazioni nei punti di confine (nomi, allegati, pulizia): bassa priorità
- `SenderCheck.CleanName` (`Core/SenderCheck.cs:312-332`): alla prima lettera non ASCII passa alla via lenta, e `result.Append(rune.ToString())` (`:328`) alloca una stringa per carattere; `StringBuilder.Append(Rune)` esiste. Gira a ogni nascita di `Address` (`Core/Models.cs:118`), anche a ogni deserializzazione della copia locale e della ricerca non in linea.
- `Safety.SafeFileName` (`Core/Safety.cs:52`): `Path.GetInvalidFileNameChars().Concat(...).ToArray()` a ogni chiamata; `WithoutUnseen` (`:71-78`) alloca `rune.ToString()` per carattere; gli allegati con accenti non passano da `IsPlainName` (`:81-88`) e pagano questo per ogni nome. Un array statico basta.
- `MailHtml.Clean` (`Core/MailHtml.cs:85,106,149,164,181`): per ogni elemento fino a cinque liste LINQ (`Attributes.Where(...).ToList()`) e un array `new[] { "src", "background", "poster" }` anche quando non c'è nulla da togliere; su un thread del pool ma è garbage per ogni newsletter (parte del +8% di allocazioni di B-24).
- `MessageItem.DateText` (`UI/ViewModels.cs:781-784`): `Store.Settings.Snoozed.FirstOrDefault(...)` per ogni `Compute`, lineare nei posposti.
- `MailHtml.ParseColor` (`:952-985`): `ColorNames` si inizializza alla prima parola in tema scuro (`:1009-1013`, riflessione su `Colors`); il campione di riscaldamento ha solo `#hex`; un colore con nome nel campione la anticipa.
- **Gravità:** Bassa; nessuna misura (B-24 dà la quota di `Clean`), costo per chiamata piccolo. **Stato:** CONFERMATO nel codice. **Verifica su Windows:** "scorri 120" per `DateText` e `Logo`, "HTML pulito" per `Clean`.

### B-33 La copia dei 30 giorni e il prefetch di Graph occupano i posti dell'apertura
- **Dove:** `Core/GraphProvider.cs:723-725` (`GetBodyAsync` accende sempre `opening.Value = true`), `:52-89` (`MailboxGate`: l'apertura usa solo `all`, le altre `others` + `all`), `:764-787` (immagini cid quattro alla volta dentro `ReadBodyAsync`); `OfflineCopy` chiama `GetBodyAsync` per Graph.
- **Scenario:** copia dei 30 giorni in corso e clic su un messaggio: le richieste delle immagini riempiono tutti i posti di `all` e il clic aspetta la fine di una (decimi di secondo). Già annotato fra le scelte del blocco 9 (ESITO.md:1500-1502) come accettato, senza misura.
- **Gravità:** Bassa, solo Graph e solo a finestra ferma. **Stato:** CONFERMATO nel codice.
- **Proposta:** un `AsyncLocal` "in background" (come `MicrosoftAuth.InBackgroundAsync`) che impedisca a `GetBodyAsync` di accendere `opening` per la copia dei 30 giorni.
- **Verifica su Windows:** un clic durante un giro di `OfflineCopy` con l'account Outlook personale, riga "in fila N ms".

### B-34 Minori
- `PersonCard.LoadAsync` (`UI/PersonCard.cs:171-189`): `provider.SearchAsync(null, query)` senza token di annullamento; chiusa la scheda la ricerca continua (su Gmail occupa la connessione delle conversazioni, che serve all'apertura dei messaggi); chiede una pagina da 50 per mostrarne 8 più 8 file. Bassa.
- `GraphProvider.early` (`Core/GraphProvider.cs:698`): quattro voci, ma `early.Keys.First()` su un `Dictionary` non è "il più vecchio" (l'ordine di enumerazione non è garantito dopo le rimozioni): può espellere un corpo appena richiesto. Bassa; correttezza dell'ottimizzazione, non memoria.
- `Network.ApiHandler` (`Core/Offline.cs:87-92`): manca `PooledConnectionLifetime` (default infinito), quindi le connessioni attive non si rinnovano mai. SOSPETTO a basso rischio, non provato.
- `deltaTimer` non si ferma se l'ultimo account Graph viene rimosso (un tick a vuoto ogni 30 s). Trascurabile.

## B.5 Timer a riposo (CONFERMATI nel codice)

"Nascosta" = finestra principale nell'area di notifica (`HideToTray`).

| File:riga | Intervallo | Che cosa fa | Attivo con finestra nascosta |
|---|---|---|---|
| `UI/App.cs:168` (`poll`) | 2 minuti | `CheckNewAsync(periodic)`: nuovi messaggi, coda, regole di Missiva; rete di sicurezza ogni giro (`SafetyNet` 100 s) | sì |
| `UI/App.cs:711` (`deltaTimer`) | 30 s | query delta di Graph, solo se c'è un account Graph; una richiesta HTTP per account | sì |
| `UI/MainWindow.xaml.cs:3791`, `133-141` (`snoozeTimer`) | 1 minuto | `CheckSnoozedAsync`, `SendScheduledAsync`, `CheckFlagReminders`, `Safety.CleanTemp` su pool | sì |
| `UI/MainWindow.Offline.cs:237,257-262` (`offlineTimer`) | 1 minuto | `OfflineCopyAsync` (un giro per account ogni 30 minuti, solo con finestra ferma o nascosta) e `CompactWhenIdle` (solo visibile) | sì (la copia sì, la compattazione no) |
| `UI/MainWindow.Calendar.cs:131` (`reminderTimer`) | 30 s | `CheckRemindersAsync`: lettura dei calendari ogni 30 minuti, scorre `upcoming` (B-26) | sì |
| `Core/Idle.cs:114,237` | rinnovo ogni 9 minuti; dopo errore 5 s raddoppiando fino a 5 minuti | connessione ferma in attesa del server (nessuna sveglia locale oltre al rinnovo) | sì |
| `UI/App.cs:534` (`themeCheck`) | 250 ms | solo dopo un cambio di tema di Windows, fino a 8 tentativi | solo a evento |
| `Core/Store.cs:949` (`SettingsSaver.timer`) | 400 ms, un colpo | salva le impostazioni dopo `SaveSoon` | solo a evento |
| `Core/AddressBook.cs:516` (`saveTimer`) | 5 s, un colpo | scrive la rubrica (B-14) | solo a evento |
| `Core/SenderLogos.cs:245` (`Task.Delay`) | 5 s, un colpo | scrive `domini.dat` (B-26) | solo a evento |
| `Core/PdfWorker.cs:305,439` | 20 s dopo la chiusura dell'anteprima, 1 minuto senza richieste | chiude il processo dei PDF | solo con l'anteprima |
| `UI/ComposeWindow.xaml.cs:465` (`autoSave`) | 1 s | salvataggio automatico della bozza; resta acceso nelle scritture della Posta in uscita (B-30) | sì, solo con scritture nella Posta in uscita |
| `UI/MainWindow.xaml.cs:488` (`CompactLaterAsync`) | 5 s, un colpo | raccolta compattata dopo la chiusura | solo alla chiusura |
| `UI/App.cs:669` (`ScanSentLaterAsync`) | 15 s dopo l'avvio, una volta | rubrica dalla Posta inviata (poi `App.cs:952`, al massimo ogni ora dopo una notifica) | no |
| `UI/MainWindow.xaml.cs:3245` (`undoTimer`), `UI/AttachmentPreview.cs:188`, `UI/PeopleView.cs:317`, `UI/SettingsWindow.cs:739,1021`, `UI/ScrollHitGuard.cs:98`, `UI/App.cs:512` | da 25 ms a 20 s, un colpo o solo durante un gesto | barra Annulla; raccolta dopo l'anteprima; "Copiato"; firme e modelli; ripresa del hit test; fine sessione | no |

Eventi di sistema senza timer: `NetworkChange.NetworkAddressChanged` (`UI/App.cs:183`, tolto a `:219`), `SystemEvents.UserPreferenceChanged` (`:110`, tolto a `:218`), tutti e due tolti in `OnExit`. `CompositionTarget.Rendering` solo durante le animazioni (B.0). Sveglie al secondo a riposo senza scritture in uscita: nessuna; con una scrittura nella Posta in uscita: una (B-30).

## B.6 Contenitori che crescono: riepilogo

Senza tetto né potatura nel codice: `AddressBook.entries` (B-14), `SenderLogos.seen`/`doubtful`/`.none`/PNG dei loghi (B-26), `Logos.images`/`asked` (B-25), `App.notified` (B-26), `upcoming` (B-26), `Templates.plain` (B-26), `notifiche\*.png` (B-26), `missiva-errori.log` (B-26), cartella `WebView2\` (B-21), `Pec.opened` con tetto 8 voci e 64 MB ma senza età (B-27). A posto, con il loro tetto: `SummaryCache` 2.000 (B-23 per il peso), `bodyCache` di Gmail/IMAP 20, `ReadingView.conversations` 50, `notifiedMessages` 50, `RulesSeen` 2.000, `RecentSearches` 10, `SuggestSearches.asked` 40, `badgeImages` 400, `ProtectedTextConverter.known` (potato a ogni istantanea), `missiva.log` 1 MB più `.old`, `cache\corpi` 200 MB e 30 giorni per account, `%TEMP%\Missiva` pulita all'ora; i dizionari con una voce per account (`Idle.states`, `own`, `MicrosoftAuth.apps`/`tokens`/`declined`, `GoogleAuth.Tokens`, `GraphProvider.gates`, `Categories.Known`, `BodyCache.generations`/`sizes`, `ContactBook.remote`).

## Tabella riassuntiva, per guadagno atteso

Il "guadagno atteso" è qualitativo, in ordine di effetto sull'utente; dove c'è un numero, è di una misura riportata nel punto. Nessun guadagno in millisecondi su Windows è dimostrato qui.

| Rango | Punto | Gravità | Stato | Guadagno atteso |
|---|---|---|---|---|
| 1 | B-01 + B-02 + B-03: coda del cursore, ripresa del hit test dal fotogramma, `FrameClock` che si ferma al primo input | Media | CONFERMATO (codice e sorgente WPF); effetto SOSPETTO | fino a ~1 s di disegno continuo in meno dopo ogni scorrimento veloce; clic e tasti non più dietro la soglia di 500 ms di WPF |
| 2 | A-15 + A-16: stato "Non in linea"/"Non raggiungibile" che non si ripulisce, IDLE che non si sveglia | Media | CONFERMATO | cartelle e conteggi giusti per tutta la sessione; azioni non più in coda a rete tornata; notifiche in tempo |
| 3 | A-13: timer di Posponi, invii pianificati, promemoria, copia dei 30 giorni dietro `WhenAll` | Media | CONFERMATO; durata SOSPETTA | fino al timeout più lungo (60-120 s) con rete appesa all'accesso |
| 4 | B-13: azioni in coda non su disco durante la ripetizione; Graph una per volta | Media | CONFERMATO; tempi SOSPETTI | nessuna perdita di lavoro; ripetizione fino a ~4× più rapida (stima) |
| 5 | A-03: firme e modelli RTF con DPAPI all'avvio | Media | CONFERMATO; peso SOSPETTO | dipende da numero e dimensione delle firme (RTF di 100-200 KB con logo) |
| 6 | A-04 + A-12 + A-21: costruttori statici (certificati, `HttpClient`, MSAL, MailKit, regex) sul thread dell'interfaccia | Bassa-Media | CONFERMATO; costo SOSPETTO | alcune decine di ms prima del primo elenco (ordine di grandezza dalle misure relative di A-21, non di Windows) |
| 7 | A-09: WebView2 e pulizia dell'HTML dalla riapertura dall'area di notifica | Media | CONFERMATO; guadagno SOSPETTO | primo messaggio dopo la riapertura verso i 0,25-0,32 s (CLAUDE.md) invece di 0,6-1,2 s se si clicca presto |
| 8 | A-01 + A-02: `Store.Load` in parallelo al tema, `ThemeMode` una volta sola | Media | CONFERMATO l'ordine; guadagno SOSPETTO | 150-200 ms prima per token e collegamento di Gmail; una seconda apertura di Fluent in meno se il setter ricarica |
| 9 | A-06 + A-07 + A-08: layout doppio, XAML nascosto, copia di tutti gli account | Media | SOSPETTO / CONFERMATO | fette dei 257 ms "dell'XAML" e dei 120-200 ms fra cartelle ed elenco; da misurare per gruppo |
| 10 | A-11: pacchetto (30 MB di proiezione WinRT in ReadyToRun, composito da 155 MB, file unico contro cartella) | Media come insieme | MISURATO (contenuto); effetto SOSPETTO | fino a -30 MB di file e di composito; l'effetto sul freddo si sa solo con A.7 |
| 11 | B-11: `IsLast` quadratico sui `<br>` | Media se capita | MISURATO (andamento) | da 2-3 s a ~0 con 40.000 `<br>` (Linux) |
| 12 | B-14 + B-15: lucchetto della rubrica e dei contatti, copia locale scritta sul thread UI | Bassa-Media | CONFERMATO; ms SOSPETTI | meno blocchi dopo ogni arrivo e ogni apertura di cartella |
| 13 | B-19: `Store.Save()` immediato su clic | Bassa per voce | CONFERMATO | 12-26 ms per clic (dal rapporto del blocco 8) su ~20 percorsi |
| 14 | B-16: corpi in anticipo non puliti; logo riletto | Media | CONFERMATO | 7-60 ms per freccia, 5-30 ms per `Render` di conversazioni lunghe (dai rapporti, non rimisurati) |
| 15 | A-14 + A-17 + A-18 + A-19 + A-20: avvio nascosto che lavora per nessuno, tentativi unici, manutenzione a +20/+30 s, rete appesa | Bassa-Media | CONFERMATO; costi SOSPETTI | meno concorrenza con l'avvio di Windows; promemoria e contatti recuperati |
| 16 | B-12 + B-10 + B-09: bozza ripresa con corpo grande, calendario, raggruppamento | Bassa-Media | SOSPETTO | da misurare |
| 17 | B-17 + B-18: riconteggio dei non letti dopo scritture che non li cambiano, indice dei suggerimenti | Bassa-Media | CONFERMATO | meno comandi sulla connessione in sottofondo; 21,8 ms con 4.500 voci evitati (rapporto) |
| 18 | B-20: intestazioni di Annulla iscrizione | Bassa | CONFERMATO | nessuno: finisce da sola il 3 novembre 2026 |
| 19 | B-05 + B-06 + B-07 + B-08 | Bassa-Media | SOSPETTO / CONFERMATO | tenuta nel tempo (tema), fino al 7% per layout (WebView2), etichette giuste a mezzanotte |
| 20 | B-21..B-23, B-25..B-34 | Bassa | CONFERMATO / SOSPETTO | memoria e disco a lungo termine; vedi i punti |
| n/d | B-24: costo degli audit su email normali | Bassa | MISURATO | nessun intervento: +3-7% di tempo a 788 KB, +8% di allocazioni, `ReadableText` -15-25% |

## Guardato e trovato a posto

- **Pulizia dell'HTML (B-24):** `ParseGuard` (`Core/MailHtml.cs:335-367`) è O(1) per gettone con un controllo di memoria ogni 64; `Created` risale al massimo 512 antenati; sulle email vere (profondità 84) qualche migliaio di passi; il costo misurato qui è nel 3-7% a 788 KB. `EscapeStrayLt`: una passata senza allocazioni se i "<" sono 64 o meno. La pulizia gira in `Task.Run` (`UI/ReadingView.xaml.cs:717-722`). Le regex di `MailHtml` si costruiscono al primo uso, non all'avvio.
- **Loghi:** `IsKnown`/`IsAuthenticated`/`Note`: dizionario sotto `seenLock`, nessun I/O a ogni chiamata (solo il `Load` iniziale); `SaveSoonAsync` accorpa a 5 s e fa DPAPI e scrittura fuori dal lucchetto; `AuthenticateFromCopies` una volta sola, su pool. `Core/LogoSvg.cs`: solo campi statici su pool.
- **`SenderCheck`:** `Parse`, `Split`, `Words`, `Authenticated` lineari in una passata (`Core/SenderCheck.cs:49-241`); `Address.Display` e `MessageItem.Sender` calcolati una volta per istanza; `NameAddress` esce dopo una passata per nomi ASCII senza "@" né "."; `LookAlike` esce presto per nomi corti, propri indirizzi, chi ha già `Sent > 0` (`:437-453`).
- **`Safety.CleanTemp`** (`Core/Safety.cs:153-173`): senza cartella è un `Directory.Exists` al minuto. `CleanAttachmentName`: percorso veloce senza allocazioni per nomi ASCII.
- **`SummaryCache` come struttura** (O(1), un lucchetto per cache), **`MailboxGate`** (nessun deadlock: l'apertura prende solo `all`, le altre `others` poi `all`), **`SettingsSaver`** (numerazione, ripetizione se l'elenco cambia, `Flush` nei quattro punti), `Log.Time` `[Conditional]`.
- **`UnreadSync` e `RecountSoon`:** una raffica dà una scrittura per cartella e un solo riconteggio (`recounting`, un secondo di quiete). Il caso delle scritture singole è in B-17.
- **`StallLog`:** vettore fisso di 32 voci, 10 righe al minuto; nessun gestore di `Rendering`.
- **Impostazioni:** `SettingsWindow.cs` costruisce solo la pagina scelta (`ShowPage`, `:109-126`); firme e modelli con 500 ms di attesa e `SaveSoon`. `ContactWindow`, `EventWindow` (suggerimenti indicizzati; disponibilità solo al clic), `PersonCard` (una sola ricerca per i due elenchi; vedi B-34).
- **`ActionQueue`:** `Add` un solo `Save` per chiamata (`:97-100`), `ReplayAsync` al massimo due salvataggi, non uno per azione.
- **`Pec`:** `OpenAsync` su pool (`:78`), `X509Chain` senza scaricamento né revoca, `Remember` con tetto 8 voci e 64 MB.
- **`HtmlToFlow`:** usa `ParseWithinLimits` (`:29`), ricorsione limitata dai 512 livelli; unico punto quadratico: `IsLast` (B-11).
- **`Idle`:** gestori `CountChanged`/`MessageFlagsChanged`/`MessageExpunged` tolti in `finally` (`:231-248`).
- **Gestori e sottoscrizioni:** `NetworkAddressChanged` e `UserPreferenceChanged` tolti in `OnExit`; `MessageRowElement.Refreshed` staccato quando la riga esce (`:77-95`); `ReadingView` stacca `App.ThemeChanged` alla chiusura della finestra ospite; `MainWindow` stacca `Logos.Arrived`, `ReadingView.ZoomChanged`, `Outbox.Changed`, `ActionQueue.Changed`; `TrayIcon.DestroyIcon` presente.
- **A riposo** nessuna sottoscrizione a `Rendering` resta attiva (B.0).
- **Notifiche:** i messaggi arrivati mentre l'account è non in linea si trovano al controllo con `since` = avvio e `notified` toglie i doppioni: in ritardo, non persi. Dopo A-15 il ritorno al normale passa comunque dal primo controllo riuscito e nessuna azione si perde.
- **Un ritorno di rete non apre mai la finestra di Microsoft da solo** a finestra nascosta (`MicrosoftAuth.Quiet`/`InBackgroundAsync`, `:63-76, 238-239`); controllo dei nuovi messaggi, delta, Posponi, copia dei 30 giorni e coda girano tutti in `InBackgroundAsync`.
- **Avvio:** `Store.Read` nel percorso normale è due `File.Exists` più una lettura; `SetAside` e `File.Copy` solo in caso di errore o una volta; deserializzatori generati; `WarmUp`, `Preload`, `EncryptOld`, `TrimAll`, `AuthenticateFromCopies` su `Task.Run` (salvo `CleanTemp`, A-05); `StallLog.Start`, `WheelScrolling.Enable`, `SetTextColors` trascurabili; `LayOutHidden`, `ProcessRenderMode` e `ReleaseWeb`/`OnProcessFailed` già misurati o corretti.
- **Collegamento anticipato senza rete:** `Preconnect` e `WarmUp` falliscono subito e la prima richiesta vera rifà tutto: nessun danno.

## Non guardato

- **Tutto ciò che dipende da Windows:** tempi reali di DPAPI, NTFS, antivirus (sui file aperti e chiusi del registro, sulle rinomine, sul `File.Replace`), Portmaster e VPN all'accesso, ICU e `string.Normalize`, costo delle eccezioni, caricamento di crypt32/X509 al primo certificato e della prima serializzazione a riflessione. Le cifre Linux di A-03, A-21, A-22, B-11 e B-24 danno l'ordine relativo e l'andamento, non il valore su Windows.
- **Codice interno di WPF, Fluent, WebView2, MSAL, MailKit, AngleSharp e del Toolkit delle notifiche:** se il setter di `Application.ThemeMode` ricarica le risorse a ogni assegnazione (A-02); che cosa carica esattamente `ThemeMode.System`; che cosa registra il Toolkit all'iscrizione a `OnActivated` (A-05); l'override `WebView2.OnWindowPositionChanged` (B-06); le animazioni interne dei modelli Fluent; che eccezione dia WAM/MSAL senza rete (A-19); l'impaginazione di un `FlowDocument` con tabelle annidate (B-12); la memoria di `MimeMessage.Load` su 30 MB (B-27).
- **Il sorgente di `HwndKeyboardInputProvider` e `HwndMouseInputProvider`** (404 sul sorgente pubblico): resta da confermare che tasti e clic veri entrino sincroni dal `WndProc` (B.0, B-04).
- **Il file unico:** non si è verificato aprendo l'intestazione R2R che `Missiva.r2r.dll` sia un'immagine composita; né quali pagine dei 155 MB si tocchino prima del primo disegno; né l'estrazione delle librerie native al primo avvio dopo un aggiornamento (solo con Process Monitor su `%TEMP%\.net`, A.7).
- **Contenuto e dimensione reali dei dati dell'autore:** copia dei dati, `loghi`, `notifiche`, `WebView2`, `cache`, `missiva.log` (quante righe all'ora).
- **Moduli letti solo per le chiamate o in parte:** `UI/MainWindow.xaml.cs` e `UI/ViewModels.cs` oltre ai punti citati, `Core/GraphProvider.cs` oltre a `early`, `gates`, `SendAsync` e il `MailboxGate`, `UI/ComposeWindow.xaml.cs` oltre al timer, all'invio e alla ripresa della bozza, `Core/MailAutoconfig.cs`, `Core/Backup.cs`, `Core/Invitation.cs`, `Core/P7m.cs`, `UI/AttachmentPreview.cs`, `UI/PeopleView.*`, `ReadingPage.Build`, `FlowToHtml`, `Core/LogoSvg.cs` oltre ai campi statici, `SenderLogos.FetchAsync`, `ContactBook` oltre al salvataggio, `OfflineCopy.RunAsync` dentro, il percorso della delta di Graph, `Store.TryWrite` e `StoreRecovery`.
- **Traffico a riposo:** quante letture e scritture di file e connessioni al minuto fa Missiva nell'area di notifica (qui contate solo le sveglie dichiarate nel codice): serve Process Monitor.
- **Strumenti sul PC dell'autore** (`loop\...`, `prova-dati`, ecc.): non esistono nel cloud.
- **L'unico banco del codice del repository con `HtmlToFlow` vera:** non eseguito (dipende da WPF); B-11 usa il ciclo equivalente su AngleSharp.
# Appendice: codice essenziale dei banchi (fuori dal repository)

Tutto è stato eseguito in `/tmp` (cartelle di lavoro di sessione), mai nel repository. Versioni: .NET SDK 10.0.112 e runtime 10.0.12 dai pacchetti Ubuntu (`apt-get install dotnet-sdk-10.0`), AngleSharp 1.8.2 (la stessa di `Missiva.csproj`).

## Pubblicazione e analisi del pacchetto (A-11)

```
dotnet publish -c Release -o OUT                                         # come installa.ps1: file unico, ReadyToRun, win-x64
dotnet publish -c Release -p:PublishSingleFile=false -o OUT-dir-r2r      # cartella, ReadyToRun
dotnet publish -c Release -p:PublishSingleFile=false -p:PublishReadyToRun=false -o OUT-dir-jit
dotnet publish -c Release -p:PublishReadyToRun=false -o OUT-single-jit
```

Per l'elenco del file unico: lettura dell'intestazione del bundle (versione 6). Il percorso dell'intestazione è negli 8 byte prima della firma di 32 byte del bundle; poi `versione maggiore`, `minore`, `numero di file` (int32), `id` (stringa con lunghezza a 7 bit), per la versione 2 o successiva 5 numeri a 8 byte (offset e misura di `deps.json` e `runtimeconfig`, flag), poi per ogni file `offset` (int64), `misura` (int64), `misura compressa` (int64, dalla versione 6), `tipo` (byte: 1 assembly, 2 nativo, 3 deps, 4 runtimeconfig, 5 simboli) e il nome.

```python
import struct, mmap
sig = bytes([0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
             0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae])
m = mmap.mmap(open('Missiva.exe','rb').fileno(), 0, access=mmap.ACCESS_READ)
off = struct.unpack('<q', m[m.find(sig)-8:m.find(sig)])[0]; p = off
maj, mnr, n = struct.unpack('<IIi', m[p:p+12]); p += 12
def rs():
    global p; l = sh = 0
    while True:
        b = m[p]; p += 1; l |= (b & 0x7f) << sh; sh += 7
        if not b & 0x80: break
    s = m[p:p+l].decode(); p += l; return s
rs()
if maj >= 2: p += 8*4 + 8
for _ in range(n):
    o, sz = struct.unpack('<qq', m[p:p+16]); p += 16
    if maj >= 6: p += 8
    t = m[p]; p += 1; name = rs()
    print(name, t, sz)
```

## Pulizia dell'HTML e `SenderCheck` (B-24)

Progetto `net10.0` console con AngleSharp. Le due copie di `Core/MailHtml.cs` (`git show HEAD:Core/MailHtml.cs` e `git show 3ae3675:Core/MailHtml.cs`) con `namespace Missiva;` cambiato in `namespace Missiva.New;` e `namespace Missiva.Old;`. Le funzioni di nomi sono estratte dalle righe di `Core/SenderCheck.cs` fra il commento di `CleanName` e quello di `SameOrganization` (`CleanName`, `NameAddress`, `CommonEndings`, `Skeleton`, `Unseen`, `Latin`) dentro una classe `SenderCheck`, con tre stub: `Address { Name, Email }`, `Settings.ImageKey` e `AccountTypes.DomainOf` (le stesse due funzioni di `Core/Store.cs:161-172` e `Core/AccountTypes.cs:18-23`, con due insiemi minimi di domini). Guscio dei tipi di WPF:

```csharp
namespace System.Windows.Media;
public readonly struct Color
{
    public byte A { get; } public byte R { get; } public byte G { get; } public byte B { get; }
    Color(byte a, byte r, byte g, byte b) { A=a; R=r; G=g; B=b; }
    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
}
public static class Colors { /* White, Black, Red, Blue, Green, Gray, Silver, Navy, Transparent */ }
public static class ColorConverter
{
    public static object ConvertFromString(string s)   // #rgb, #rrggbb, #aarrggbb, nomi di Colors; il resto: FormatException come WPF
    { /* ... */ }
}
```

Programma di misura (generatore di email e ciclo di misura):

```csharp
using System.Diagnostics;
using System.Text;

// Generatore di email finte "normali" (newsletter con tabelle, stili in linea, blocco <style>, immagini remote, link).
static string Newsletter(int blocks, int seed, bool darkOwn = false)
{
    var r = new Random(seed);
    var sb = new StringBuilder();
    sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Offerte</title><style>");
    sb.Append("body{margin:0;padding:0;background:#f4f4f4;font-family:Arial,Helvetica,sans-serif}");
    for (int i = 0; i < 30; i++) sb.Append($".c{i}{{color:#{r.Next(0x1000000):x6};background-color:#{r.Next(0x1000000):x6};padding:{r.Next(4, 20)}px;font-size:{r.Next(10, 20)}px}}");
    sb.Append("@media (max-width:600px){.w{width:100%!important}} a{color:#0066cc;text-decoration:none}");
    if (darkOwn) sb.Append("@media (prefers-color-scheme: dark){body{background:#111}}");
    sb.Append("</style></head><body><table width=\"100%\" bgcolor=\"#f4f4f4\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\">");
    sb.Append("<table class=\"w\" width=\"600\" bgcolor=\"#ffffff\" cellpadding=\"0\" cellspacing=\"0\" style=\"border:1px solid #dddddd\">");
    for (int b = 0; b < blocks; b++)
    {
        sb.Append($"<tr><td class=\"c{b % 30}\" style=\"background-color:#{(b % 3 == 0 ? "ffffff" : r.Next(0x1000000).ToString("x6"))};padding:12px 16px;color:#222222\">");
        sb.Append($"<table width=\"100%\"><tr><td><h2 style=\"margin:0;color:#{r.Next(0x1000000):x6};font-size:18px\">Titolo offerta numero {b} per Mario Rossi</h2></td></tr>");
        sb.Append($"<tr><td style=\"font-size:14px;line-height:20px\">Testo di prova numero {b}. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua &amp; &egrave; ancora. ");
        sb.Append($"<a href=\"https://negozio.example/offerta/{b}?utm_source=mail&amp;utm_campaign=x{b}\" style=\"color:#0066cc;text-decoration:underline\">Scopri di più</a></td></tr>");
        sb.Append($"<tr><td><img src=\"https://img.negozio.example/p/{b}.jpg\" width=\"560\" height=\"200\" alt=\"Prodotto {b}\" style=\"display:block;border:0\"></td></tr>");
        if (b % 5 == 0) sb.Append($"<tr><td><img src=\"https://track.negozio.example/o.gif?i={b}\" width=\"1\" height=\"1\" alt=\"\"></td></tr>");
        if (b % 7 == 0) sb.Append($"<tr><td><a href=\"https://negozio.example/acquista/{b}\" style=\"display:inline-block;background-color:#d9232d;color:#ffffff;padding:10px 20px;border-radius:4px\">Acquista ora</a></td></tr>");
        sb.Append("</table></td></tr>");
    }
    sb.Append("<tr><td style=\"font-size:11px;color:#888888\" align=\"center\">Hai ricevuto questa mail perché sei iscritto. <a href=\"https://negozio.example/disiscrivi\">Annulla iscrizione</a></td></tr>");
    sb.Append("</table></td></tr></table></body></html>");
    return sb.ToString();
}
static string Simple(int paras, int seed)
{
    var sb = new StringBuilder("<html><body><div style=\"font-family:Calibri;font-size:11pt\">");
    for (int i = 0; i < paras; i++) sb.Append($"<p style=\"margin:0 0 8px\">Ciao Maria, ti scrivo per la pratica {i}: <b>grazie</b> del documento, lo guardo &amp; ti rispondo. <a href=\"mailto:mario.rossi@prova.example\">Mario Rossi</a></p>");
    sb.Append("<blockquote style=\"border-left:2px solid #ccc;padding-left:8px;color:#555\">");
    for (int i = 0; i < paras; i++) sb.Append($"<div>Il giorno scorso Maria Rossi ha scritto: riga citata {i}</div>");
    sb.Append("</blockquote></div></body></html>");
    return sb.ToString();
}

var inputs = new (string name, string html)[]
{
    ("semplice ~5 KB", Simple(25, 1)),
    ("newsletter ~30 KB", Newsletter(30, 2)),
    ("newsletter ~100 KB", Newsletter(100, 3)),
    ("newsletter ~200 KB", Newsletter(200, 4)),
    ("newsletter ~1 MB", Newsletter(1000, 5)),
};
foreach (var (n, h) in inputs) Console.WriteLine($"# {n}: {h.Length / 1024.0:F0} KB di HTML");

const int Rounds = 30;
Console.WriteLine($"# giri per misura: {Rounds}; .NET {Environment.Version}; {Environment.ProcessorCount} CPU; Server GC {System.Runtime.GCSettings.IsServerGC}");

(double med, double p95, long alloc) Run(Action f)
{
    for (int i = 0; i < 5; i++) f(); // riscaldamento (JIT a livelli)
    var t = new double[Rounds]; long a = 0;
    for (int i = 0; i < Rounds; i++)
    {
        GC.Collect(); 
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew(); f(); sw.Stop();
        a += GC.GetAllocatedBytesForCurrentThread() - b0;
        t[i] = sw.Elapsed.TotalMilliseconds;
    }
    Array.Sort(t);
    return (t[Rounds / 2], t[(int)Math.Ceiling(Rounds * 0.95) - 1], a / Rounds);
}
Func<string, string?> cid = _ => null;
Console.WriteLine("| funzione | input | vecchio med / p95 ms | nuovo med / p95 ms | alloc vecchio KB | alloc nuovo KB |");
Console.WriteLine("|---|---|---|---|---|---|");
foreach (var (n, h) in inputs)
{
    var cases = new (string f, Action o, Action nw)[]
    {
        ("Prepare chiaro, immagini bloccate", () => Missiva.Old.MailHtml.Prepare(h, false, false, cid), () => Missiva.New.MailHtml.Prepare(h, false, false, cid)),
        ("Prepare scuro, immagini bloccate", () => Missiva.Old.MailHtml.Prepare(h, true, false, cid), () => Missiva.New.MailHtml.Prepare(h, true, false, cid)),
        ("Prepare chiaro, immagini consentite", () => Missiva.Old.MailHtml.Prepare(h, false, true, cid), () => Missiva.New.MailHtml.Prepare(h, false, true, cid)),
        ("ReadableText", () => Missiva.Old.MailHtml.ReadableText(h), () => Missiva.New.MailHtml.ReadableText(h)),
    };
    foreach (var c in cases)
    {
        var o = Run(c.o); var nw = Run(c.nw);
        Console.WriteLine($"| {c.f} | {n} | {o.med:F2} / {o.p95:F2} | {nw.med:F2} / {nw.p95:F2} | {o.alloc / 1024} | {nw.alloc / 1024} |");
    }
}
// anteprime: testo semplice con link, 600 caratteri come arrivano dai provider
var prev = string.Concat(Enumerable.Repeat("Ciao Mario Rossi &egrave; ora [Leggi da browser](https://negozio.example/x?a=1&amp;b=2) <https://negozio.example/y> grazie ", 6));
{
    var o = Run(() => Missiva.Old.MailHtml.PlainPreview(prev)); var nw = Run(() => Missiva.New.MailHtml.PlainPreview(prev));
    Console.WriteLine($"| PlainPreview | 600 car. | {o.med*1000:F1} / {o.p95*1000:F1} us | {nw.med*1000:F1} / {nw.p95*1000:F1} us | {o.alloc / 1024.0:F1} | {nw.alloc / 1024.0:F1} |");
}

// ---- SenderCheck: costo per indirizzo (solo codice nuovo: nel vecchio queste funzioni non c'erano) ----
{
    var names = new[] { "Mario Rossi", "Maria Rossi", "Negozio.example", "Banca Prova S.p.A.", "Dr.Rossi", "Newsletter di Negozio Prova", "José Núñez", "Ufficio Acquisti - Studio Verdi & Associati", "Assistenza clienti", "Mario Rossi (via Negozio)", "assistenza@banca.example", "Ｍａｒｉｏ Ｒｏｓｓｉ", "Marіa Rossi", "Servizio Clienti www.negozio.example", "Rossi, Mario", "Poste Prova", "Il Team di Prova ​", "Luca Bianchi | Studio Prova" };
    var mails = new[] { "mario.rossi@prova.example", "news@mail.negozio.example", "x@gmail.example", "ticket+123@posta.negozio.example" };
    var addrs = new List<Missiva.New.Address>();
    for (int i = 0; i < 1000; i++) addrs.Add(new Missiva.New.Address { Name = names[i % names.Length], Email = mails[i % mails.Length] });
    (double med, double p95, long alloc) R2(Action f) => Run(f);
    var a = R2(() => { foreach (var x in addrs) Missiva.New.SenderCheck.CleanName(x.Name); });
    var b = R2(() => { foreach (var x in addrs) Missiva.New.SenderCheck.NameAddress(x); });
    var c = R2(() => { foreach (var x in addrs) Missiva.New.SenderCheck.Skeleton(x.Name); });
    Console.WriteLine();
    Console.WriteLine("| funzione (1000 indirizzi) | mediana ms | p95 ms | alloc KB | per indirizzo (us) |");
    Console.WriteLine("|---|---|---|---|---|");
    Console.WriteLine($"| CleanName | {a.med:F3} | {a.p95:F3} | {a.alloc / 1024} | {a.med:F3} |");
    Console.WriteLine($"| NameAddress (Address.Display) | {b.med:F3} | {b.p95:F3} | {b.alloc / 1024} | {b.med:F3} |");
    Console.WriteLine($"| Skeleton (LookAlike, per nome) | {c.med:F3} | {c.p95:F3} | {c.alloc / 1024} | {c.med:F3} |");
    // caso peggiore: un nome pulito ASCII lungo e uno con molti punti
    var long1 = string.Join(" ", Enumerable.Repeat("Mario.Rossi.Dr.Prof", 12));
    var d = Run(() => { for (int i = 0; i < 1000; i++) Missiva.New.SenderCheck.NameAddress(new Missiva.New.Address { Name = long1, Email = "a@b.example" }); });
    Console.WriteLine($"| NameAddress nome lungo 228 car. con molti punti | {d.med:F3} | {d.p95:F3} | {d.alloc / 1024} | {d.med:F3} |");
}
```

## Deserializzazione JSON con i tipi veri (A-03)

Programma `net10.0` che carica `Missiva.dll` dalla cartella di pubblicazione senza ReadyToRun, riempie `Settings` e `List<MessageSummary>` per riflessione con testi finti e li passa a `SettingsJson` e `CacheJson` (contesti generati, interni, raggiunti con la riflessione). `ProtectedTextConverter.Portable` è acceso con la riflessione perché su Linux non c'è DPAPI (nessuna decifratura nella misura).

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

var dir = args[0]; // cartella di pubblicazione con Missiva.dll
AssemblyLoadContext.Default.Resolving += (c, n) => { var p = Path.Combine(dir, n.Name + ".dll"); return File.Exists(p) ? c.LoadFromAssemblyPath(p) : null; };
var asm = Assembly.LoadFrom(Path.Combine(dir, "Missiva.dll"));
Type T(string n) => asm.GetType("Missiva." + n) ?? throw new Exception(n);

object Make(Type t, int depth, int lists)
{
    if (t == typeof(string)) return "Testo di prova per il campo, lunghezza media";
    if (Nullable.GetUnderlyingType(t) != null) return null;
    if (t == typeof(DateTime)) return new DateTime(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc);
    if (t.IsValueType) return Activator.CreateInstance(t);
    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
    {
        var l = (System.Collections.IList)Activator.CreateInstance(t);
        var et = t.GetGenericArguments()[0];
        if (depth < 3) for (int i = 0; i < lists; i++) l.Add(Make(et, depth + 1, 1));
        return l;
    }
    if (t.IsGenericType) return Activator.CreateInstance(t);
    if (depth > 4) return null;
    object o;
    try { o = Activator.CreateInstance(t, true); } catch { o = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t); }
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!p.CanWrite || p.SetMethod is null) continue;
        try { p.SetValue(o, Make(p.PropertyType, depth + 1, lists == 1 ? 1 : 1)); } catch { }
    }
    return o;
}
var pt=asm.GetType("Missiva.ProtectedTextConverter"); pt.GetField("Portable",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,true);
double Med(double[] a) { Array.Sort(a); return a[a.Length / 2]; }

// ---- impostazioni ----
var ST = T("Settings"); var ctxT = T("SettingsJson");
var ctx = (JsonSerializerContext)ctxT.GetProperty("Default", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
var s = Activator.CreateInstance(ST);
void SetList(string prop, int n)
{
    var p = ST.GetProperty(prop); var l = (System.Collections.IList)p.GetValue(s);
    var et = p.PropertyType.GetGenericArguments()[0];
    for (int i = 0; i < n; i++) l.Add(et == typeof(string) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(i))) : Make(et, 1, 1));
}
SetList("Accounts", 3); SetList("RulesSeen", 2000); SetList("Flags", 200); SetList("Snoozed", 30); 
var sigs = (System.Collections.IDictionary)ST.GetProperty("Signatures").GetValue(s);
var sigT = T("Signature");
for (int i = 0; i < 3; i++) { var sig = Make(sigT, 1, 1); foreach (var pn in new[] { "Rtf", "StoredRtf" }) { var pp = sigT.GetProperty(pn); if (pp?.CanWrite == true) try { pp.SetValue(sig, new string('x', 150_000)); } catch { } } sigs["acc" + i] = sig; }
var json = JsonSerializer.Serialize(s, ST, ctx);
Console.WriteLine($"impostazioni.json finto: {json.Length / 1024.0:F0} KB (3 account, 2000 RulesSeen, 200 contrassegni, 30 posposti, 15 regole, 10 modelli, 3 firme con RTF finto di 150 KB)");
foreach (var (label, js) in new[] { ("impostazioni 13 KB circa (dell'autore oggi)", JsonSerializer.Serialize(Activator.CreateInstance(ST), ST, ctx)), ("impostazioni cariche", json) })
{
    // primo uso a freddo = in un processo nuovo: qui solo prima chiamata contro regime
    var sw = Stopwatch.StartNew(); JsonSerializer.Deserialize(js, ST, ctx); var first = sw.Elapsed.TotalMilliseconds;
    var t = new double[30]; for (int i = 0; i < 30; i++) { sw.Restart(); JsonSerializer.Deserialize(js, ST, ctx); t[i] = sw.Elapsed.TotalMilliseconds; }
    Console.WriteLine($"{label}: {js.Length / 1024.0:F0} KB, prima {first:F1} ms, regime mediana {Med(t):F2} ms");
}

// ---- pagina della copia locale: 50 messaggi ----
var MS = T("MessageSummary"); var cache = (JsonSerializerContext)T("CacheJson").GetProperty("Default", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
var listT = typeof(List<>).MakeGenericType(MS);
foreach (var n in new[] { 50, 500 })
{
    var list = (System.Collections.IList)Activator.CreateInstance(listT);
    for (int i = 0; i < n; i++)
    {
        var m = Make(MS, 1, 1);
        MS.GetProperty("Preview").SetValue(m, new string('a', 200));
        MS.GetProperty("Subject").SetValue(m, "Oggetto del messaggio numero " + i);
        list.Add(m);
    }
    var js = JsonSerializer.Serialize(list, listT, cache);
    var sw = Stopwatch.StartNew(); JsonSerializer.Deserialize(js, listT, cache); var first = sw.Elapsed.TotalMilliseconds;
    var t = new double[30]; for (int i = 0; i < 30; i++) { sw.Restart(); JsonSerializer.Deserialize(js, listT, cache); t[i] = sw.Elapsed.TotalMilliseconds; }
    Console.WriteLine($"copia locale {n} messaggi: {js.Length / 1024.0:F0} KB, prima {first:F1} ms, regime mediana {Med(t):F2} ms");
}
```

## Andamento di `IsLast` (B-11)

```csharp
using System.Diagnostics; using AngleSharp.Html.Parser; using AngleSharp.Dom;
foreach (var n in new[]{5000,10000,20000,40000}) {
  var html = "<html><body>" + string.Concat(Enumerable.Range(0,n).Select(i=>"line "+i+"<br>")) + "</body></html>";
  var dom = new HtmlParser().ParseDocument(html);
  var sw = Stopwatch.StartNew(); int c=0;
  foreach (var br in dom.Body!.Children.Where(e=>e.LocalName=="br"))
  { for (var next = br.NextSibling; next != null; next = next.NextSibling) { if (next is IElement || next is IText t && t.Data.Trim().Length>0) break; } c++; }
  Console.WriteLine($"{n} br: NextSibling loop {sw.ElapsedMilliseconds} ms ({c})");
  sw.Restart(); int d=0; foreach (var node in dom.Body.ChildNodes) d++;
  Console.WriteLine($"   plain ChildNodes enumeration {sw.ElapsedMilliseconds} ms");
}
```
