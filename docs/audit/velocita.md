# Audit di velocita di Missiva

Commit di main letto: `59ed8bafa7b59942f04bef8c084bbb5d3455bca9`.

## Metodo e limiti

Lavoro di sola lettura. Su Linux Missiva non si esegue e `--prova-bozze` non gira. La Parte A e misurata davvero fuori
dal repository; la Parte B e letta nel codice. Ogni punto dichiara lo stato: MISURATO (con i numeri), CONFERMATO (letto
nel codice, con file e riga) o SOSPETTO (e cosa manca). I numeri senza "MISURATO" sono stime, con il ragionamento.

Le misure della Parte A vengono da questo contenitore: **Intel Xeon @ 2,10 GHz, 4 core logici (KVM), .NET SDK 10.0.112,
runtime 10.0.12, GC workstation (non server)**. Non si confrontano con il PC dell'autore (Windows 11, 120 Hz): servono per
l'andamento al crescere della dimensione e per i confronti relativi. Il banco compila copie dei file senza dipendenze da
Windows (`MailHtml` con AngleSharp, `Invitation`, `SearchTerms`, `Mailto`, `ContactLinks`, `SenderCheck` con MimeKit, la
parte vCard/CSV di `Contacts`), con finti al posto di `Store` e `Log`. Il codice essenziale del banco e in fondo
("Come rifare le misure su Windows").

Gia deciso o provato, non riproposto: DirectManipulation; la riga disegnata da un solo elemento (`MessageRowElement`);
`CacheLength=0`; `ScrollHitGuard`; il modello 3 del touchpad; file unico senza compressione con ReadyToRun; il servizio
di rete di WebView2 in un processo suo; il controllo ogni due minuti; l'alone delle schede; il riscaldamento dei
caratteri, BackForwardCache, GCConserveMemory, GC non concorrente, TieredPGO. Niente riscritture in un altro framework.

---

## Parte A: misure (MISURATO)

### 1. `MailHtml.Prepare` (immagini NON consentite), email generate

| Ingresso | Byte | Chiaro mediana / p95 | Scuro mediana / p95 | Alloc/giro (chiaro / scuro) |
|---|---|---|---|---|
| newsletter 50 KB | 51.512 | 20,2 / 40,1 ms | 25,3 / 34,8 ms | 3,8 / 5,8 MB |
| newsletter 200 KB | 205.261 | 38,9 / 73,8 ms | 40,7 / 88,2 ms | 14,6 / 21,8 MB |
| newsletter 1 MB | 1.049.250 | 80,0 / 250,9 ms | 206,4 / 275,3 ms | 71,4 / 110,4 MB |
| newsletter 5 MB | 5.243.439 | 802,3 / 885,4 ms | 1.119,6 / 1.240,1 ms | 351,4 / 545,1 MB |
| tabelle annidate 10 / 50 / 200 | 1,3 / 6,3 / 25 KB | - | 0,5 / 2,0 / 9,4 ms | 0,2 / 1,0 / 3,7 MB |
| 5000 elementi con `style` | 433.916 | - | 90,1 / 200,7 ms | 48,0 MB |
| `<style>` con 2000 regole | 247.842 | - | 28,2 / 111,1 ms | 21,0 MB |
| 200 `data:` URI da 8 KB | 2.199.826 | - | 36,3 / 45,9 ms | 51,6 MB |

Andamento: con le email reali `Prepare` e circa lineare nella dimensione. Il tema scuro costa circa 1,4 volte il chiaro e
alloca di piu (l'adattamento dei colori). Le allocazioni sono forti: 5 MB scuro = 545 MB allocati per apertura, cioe
molte raccolte del GC. Questo pesa su `ReadingPage.Warm` (in sottofondo) e, a ogni "Mostra immagini"/cambio tema, sul
thread dell'interfaccia (vedi V-14).

### 1b. Casi patologici (scuro, immagini NON consentite)

| Ingresso | Byte | Tempo | Alloc/giro |
|---|---|---|---|
| 100.000 `<` di fila | 100.026 | ~2.775 ms | **9.550 MB** |
| attributo `style` da 640 KB | 660.047 | ~196 ms | 85,8 MB |
| un attributo da 1 MB | 1.000.048 | ~20 ms | 19,3 MB |
| "tag mai chiusi" (20.000 `<div>` annidati) | 540.030 | ~7.258 ms | 142 MB |
| `<style>` da 1 MB senza `;` | 1.000.065 | ~41 ms | 33,2 MB |
| `url(` aperto mai chiuso in `style` | 500.062 | ~42 ms | 21,1 MB |
| 1 MB di backslash in `style` | ~1 MB | **non termina (> 60 s)** | - |

Due reperti gravi (anche di sicurezza, vedi `sicurezza.md` S-29 e S-30):

- **Backslash nel CSS: costo quasi cubico.** `CssEscapedFunction` (`Core/MailHtml.cs:389`) gira quando il CSS contiene un
  `\`. Misurata isolata su soli backslash: 250 -> 67 ms, 500 -> 473 ms, 1000 -> 2.529 ms, 2000 -> 18.993 ms (ogni
  raddoppio x7-8: ~n^2,9). Un'email con decine di migliaia di backslash nel CSS blocca il thread che pulisce l'HTML per
  minuti.
- **Annidamento profondo: O(n^2) nel ramo scuro.** div annidati scuro 2.000 / 4.000 / 8.000 = ~0,2 / 0,12 / 0,37 s,
  chiaro 0,10 / 0,05 / 0,15 s; "tag mai chiusi" 20.000 = ~7,3 s. Il ramo scuro risale tutta la catena di antenati per
  ogni elemento (`Core/MailHtml.cs:71`): O(profondita) per elemento. I 100.000 `<` allocano 9,5 GB in AngleSharp.

### 2. `PlainPreview`, `ToText`, `ReadableText`

| Funzione | 50 KB | 200 KB | 1 MB |
|---|---|---|---|
| `ToText` | 6,5 ms | 42,7 ms | 61,0 ms |
| `ReadableText` | 10,8 ms | 22,5 ms | 47,0 ms |

`PlainPreview` su ~3,5 KB: 0,05 ms. Andamento lineare, nessun problema (ma queste girano sul thread dell'interfaccia dopo
l'await dei provider, vedi V-13 e V-19).

### 3. Invitation, vCard, CSV, SearchTerms

| Funzione | Ingresso | Mediana / p95 | Alloc |
|---|---|---|---|
| `Invitation.Parse` | .ics 256 KB | 6,1 / 13,7 ms | 5,3 MB |
| `Invitation.Parse` | .ics piegato in migliaia di righe, 256 KB | 3,1 / 6,2 ms | 3,5 MB |
| `ContactFiles.VCard` | 5000 contatti | 64,7 / 94,4 ms | 27,5 MB |
| `ContactFiles.VCard` | 5 MB quoted-printable | 23,9 / 79,1 ms | 25,5 MB |
| `ContactFiles.Csv` | 5000 contatti | 13,0 / 24,3 ms | 6,1 MB |
| `SearchTerms.Parse` | query con 8 filtri | 0,003 ms | ~0 |
| `SearchTerms.Matches` | un messaggio | ~0 ms | ~0 |

Tutti lineari e nei limiti: le correzioni gia fatte (il `ParseLine` a passata singola, `Unfold` del quoted-printable in
una passata) tengono. `Invitation` con il limite `MaxLength` a 256 KB esatti esce subito; sopra non si legge.

### 4. Espressioni regolari con ingressi scelti per metterle in difficolta

| Espressione (file) | Ingresso | Tempo |
|---|---|---|
| `CssEscapedFunction` (MailHtml:389) | 250 / 500 / 1000 / 2000 backslash | 67 / 473 / 2.529 / 18.993 ms |
| `ColorToken` (MailHtml:426) | 50.000 lettere | 0,16 ms |
| `ColorDeclaration` (MailHtml:423) | 5000 `color:red;` | 2,1 ms |
| `CssImageSet` (MailHtml:382) | `image-set(` aperto 50 KB / 20.000 `a(b)` | 1,8 / 1,1 ms |
| `UrlInText` (MailHtml:366) | 10.000 url | 3,3 ms |
| `WebAddress` (MailHtml:268) | 1 url da 500 KB | 1,4 ms |
| `CutEntity` (MailHtml:248) | 50.000 `&` | 5,9 ms |
| `RootSelectors` (MailHtml:370) | 20.000 `body ` | 8,6 ms |
| link https di `Invitation` (:332,492) | 1 url da 500 KB | 1,9 ms |
| `SenderCheck.Parse` | 50.000 campi | 36,1 ms |

**Solo `CssEscapedFunction` ha un costo piu che lineare** (catastrofico). Tutte le altre sono lineari e sotto i 10 ms
anche su ingressi costruiti.

### Facoltativo: `dotnet build` del progetto reale

`dotnet build -c Release Missiva.csproj` **riesce** su Linux (grazie a `EnableWindowsTargeting`): 16 avvisi, 0 errori,
~40 s. Non prova nulla sul comportamento, ma conferma che il codice compila.

---

## Parte B: lettura (ogni punto con file e riga)

### V-01 La cache delle strutture di Gmail/IMAP (`known`) si svuota tutta oltre 1000 voci

- Dove: `Core/GmailProvider.cs:639` e `Core/ImapProvider.cs:890`, `Summary`.

  ```
  if (known.Count > 1000) known.Clear();
  known[folderId + "|" + i.UniqueId.Id] = i;
  ```

  Usata da `OnDisk`/`SavedBody` (`GmailProvider.cs:761`), `PrefetchAsync` (`:834`), `ReadBodyAsync` (`:869`).
- Scenario: la cache cresce con ogni pagina di elenco, ogni conversazione (fino a 50 voci), ogni ricerca, i nuovi arrivi
  e soprattutto la copia dei 30 giorni (`GetRecentAsync`, fino a 500 voci in un colpo). Alla voce 1001 sparisce **tutto**,
  anche le strutture dell'elenco aperto. Da li un clic su un messaggio gia copiato su disco non passa piu dalla copia
  (`OnDisk` falso) e va al server con una FETCH in piu; i vicini non si scaricano piu in anticipo; durante la copia dei 30
  giorni, i messaggi persi vengono saltati in silenzio.
- Gravita: Media (Alta per chi apre molte conversazioni). Stima: dopo lo svuotamento ogni clic su un messaggio gia sul PC
  costa come un clic a freddo. CLAUDE.md misura 290-440 ms contro 40-70 ms. Si ripete ogni 1000 voci distinte.
- Stato: CONFERMATO.
- Proposta: togliere lo svuotamento totale; eliminare le voci piu vecchie con una coda (come `bodyOrder`), oppure due
  generazioni (`current`/`previous`, oltre 1000 `previous = current` e `current` riparte; la lettura guarda entrambe).
  Costo di memoria: 1000-2000 `IMessageSummary`, pochi MB.
- Verifica su Windows: una riga di registro allo svuotamento; aprire ~20 conversazioni poi cliccare righe gia sul PC,
  confrontare "corpo dalla copia su disco" con "corpo (intero...)" nei tempi.

### V-02 Ogni testo della riga si formatta due volte, alcuni tre

- Dove: `UI/MessageRowElement.cs`, `Compute` (393-520) e `OnRender`. Le letture di `.Height`/`.Width`/`.DesiredSize`
  (es. `:402,417,453`) formattano il testo; `Fit(...)` (`:419,447,457,519`) imposta `MaxTextWidth`/`Trimming`, che
  invalida le metriche; il disegno riformatta. L'anteprima vuota formatta un "A" in piu (`:520`).
- Scenario: ogni riga nuova o riciclata durante lo scorrimento. Con mittente, data, oggetto e anteprima si arriva a ~10
  formattazioni per riga; `where` e ogni pastiglia di categoria ne fanno 3 (Width, Fit, Height).
- Gravita: Media. Stima: la riga nuova costa 1,6-2,3 ms (CLAUDE.md); nel profilo del modello vecchio `FormatLine` era il
  22%, e nella riga disegnata gli altri costi sono spariti, quindi la quota e piu alta. Togliere una formattazione ai
  testi lunghi dovrebbe dare 0,3-0,7 ms a riga e meno allocazioni. Cresce con le righe per fotogramma.
- Stato: CONFERMATO.
- Proposta: calcolare l'altezza di una riga di testo una volta per (carattere, dimensione, DPI), come gia fa `:520`, cosi
  i testi lunghi si formattano una volta nel disegno; leggere `Height` prima di `Fit` per `where`/categorie; per i testi
  di cui serve solo la larghezza, disegnarli in un `DrawingGroup` (il draw calcola le metriche) e in `OnRender`
  `DrawDrawing`.
- Verifica su Windows: "costo righe" e "scorri 60/120/240" prima e dopo; `dotnet-trace` sulla quota di `FormatLine`;
  `vetrina-C.ps1` + `confronta.ps1` e "altezze" per controllare il disegno identico.

### V-03 Tema scuro: una `FormatException` per ogni parola CSS che non e un colore

- Dove: `Core/MailHtml.cs:480-499` (`ParseColor`: `ColorConverter.ConvertFromString` dentro try/catch), chiamata da
  `IsVivid` (`:458`) e `Recolor` (`:472`) per ogni parola di `ColorToken` (`:426`, che finisce con `[a-zA-Z]+`).
- Scenario: aprendo un messaggio col riquadro scuro, in `border:1px solid #ddd` le parole "px" e "solid", in
  `background:... no-repeat center top` le parole "no", "repeat", "center", "top", e poi "none", "inherit", "important"
  passano tutte da `ConvertFromString`, che per un nome sconosciuto lancia `FormatException` (comportamento di WPF). Ogni
  sfondo passa 2-3 volte dallo stesso controllo.
- Gravita: Media-Bassa (solo tema scuro). Stima: da centinaia a migliaia di eccezioni per newsletter, a ~5-20 us l'una
  (lancio + stack + messaggio dalle risorse): qualche ms o qualche decina di ms in piu nella pulizia. Nota: nel mio banco
  `ParseColor` usa un `ColorConverter` finto che lancia come WPF; il numero di eccezioni e reale, il costo della singola
  eccezione in WPF e SOSPETTO.
- Stato: CONFERMATO il percorso; SOSPETTO il costo della singola eccezione in WPF.
- Proposta: un dizionario statico (minuscole) dei nomi di colore CSS, letto una volta; le parole che non ci sono
  restituiscono null senza chiamare `ConvertFromString`.
- Verifica su Windows: `dotnet-counters` ("Exception Count") aprendo newsletter nel tema scuro; confrontare "HTML pulito
  in sottofondo" in chiaro e in scuro sullo stesso messaggio.

### V-04 Elimina/Archivia/Sposta passano dal riquadro vuoto

- Dove: `UI/MainWindow.xaml.cs:2847-2856` (`RemoveRows`: toglie le righe poi sceglie la successiva) e `:2601-2613`
  (`OnMessageSelected`); `UI/ReadingView.xaml.cs:477-494,541` (`Clear` naviga al segnaposto).
- Scenario: togliere una riga selezionata fa scattare `SelectionChanged` con 0 righe scelte prima che venga scelta la
  successiva: `Clear` naviga al segnaposto, e la "pagina di prima" che `ShowAsync` tiene fino al corpo diventa il
  segnaposto. Con piu righe selezionate, dopo le rimozioni intermedie ne resta una sola che viene aperta e subito chiusa.
  Con "segna letto al cambio selezione", si segna letto il messaggio che si sta eliminando.
- Gravita: Media. Stima: una navigazione in piu per ogni Canc; il segnaposto resta visibile finche arriva il corpo
  successivo (decine o centinaia di ms se non in memoria).
- Stato: CONFERMATO il percorso; il lampo visibile e SOSPETTO (non misurabile da qui).
- Proposta: in `RemoveRows` calcolare e selezionare la riga successiva **prima** di togliere le altre, cosi scatta un solo
  `SelectionChanged` verso il messaggio dopo.
- Verifica su Windows: `lampi.ps1 -Steps "clic 3;delete"` con `MISSIVA_NO_READ`, cercando i fotogrammi del segnaposto.

### V-05 Invio ripete la ricerca che i suggerimenti hanno appena fatto (Gmail e Graph)

- Dove: `UI/MainWindow.Suggestions.cs:215-227` (`suggestCache` usata solo dai suggerimenti), `UI/MainWindow.xaml.cs:4180`
  (`SearchAsync` chiude i suggerimenti e rilancia), `Core/GmailProvider.cs:494-502` (con `next == null` la SEARCH si rifa
  sempre).
- Scenario: si scrive, si fa una pausa oltre 350 ms (i suggerimenti cercano sul server) e si preme Invio: la stessa
  ricerca gira di nuovo. Gmail la rifa sulla stessa connessione (se quella dei suggerimenti e in corso, la aspetta);
  Graph interrompe e rifa identica. Lo stesso scegliendo una riga del pannello.
- Gravita: Media. Stima: una ricerca intera in piu sul percorso di Invio (CLAUDE.md: "fattura" 0,42 s, pannello 1,41 s).
  IMAP in "tutte le cartelle" riusa gia (`Everywhere`); IMAP "cartella corrente" e SOSPETTO.
- Stato: CONFERMATO per Gmail e Graph.
- Proposta: tenere in `suggestCache` il `Task<MessagePage>` e, in `SearchAsync`, riusare la voce con la stessa chiave
  (account|cartella|testo dopo `TakeFilters`) se riuscita o in corso, senza annullarla; o riusare per 60 s gli uid in
  `GmailProvider.SearchAsync` anche con `next == null`, come fa IMAP.
- Verifica su Windows: "ricerca in tutte le cartelle di ..." e "attesa della connessione" prima e dopo.

### V-06 Persone: a ogni lettera si filtrano e si riordinano tutti i contatti, con confronti culturali

- Dove: `UI/PeopleView.cs:79-83` (`TextChanged` -> `Fill`, senza pausa), `UI/PeopleView.Sections.cs:95`
  (`.Where(Matches).OrderBy(Name, CurrentCultureIgnoreCase)`), `Core/Contacts.cs:67-68` (`Matches`: ~9 `Contains`
  culturali per contatto, Notes comprese).
- Scenario: con 4500 contatti, ogni lettera fa sul thread dell'interfaccia ~40.000 confronti culturali piu un ordinamento
  culturale di 4500 nomi, e ricrea tutte le `Row`. Con la casella vuota (apertura, dopo ogni account caricato) ordina
  tutti.
- Gravita: Media. Stima: 10-50 ms per lettera, lineare nei contatti (SOSPETTO il tempo esatto).
- Stato: CONFERMATO il lavoro; SOSPETTO il tempo.
- Proposta: pausa di 120-150 ms prima di `Fill`; per contatto un testo di ricerca gia normalizzato (minuscole, senza
  accenti) in cache con contatore di versione, ricerca `Ordinal`; elenchi per origine gia ordinati una volta al
  caricamento.
- Verifica su Windows: `--persone --contatti-finti --contatti-molti`, testo scritto via ValuePattern, `Log.Time` attorno a
  `Fill`, righe StallLog.

### V-07 Suggerimenti dei destinatari: tutta la rubrica scandita e riallocata a ogni lettera, sul thread UI

- Dove: `Core/AddressBook.cs:193-221` (`Suggest`: copia tutta la rubrica in un nuovo dizionario), `:271-279` (`Rank`:
  `ToLowerInvariant`, `Split` con tre vettori di separatori, `Concat`, `ToList` per voce). Chiamanti senza pausa:
  `UI/ComposeWindow.xaml.cs:934`, `UI/MainWindow.Suggestions.cs:112`, `UI/EventWindow.cs:400`.
- Scenario: per ogni lettera, copia della rubrica, una `Entry` nuova per ogni indirizzo dei contatti (`ForSuggestions`
  rifa la lista), ~20 allocazioni per voce.
- Gravita: Bassa-Media. Stima: con 10.000 voci, 10-15 MB e 5-20 ms per lettera, con raccolte gen0. Lineare. CLAUDE.md
  misura 42-90 ms per i suggerimenti "sullo schermo".
- Stato: CONFERMATO; SOSPETTO il tempo.
- Proposta: un indice in memoria fatto una volta e rifatto con un contatore di versione (parole in minuscolo e punteggio
  per voce); a ogni lettera solo i test di prefisso `Ordinal`; separatori statici.
- Verifica su Windows: "suggerimenti per ... sullo schermo" con una rubrica grande; `dotnet-counters` per le gen0.

### V-08 Chiusura dell'anteprima: due GC completi bloccanti dentro l'apertura del messaggio dopo

- Dove: `UI/AttachmentPreview.cs:167-174` (`Clear`: `GC.Collect(); WaitForPendingFinalizers(); GC.Collect();` a
  `ApplicationIdle`), chiamata da `ClosePreview` (`UI/ReadingView.xaml.cs:1371`), a sua volta prima cosa di `ShowAsync`
  (`:268`).
- Scenario: si guarda l'anteprima di un PDF/immagine, poi si apre un altro messaggio: mentre `ShowAsync` aspetta il corpo
  il Dispatcher e libero e partono le due raccolte; `Render`/`RevealAsync` le aspettano. Si vede quando il corpo arriva
  prima della fine del GC (corpo in memoria o copia su disco).
- Gravita: Media. Stima: 30-120 ms (una raccolta compattata costa 13-90 ms, CLAUDE.md; qui sono due, con Missiva a
  239-251 MB ad anteprima aperta). Cresce con la memoria gestita.
- Stato: CONFERMATO l'ordine; SOSPETTO la durata.
- Proposta: spostare la raccolta in un `DispatcherTimer` di 2-3 s riarmato da ogni `ShowAsync`, o saltarla se un'apertura
  e in corso; una sola `GC.Collect` + `WaitForPendingFinalizers`.
- Verifica su Windows: "anteprima chiusa, memoria liberata" e "clic, pagina mostrata" con e senza anteprima prima.

### V-09 "Segna come letto" sta sul percorso del clic e manca dalle misure della prova

- Dove: `UI/ReadingView.xaml.cs:318-319,755-767` (`MarkRead`/`SetRead` subito dopo `Render`), `UI/MainWindow.xaml.cs:1215-1237`
  (`UpdateBadge`: `RenderTargetBitmap` del numero, overlay sulla barra, suggerimento dell'icona via Explorer, una riga di
  registro). Con `MISSIVA_NO_READ=1` nulla di questo gira.
- Scenario: clic su un non letto con "Subito" (il valore di base): prima della pagina, sul thread dell'interfaccia, la
  riga cambia stato e si ridisegna, `Unread--` e `UpdateUnified`, una bitmap nuova, due chiamate verso Explorer, una riga
  di registro, l'avvio di `SetReadAsync`. Le misure della prova (con `MISSIVA_NO_READ`) valgono per un messaggio gia
  letto: c'e un buco di misura.
- Gravita: Media (soprattutto per il buco di misura). Stima: 1-10 ms per apertura, in parte nascosto dal tempo di
  Chromium.
- Stato: CONFERMATO percorso e salto in prova; SOSPETTO il costo.
- Proposta: in prova far girare la parte locale anche con `MISSIVA_NO_READ` (la scrittura la ferma gia `TestingProvider`);
  `UpdateBadge` accorpato (Background, un flag "gia in coda"); bitmap del numero in cache; riga "numero sull'icona" tolta
  dalla versione installata o solo al passaggio 0 <-> >0.
- Verifica su Windows: con la modifica 1, dieci clic su non letti, "clic, pagina mostrata" prima e dopo; `dotnet-trace`
  per `SetOverlayIcon`, `Shell_NotifyIconW`, `RenderTargetBitmap`.

### V-10 Avvio: una chiamata DPAPI per ogni campo cifrato delle impostazioni

- Dove: `Core/Store.cs:461-473` (`Load`, chiamato prima della finestra) e `:391-404` (`ProtectedTextConverter.Open`, una
  `Unprotect` per valore non vuoto).
- Scenario: a ogni avvio (anche `--nascosta`) una `CryptUnprotectData` per: `RecentSearches` + invii pianificati + modelli
  x3 + contrassegni x2 + posposti x2 + regole x(fino a 10). Con 20 contrassegni, 10 posposti, 5 modelli e 5 regole:
  80-125 chiamate.
- Gravita: Media (sul percorso dell'avvio, lineare nei dati). CLAUDE.md misura "lettura delle impostazioni 41-44 ms"; la
  quota DPAPI non e isolata (stima 50-200 us per chiamata piu la prima del processo).
- Stato: CONFERMATO il percorso; SOSPETTO quanto pesa sui 41-44 ms.
- Proposta: prima misurare; se pesa, decifrare al primo uso (posposti/contrassegni servono a notifiche e menu, i modelli
  al pannello, le regole al primo controllo), oppure un solo blob DPAPI per tutti i testi protetti (tenendo leggibili i
  vecchi `dpapi:`).
- Verifica su Windows: due `Log.Startup` in `Store.Load` e un contatore delle `Unprotect`; tre avvii con impostazioni
  piene e vuote.

### V-11 BodyCache: `Trim` dopo ogni scrittura, lock condiviso con il thread dell'interfaccia

- Dove: `Core/Offline.cs:179-187` (`Save`: `Trim(account)` dentro `lock (Gate)` a ogni corpo), `:304-317` (`Trim`:
  `GetFiles(... AllDirectories).OrderByDescending(...)`), `:344-357` (`Forget`, sincrono), `:429-451`
  (`OfflineCopy.RunAsync`: `Size` e fino a 500 `Has` sul thread dell'interfaccia). Chiamanti UI: `ReadingView.xaml.cs:666`,
  `MainWindow.xaml.cs:2296,2910,2990`.
- Scenario: ogni corpo salvato elenca e ordina **tutti** i `.bin` dell'account tenendo `Gate`. Con 200 MB e ~90 KB a
  corpo (CLAUDE.md) sono ~2400 file. Un clic (`Save`, riga 156 prende `Gate`) o un Canc (`Forget`) aspetta il Trim in
  corso; la copia dei 30 giorni fa N enumerazioni (quadratico). `OfflineCopy.RunAsync` gira sul thread dell'interfaccia
  (lo chiama un `DispatcherTimer`, `InBackgroundAsync` imposta solo l'AsyncLocal), con `Size` + 500 `Has`.
- Gravita: Media. Stima: Trim di 2400 file 3-7 ms; la fila di 10 salvataggi 30-150 ms; 400 corpi su 2400 file ~2 s di CPU.
- Stato: CONFERMATO; SOSPETTO le durate.
- Proposta: un indice per account in memoria (nome, byte, data), letto una volta e aggiornato da `Save`/`Forget`/`Delete`;
  `Has`/`Size` da li; `Trim` solo oltre il limite o una volta l'ora; `Save` senza `Gate` sul chiamante
  (`generations` concorrente); `Forget` e i due blocchi di `RunAsync` in `Task.Run`.
- Verifica su Windows: `Log.Time` su Trim e sull'attesa di `Gate`; "clic, testo pronto" dopo un elenco con corpi nuovi.

### V-12 Ricerca senza rete: ogni file letto, decifrato e deserializzato a ogni ricerca

- Dove: `Core/Offline.cs:264-283` (`BodyCache.Search`: `Parallel.ForEach` con `ProtectedData.Unprotect` +
  `JsonSerializer.Deserialize` per file, poi `PlainText` con 2 regex `NonBacktracking` + `HtmlDecode` + `Contains`
  culturale).
- Scenario: per N file, N `Unprotect` e N deserializzazioni del JSON intero (immagini incorporate in base64 comprese, che
  la ricerca non usa). Nessuna uscita anticipata al limite `max`. Stima per estrapolazione da CLAUDE.md (~400 corpi in
  2,6 s su un thread, 423 in 0,73 s in parallelo): 5000 file ~8-9 s in parallelo, 20.000 ~35 s. Con ~90 KB a corpo il
  limite di 200 MB tiene ~2400 file per account; 5000-20.000 sommando gli account in "Tutti gli account".
- Gravita: Bassa-Media (solo senza rete). Stato: CONFERMATO il conteggio; SOSPETTA la ripartizione fra DPAPI/JSON/regex.
- Proposta: salvare in `SavedBody` il testo semplice gia normalizzato (nel `Task.Run` di `Save`); meglio un indice per
  account cifrato in un solo file (un `Unprotect` invece di N); ricerca `Ordinal`.
- Verifica su Windows: `--senza-rete` con la copia dei 30 giorni piena; "ricerca nella copia sul PC (N risultati)" con N
  diversi; `dotnet-trace` per dividere `CryptUnprotectData`/`JsonSerializer`/`Regex`.

### V-13 Dopo ogni await il lavoro dei provider torna sul thread dell'interfaccia

- Dove: in `Core/` `ConfigureAwait(false)` compare una volta sola (`SenderLogos.cs`). Le chiamate partono dall'interfaccia
  (es. `UI/ReadingView.xaml.cs:655` `await provider.GetBodyAsync(...)`). Girano cosi sull'interfaccia: `JsonNode.Parse` di
  corpi Graph e `Convert.FromBase64String` delle immagini (`GraphProvider.cs:717`), in Gmail `whole.HtmlBody`, decodifica
  delle immagini e `MemoryStream.ToArray` (`:886-914`), `PlainPreview` per 50 messaggi, `MailHtml.ToText` in `BuildAsync`.
- Scenario: tutto cio che il provider fa dopo un await, quando la chiamata nasce sull'interfaccia, gira sulla finestra.
- Gravita: Media-Bassa. Stima: pochi ms per i messaggi normali, qualche decina per corpi da 1-2 MB con immagini.
- Stato: CONFERMATO dove gira; SOSPETTO il peso.
- Proposta: non `ConfigureAwait(false)` ovunque (`MicrosoftAuth` apre finestre); spostare solo il lavoro pesante in
  `Task.Run` (`JsonNode.Parse` e base64 di Graph, costruzione di `MessageBody` in `ReadBodyAsync`, `ToText` in
  `BuildAsync`).
- Verifica su Windows: `dotnet-trace` sul thread dell'interfaccia durante 20 clic, quota sotto `ReadBodyAsync`/`JsonNode.Parse`/`MimeKit`.

### V-14 Sole / Mostra immagini / cambio tema rifanno la pulizia dell'HTML sul thread dell'interfaccia

- Dove: `UI/ReadingView.xaml.cs:1185-1187,1297-1301,142-148` (poi `Render`); il lavoro in `ReadingPage.cs:413,424`. La
  pulizia in sottofondo (`:680-682`) vale solo per il tema e le immagini del momento dell'apertura.
- Scenario: premere il sole, "Mostra immagini" o cambiare tema con una newsletter grande aperta rifa `MailHtml.Prepare`
  (vedi Parte A: decine di ms, fino a oltre 1 s per 5 MB scuro) sul thread dell'interfaccia.
- Gravita: Media-Bassa. Stato: CONFERMATO (il costo di Prepare e MISURATO in Parte A).
- Proposta: prima di `Render`, `await Task.Run(() => ReadingPage.Warm(...))` per i corpi aperti.
- Verifica su Windows: "pagina costruita" dopo il sole su una newsletter grande.

### V-15 Dal clic alla pagina: tre o piu `ExecuteScriptAsync` in fila

- Dove: `UI/ReadingView.xaml.cs:961-977` (DOMContentLoaded: `FitAsync`, `Script(ImagesPending)`, eventuale secondo
  `FitAsync`, `ScrollTo`, `RevealAsync`) e `:996-1004` (NavigationCompleted: altri `FitAsync` + `RevealAsync`).
- Scenario: per ogni pagina almeno 3 viaggi `ExecuteScriptAsync` in fila prima che si veda (5 con immagini in attesa),
  piu 2-3 a NavigationCompleted, piu quelli per la conversazione.
- Gravita: Media-Bassa. CLAUDE.md misura "5-30 ms dopo testo pronto" per questa coda; un solo viaggio invece di tre
  toglierebbe gran parte del tratto.
- Stato: CONFERMATO il numero; SOSPETTO il guadagno.
- Proposta: un solo script che adatta, calcola le immagini in attesa, rimette lo scorrimento e, se non ne mancano, toglie
  `fitting`, restituendo il risultato; secondo script solo se mancano immagini; a NavigationCompleted un solo script
  idempotente.
- Verifica su Windows: "clic, testo pronto" vs "clic, pagina mostrata" su 20 righe, prima e dopo.

### V-16 Scrittura: screenshot codificato e poi ridecodificato; immagine da file decodificata due volte

- Dove: `UI/ComposeWindow.xaml.cs:1274-1275,1294-1295` (incolla: `FromBitmap` codifica PNG, poi `Create`/`Decode`),
  `Core/FlowToHtml.cs:264-270` (`FromBitmap`: `PngBitmapEncoder` + `ToArray`), `:249-254` (`FromFile`: un `Decode` intero
  solo per controllare il file), `UI/ComposeWindow.Format.cs:254-259` (poi un secondo `Decode` in `InsertImage`).
- Scenario: uno screenshot incollato (solo bitmap negli appunti) viene compresso in PNG e poi ridecodificato, tutto sul
  thread dell'interfaccia; una foto inserita o trascinata viene decodificata due volte.
- Gravita: Media-Bassa. Stima: per uno schermo 4K, compressione PNG + decodifica = centinaia di ms a finestra ferma; per
  una foto 12 MP, 2 decodifiche da decine di ms.
- Stato: CONFERMATO il doppio lavoro; SOSPETTI i tempi.
- Proposta: mostrare la bitmap degli appunti direttamente (copia congelata, `TransformedBitmap`/`CachedBitmap`) e
  codificare il PNG in `Task.Run`; in `FromFile` controllare solo il tipo e `PixelWidth` (l'intestazione), o restituire
  la bitmap gia decodificata da riusare.
- Verifica su Windows: `Log.Time` attorno a `OnEditorPasting`/`InsertImage`; appunti riempiti da uno script STA e "ctrl+v"
  in MISSIVA_KEYS; righe StallLog.

### V-17 Aggiornamenti degli elenchi: regroup e cartelle

- Dove: `UI/MainWindow.xaml.cs:2150-2226` (`ShowFirstPage`/`MergeFirstPage`: `rows.Clear()` + `Append` ricostruiscono
  tutto a ogni rilettura della prima pagina, chiamate da LoadMore/RefreshList/coda/Posponi), `:2398-2399,2588`
  (`AddIncoming` + `RegroupKeepingSelection`: un `Refresh`/Reset a ogni arrivo, anche quando il gruppo "Oggi" esiste gia),
  `UI/ViewModels.cs:229-256` (`SetFolders`: `IndexOf`/`Contains` in cicli, O(F^2) con 300 cartelle, ma ~10^5 confronti,
  sotto 1-2 ms).
- Scenario: basta un messaggio diverso fra copia locale e server (all'avvio, quasi sempre) perche `MergeFirstPage` rifaccia
  tutte le righe gia caricate scorrendo; ogni arrivo nella cartella aperta rifa tutti i gruppi.
- Gravita: Media (regroup e merge); Bassa (SetFolders). Stima: 25-60 ms con 1000 righe caricate (SOSPETTO).
- Stato: CONFERMATO; SOSPETTO il costo.
- Proposta: applicare solo la differenza (Remove per le sparite, Prepend per le nuove, IsRead/IsFlagged come il ramo
  "stessi messaggi"); chiamare `Refresh` solo se il gruppo di una riga nuova non c'era gia.
- Verifica su Windows: StallLog vicino a "elenco riletto: prima pagina unita a N"; "arrivo di prova" con `lampi.ps1 -List`.

### V-18 Barra delle cartelle senza virtualizzazione

- Dove: `UI/MainWindow.xaml:393` (ScrollViewer della barra), `:551-556` (il `ListBox` delle cartelle riceve altezza
  infinita e realizza tutte le righe non compresse; ogni riga ha `NavItem` + DataTemplate con Button, 3 TextBlock,
  ~12 binding, un FindAncestor e un ToolTip).
- Scenario: con 300 cartelle aperte la prima `SetFolders` dalla copia locale cade nella stessa fase dell'elenco.
- Gravita: Media con molte cartelle. Stima: 0,2-0,5 ms per cartella, 60-150 ms (SOSPETTO).
- Stato: struttura CONFERMATA; costo SOSPETTO.
- Proposta: una lista piatta virtualizzata, come `PeopleView.Sections`.
- Verifica su Windows: "cartelle dalla copia locale" e "elenco nel disegno" con una `cartelle.json` di prova da 300
  cartelle.

### V-19 Rete di Graph: niente compressione, HTTP/1.1, cartelle in serie, sei richieste ripetute a ogni avvio

- Dove: `Core/GraphProvider.cs:33-35` (`HttpClient` predefinito: `AutomaticDecompression None`, HTTP/1.1), `:224-272`
  (sottocartelle in serie, un giro per cartella con figlie), `:234` (gli id delle sei cartelle speciali richiesti a ogni
  avvio invece di prenderli dalla copia locale), `:811` (semaforo di 4 per chiamata, non per casella: all'avvio elenco +
  delta + categorie + risposte automatiche superano il limite).
- Scenario: il JSON arriva non compresso; dopo l'inattivita del pool HTTP/1.1 la conversazione e il vicino aprono nuove
  connessioni TLS; le cartelle annidate costano un RTT ciascuna in serie.
- Gravita: Media. Stima: a 3 Mbit/s 100 KB ~270 ms contro 40-60 compressi; ogni TLS nuova 50-150 ms; 20 cartelle con
  figlie 1,5-3 s.
- Stato: codice CONFERMATO; effetto di rete SOSPETTO.
- Proposta: un `SocketsHttpHandler` condiviso con `AutomaticDecompression All`, timeout di inattivita di qualche minuto,
  HTTP/2 (`DefaultRequestVersion 2.0`, `RequestVersionOrLower`, `EnableMultipleHttp2Connections`); figlie in parallelo per
  livello (o `$expand=childFolders`); id speciali dalla copia locale; un limitatore per casella con un posto riservato
  all'apertura del messaggio; `$batch` per le azioni su piu messaggi.
- Verifica su Windows: "Graph ..." sul primo clic dopo 3 minuti fermi e sul primo elenco; `dotnet-counters`
  (System.Net.Http); `Content-Encoding` nelle risposte.

### V-20 Gmail e IMAP: comandi di controllo in piu e scansione della Posta inviata che occupa la connessione

- Dove: `Core/GmailProvider.cs:542,609` e `Core/ImapProvider.cs:763-769` (`FreshInboxAsync` NOOP + SEARCH + FETCH, poi di
  nuovo NOOP + SEARCH per il conteggio; con le regole un terzo NOOP), `GraphProvider.cs:417-427` (due GET in serie),
  `GmailProvider.cs:619-630` (scansione della Posta inviata sulla stessa connessione in sottofondo, ~2 s: CLAUDE.md).
- Scenario: a ogni controllo dei nuovi messaggi 2-3 giri di rete in piu per account (ogni 100 s e a ogni avviso IDLE); la
  scansione della Posta inviata (15 s dopo l'avvio) occupa la connessione condivisa, e frecce/Ctrl+. in quel momento
  aprono i corpi a freddo (0,3-0,6 s invece di 30-160 ms).
- Gravita: Bassa-Media. Stima: 50-150 ms per controllo.
- Stato: CONFERMATO; effetto SOSPETTO.
- Proposta: un solo `RunAsync` per controllo (NOOP, SEARCH UNSEEN che da numero e UID, FETCH dei soli UID nuovi; per Graph
  le due GET in parallelo); scansione della Posta inviata a blocchi rilasciando la connessione, o solo a finestra ferma.
- Verifica su Windows: ProtocolLogger di MailKit in una build di prova; i ms di "controllo di ..." nel registro.

### V-21 Scritture su disco sincrone sul thread dell'interfaccia, e `Store.Save` frequente

- Dove: `UI/MainWindow.xaml.cs:1829-1832` (`SaveFirstPage`, due volte per caricamento di cartella), `:1074`
  (`SaveFolders`), `Core/Store.cs:477-488` (`Store.Save`: serializza tutte le impostazioni, 83 chiamate, alcune a ogni
  ricerca `:4183`, ogni gruppo aperto/chiuso `UI/ViewModels.cs:36`, ogni tacca di zoom `ReadingView.xaml.cs:609`, ogni
  controllo con regole `Core/Rules.cs:581` che riscrive `RulesSeen` fino a 2000 voci), `Core/ActionQueue.cs:104-117`
  (`Save` dentro il lock per ogni azione senza rete), `Core/Contacts.cs:121-145` (`contatti.dat` riscritto intero a ogni
  contatto). `Store.Save` non fa flush su disco (durata, non velocita).
- Scenario: una scrittura sincrona (JSON + DPAPI + creazione + rinomina + antivirus) proprio quando l'elenco compare o a
  gesti piccoli.
- Gravita: Bassa-Media. Stima: 2-12 ms per scrittura; `contatti.dat` con 4500 contatti ~2 MB, 10-40 ms per modifica.
- Stato: CONFERMATO dove e quando; SOSPETTI i tempi.
- Proposta: serializzare sul thread dell'interfaccia (istantanea), poi cifrare e scrivere in `Task.Run` dove vince
  l'ultima scrittura per file; `Store.SaveSoon()` accorpato a 300-500 ms (immediato solo per uscita/fine sessione/account);
  seconda `SaveFirstPage` solo se le anteprime cambiano; `contatti.dat` come Importa (`Task.Run`).
- Verifica su Windows: `Log.Time` attorno alle chiamate; ProcMon (scritture per file e per thread); StallLog.

### V-22 Rubrica e domini noti: salvataggio e prima lettura dentro il lock usato dall'interfaccia

- Dove: `Core/AddressBook.cs:323-342` (`Save` tiene `Gate` durante JSON + DPAPI + scrittura; `Received` e `Suggest`
  entrano in `Gate` dall'interfaccia), `Core/SenderLogos.cs:130-131` (la prima riga dell'elenco chiama `Load`, che legge
  `domini.dat` con DPAPI e JSON per riflessione sul thread dell'interfaccia, fra "Posta in arrivo scelta" e "elenco nel
  disegno"), `Core/Calendar.cs:435` (copia del calendario per riflessione).
- Gravita: Bassa. Stima: 5-20 ms di lock per salvataggio (sovrapposizione rara); 2-15 ms una volta per `domini.dat`.
  CLAUDE.md attribuisce 80-100 ms alla riflessione delle impostazioni: per tipi piccoli stima 10-40 ms.
- Stato: CONFERMATO; SOSPETTE le quote.
- Proposta: sotto il lock solo la copia delle voci, poi JSON/DPAPI/scrittura fuori; `ScheduleSave` riarmato a ogni
  modifica; `Load` dei domini in sottofondo all'avvio (come `AddressBook.Preload`); contesti del generatore
  (`JsonSerializerContext`) al posto della riflessione.
- Verifica su Windows: `Log.Time` attorno ai lock e a `Load`; "tempi: avvio".

### V-23 Costi minori confermati (una riga ciascuno)

- **Logo letto dal disco per messaggio e per disegno** (`UI/ReadingView.xaml.cs:836-837`, `UI/ViewModels.cs:1241`): in una
  conversazione lunga con logo, `Render` legge lo stesso PNG per ogni voce sul thread dell'interfaccia, e Chromium lo
  chiede N volte con indirizzi diversi. Tenere i byte per dominio e servire un indirizzo per dominio. CONFERMATO.
- **Salvataggio automatico converte il documento 4 volte** (`UI/ComposeWindow.xaml.cs:376-377,1425`): una sola `FlowToHtml.Convert`
  per salvataggio, riusando l'HTML per `ContentState` e lo stato salvato. CONFERMATO.
- **Originale riconvertito con `ToText` a ogni salvataggio di una risposta** (`Core/GmailProvider.cs:1427`,
  `Core/ImapProvider.cs:1501`, `UI/ComposeWindow.xaml.cs:797`): calcolarlo una volta per `MessageBody`
  (`ConditionalWeakTable`) o in `Task.Run`. CONFERMATO.
- **Regole: lo stesso corpo riconvertito in testo per ogni regola** (`Core/Rules.cs:381-384,441-444`): il testo una volta
  per messaggio nel ciclo di `RunAsync`. CONFERMATO.
- **Allegati interi in memoria con copie in piu** (`Core/GraphProvider.cs:788-797` ~2x, `Core/GmailProvider.cs:934-942`
  ~3x): per Salva/Apri scrivere direttamente su file con uno `Stream`. CONFERMATO.
- **Pagina del riquadro copiata 4-5 volte a ogni disegno** (`UI/ReadingPage.cs:67-76`, `UI/ReadingView.xaml.cs:1100`): un
  solo `StringBuilder` con capacita stimata e i byte UTF-8 calcolati una volta per pagina. CONFERMATO.
- **`MailCache.EncryptOld` rilegge per intero la copia locale a ogni avvio** (`Core/MailCache.cs:72-75`): leggere solo il
  primo byte o un segno "conversione fatta". CONFERMATO.
- **`Process.Start`/`File.WriteAllBytes` sincroni sul thread dell'interfaccia** per link e allegati
  (`UI/ReadingView.xaml.cs:1449,1527`, `UI/P7mWindow.cs:143-145`, `UI/CalendarView.cs:868`, `UI/PeopleView.cs:228`):
  `Process.Start` in `Task.Run`, `WriteAllBytesAsync`. CONFERMATO.
- **`NotificationAvatar`: `Thread.Join` sul thread dell'interfaccia** per un mittente nuovo (`UI/NotificationAvatar.cs:30-37`):
  un thread STA dedicato con `TaskCompletionSource`, `Notify` asincrono. CONFERMATO.

### V-24 Registro: file aperto e chiuso a ogni riga

- Dove: `Core/Store.cs:539-549` (`Log.Write` prende `lock (Gate)`, `CreateDirectory`, `FileInfo`, `AppendAllText`: apre,
  scrive e chiude il file ogni volta, senza flush; rotazione a 1 MB con una sola copia `.old`). `Log.Time`/`Log.Startup`
  sono `[Conditional("DEBUG"/"PROVA")]`, quindi spariscono dalla Release; `Log.Private` restituisce "(nascosto)".
- Scenario: nella versione installata almeno a ogni non letto aperto (`UpdateBadge`), a ogni arrivo e a ogni riga di
  StallLog; senza rete, uno stack intero ogni 2 minuti per account. Nella prova le righe "tempi:" cadono dentro gli
  intervalli misurati (decine per clic), quindi il banco stesso paga questo costo.
- Gravita: Bassa (Media per l'accuratezza delle misure della prova). Stima: 0,1-1 ms per riga.
- Stato: CONFERMATO; SOSPETTO il costo per riga.
- Proposta: uno scrittore in sottofondo (`Channel`) con il file tenuto aperto, flush ogni 200-500 ms e subito per gli
  errori/all'uscita; `CreateDirectory` una volta; per gli errori di rete ripetuti una riga breve per account (come
  `accountFixLogged`).
- Verifica su Windows: un comando MISSIVA_KEYS che scrive 200 righe e da il tempo medio; "clic, testo pronto" prima e dopo.

### V-25 A riposo: i timer e le raccolte

Tutti questi timer girano anche con la finestra nascosta nell'area di notifica o ridotta a icona (CONFERMATO,
`UI/App.cs`, `UI/MainWindow*.cs`):

| Timer | Dove | Intervallo | Cosa fa |
|---|---|---|---|
| poll | `UI/App.cs:107-113` | 2 min | controllo dei nuovi messaggi (deciso, non toccato) |
| deltaTimer | `UI/App.cs:524-529` | 30 s | query delta di Graph (gira anche senza account Graph) |
| snoozeTimer | `UI/MainWindow.xaml.cs:3538` | 1 min | Posponi, invii pianificati, promemoria contrassegni |
| offlineTimer | `UI/MainWindow.Offline.cs:199` | 1 min | copia dei 30 giorni e `CompactWhenIdle` (solo visibile) |
| reminderTimer | `UI/MainWindow.Calendar.cs:123` | 30 s | promemoria (anche senza calendari) |

Risvegli a finestra nascosta ~6-7 al minuto, piu il rinnovo IDLE ogni 9 minuti (CLAUDE.md: CPU a riposo 2,3-3,2 ms/s).
`deltaTimer` e `reminderTimer` girano anche senza account Graph/calendari: si potrebbero fermare quando non servono.

`GC.Collect` (tutti sul thread dell'interfaccia; i primi quattro in modalita Aggressive/compattante): 5 s dopo
`HideToTray`; 3 s dopo la riduzione a icona; `CompactWhenIdle` ogni 5 minuti di pausa (solo visibile); `CompactAfterCopy`
dopo una copia con corpi nuovi; i due GC di `AttachmentPreview.Clear` (V-08). La "finestra ferma" guarda mouse/tastiera/
rotella/selezione della finestra principale (`UI/MainWindow.Offline.cs:215-218`), ma non vede l'input dentro WebView2:
leggendo scorrendo nel riquadro, dopo 30 s parte la copia dei 30 giorni (che finisce con un GC) e dopo 5 minuti arriva
`CompactWhenIdle`. Proposta: usare `GetLastInputInfo` per "utente lontano dal PC". CONFERMATO; SOSPETTO che l'input di
WebView2 non arrivi.

---

## Tabella riassuntiva (per guadagno atteso)

| Sigla | Titolo | Gravita | Stato |
|---|---|---|---|
| S-29 | Backslash nel CSS: ReDoS quasi cubico in `Prepare` (vedi sicurezza.md) | Alta | MISURATO |
| V-01 | Cache `known` svuotata a 1000: clic a freddo sui messaggi gia copiati | Media/Alta | CONFERMATO |
| V-02 | Riga dell'elenco: ogni testo formattato 2-3 volte | Media | CONFERMATO |
| V-04 | Elimina/Sposta passano dal riquadro vuoto | Media | CONFERMATO |
| V-05 | Invio ripete la ricerca dei suggerimenti | Media | CONFERMATO |
| V-06 | Persone: filtro+ordinamento culturale di 4500 contatti a ogni lettera | Media | CONFERMATO |
| V-08 | Due GC bloccanti alla chiusura dell'anteprima, dentro l'apertura dopo | Media | CONFERMATO |
| V-09 | "Segna letto" sul percorso del clic e buco di misura della prova | Media | CONFERMATO |
| V-10 | Avvio: una DPAPI per campo cifrato | Media | CONFERMATO |
| V-11 | BodyCache: Trim a ogni scrittura, lock condiviso con l'UI | Media | CONFERMATO |
| V-14 | Sole/Mostra immagini/tema rifanno `Prepare` sul thread UI | Media | CONFERMATO + MISURATO |
| V-19 | Graph: niente compressione, HTTP/1.1, cartelle in serie | Media | CONFERMATO |
| V-07 | Suggerimenti: scansione+alloc della rubrica a ogni lettera | Bassa-Media | CONFERMATO |
| V-12 | Ricerca senza rete: ogni file decifrato e deserializzato | Bassa-Media | CONFERMATO |
| V-13 | Lavoro dei provider dopo await sul thread UI | Media-Bassa | CONFERMATO |
| V-15 | Tre+ `ExecuteScriptAsync` per apertura | Media-Bassa | CONFERMATO |
| V-16 | Scrittura: immagini codificate/decodificate due volte | Media-Bassa | CONFERMATO |
| V-17 | Elenco: regroup e merge ricostruiscono tutto | Media | CONFERMATO |
| V-18 | Barra delle cartelle senza virtualizzazione | Media | CONFERMATO (struttura) |
| V-20 | Controllo nuovi messaggi: giri di rete in piu | Bassa-Media | CONFERMATO |
| V-03 | Tema scuro: `FormatException` per ogni parola CSS | Media-Bassa | CONFERMATO |
| V-21 | Scritture su disco sincrone, `Store.Save` frequente | Bassa-Media | CONFERMATO |
| V-22 | Rubrica/domini: salvataggio e prima lettura dentro il lock UI | Bassa | CONFERMATO |
| V-23 | Otto costi minori (logo, autosave, ToText, regole, allegati, pagina, EncryptOld, ShellExecute) | Bassa | CONFERMATO |
| V-24 | Registro: file aperto/chiuso a ogni riga | Bassa | CONFERMATO |
| V-25 | Timer a riposo e GC bloccanti | Bassa | CONFERMATO |

## Guardato e trovato a posto

- `MailHtml.Prepare` sulle email reali: lineare (MISURATO); i parser vCard/CSV/ICS/SenderCheck lineari e nei limiti
  (MISURATO); tutte le regex tranne `CssEscapedFunction` lineari (MISURATO).
- `WheelScrolling.OnFrame`, `StableScrollBar`, `ScrollHitGuard`, `StallLog`: lavoro costante per fotogramma/operazione,
  stacchi corretti (`UI/WheelScrolling.cs:240`, `UI/StableScrollBar.cs:94`, `UI/ScrollHitGuard.cs:112`).
- `MessageRowElement`: `Typeface` in cache, pennelli statici congelati, `GetDpi` economico, `GuidelineSet` leggero; la
  cache delle conversazioni (50), `Pec.Recall`, `previewBytes`, `suggestCache`, i corpi dei provider (20) e `early` di
  Graph (4) sono tutti limitati.
- `UpdateBadge`/`InboxUnread`: O(F), un aggiornamento per controllo; `MessageRows.Append`/`Prepend`/`KeyOf` con dizionario
  per conversazione.
- Avvio: `Preload`, `EncryptOld`, `TrimAll`, `WarmUp`, `Preconnect`, `StartOfflineCopy` in `Task.Run`; `Store.Load` con il
  generatore.
- `Touched` costa poco a ogni lettera (`UI/ComposeWindow.xaml.cs:468-474`); le bozze Graph non ricaricano gli allegati;
  Gmail tiene in memoria gli allegati sotto 5 MB; Importa contatti con un solo salvataggio in sottofondo.
- `GeneratedRegex` nei percorsi caldi; una sola `RegexOptions.Compiled` (`Core/GoogleContacts.Write.cs:28`), usata di
  rado. `Log.Time`/`Log.Startup` condizionali (gli argomenti non si calcolano in Release).
- Nessuna attesa bloccante classica (`.Result`/`.Wait()`/`Sleep`) sul thread dell'interfaccia in Release, tranne il
  `Thread.Join` di V-23 (avatar notifiche).
- La ricerca senza rete gira gia in parallelo e fuori dal thread dell'interfaccia (`UI/MainWindow.Offline.cs:189`); il
  corpo dalla copia su disco in linea gia in `Task.Run` (`UI/ReadingView.xaml.cs:650`).

## Non guardato

- Il comportamento a runtime non misurabile da qui: WebView2 reale, MsalCacheHelper, il costo della singola eccezione in
  WPF, il profilo del thread dell'interfaccia durante lo scorrimento con i dati veri.
- `CalendarView`, `SettingsWindow` (salvataggio firme ogni 500 ms, letto solo in CLAUDE.md), i dettagli di `Idle.cs`,
  `PdfWorker`/`Sandbox`, `ActionQueue.ReplayAsync`, le notifiche oltre l'avatar.
- `MessageRowElement` e `WheelScrolling` a fondo (gia molto lavorati nei giri precedenti): qui solo i percorsi caldi
  chiesti.
- `dotnet-trace`/`dotnet-counters` con i dati veri dell'autore, che restano il modo per trasformare le stime SOSPETTE in
  numeri.

---

## Come rifare le misure su Windows

Il banco sta fuori dal repository (per esempio in `C:\Temp\bench`). E un progetto console `net10.0` che compila **copie**
dei file senza dipendenze da Windows, con finti al posto di `Store`/`Log`/`Settings`/`ColorConverter` (su Windows i finti
di `Color`/`ColorConverter` non servono: si referenzia `PresentationCore`). I generatori di email e la funzione di misura
sono questi; i file misurati (`MailHtml.cs`, `Invitation.cs`, `SearchTerms.cs`, `Mailto.cs`, `ContactLinks.cs`,
`SenderCheck.cs`, la parte vCard/CSV di `Contacts.cs`) si copiano dal repository.

```csharp
// Misura: scalda, poi N giri; stampa mediana, 95 percentile e MB allocati per giro.
static void Time(string name, int bytesIn, Action run, int runs = 20, int warm = 3)
{
    for (int i = 0; i < warm; i++) run();
    var ms = new double[runs];
    long a0 = GC.GetTotalAllocatedBytes(true);
    for (int i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); run(); sw.Stop(); ms[i] = sw.Elapsed.TotalMilliseconds; }
    double allocMb = (GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0 / runs;
    Array.Sort(ms);
    Console.WriteLine($"{name,-42} {bytesIn,10:n0} B  med {ms[runs/2],9:0.000}  p95 {ms[(int)(runs*0.95)],9:0.000}  alloc {allocMb,8:0.0} MB");
}

// Newsletter ripetuta fino a targetBytes: tabelle con bgcolor/style, link, immagini remote, pulsanti colorati.
static string Newsletter(int target) {
    var sb = new StringBuilder(target + 1024);
    sb.Append("<html><head><style>.c{background:#fff;color:#202020}a{color:#1155cc}</style></head><body bgcolor=\"#f3f3f3\">");
    for (int i = 0; sb.Length < target; i++)
        sb.Append($"<table width=600 bgcolor=#fff style=\"border:1px solid #ddd;background-color:#fafafa\"><tr><td style=\"color:#333;padding:12px\">")
          .Append($"<h2 style=\"color:#1a1a1a\">Notizia {i}</h2><p style=\"color:#444;background:#f7f7f7\">Testo {i} <a href=\"https://esempio{i}.example/p?x={i}\">link</a>. ")
          .Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ")
          .Append($"</p><img src=\"https://img{i}.example/f{i}.png\" width=580 height=200 alt=\"Foto {i}\">")
          .Append($"<a href=\"https://esempio{i}.example/a\" style=\"background:#0a7d33;color:#fff;padding:10px\">Vai</a></td></tr></table>");
    return sb.Append("</body></html>").ToString();
}

// Casi che mettono in difficolta (profondita e backslash sono i due critici):
// - div annidati:  "<html><body>" + ripeti("<div style=\"color:#121212\">", d) + "fine" + ripeti("</div>", d) + "</body></html>"
// - 100.000 '<':   "<html><body>" + new string('<', 100000) + "</body></html>"
// - backslash CSS: "<html><body><div style=\"" + new string('\\', N) + "\">x</div></body></html>"   // N = 500, 1000, 2000...

// Esempi di chiamate:
foreach (var kb in new[]{50,200,1024,5120}) { var h = Newsletter(kb*1024);
    Time($"newsletter {kb} KB chiaro", h.Length, () => MailHtml.Prepare(h, false, false, _ => null));
    Time($"newsletter {kb} KB scuro",  h.Length, () => MailHtml.Prepare(h, true,  false, _ => null)); }

// ReDoS isolato (un solo giro, cresce in fretta):
var esc = new Regex(@"(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\\(?:[0-9a-fA-F]{1,6}\s?|[^\r\n0-9a-fA-F])(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\(");
foreach (var n in new[]{250,500,1000,2000}) { var sw = Stopwatch.StartNew(); esc.Matches(new string('\\', n)).Count.ToString(); Console.WriteLine($"{n}\t{sw.ElapsedMilliseconds} ms"); }
```

`ContactFiles.VCard`/`Csv`/`Invitation.Parse`/`SearchTerms.Parse` si misurano allo stesso modo generando il testo (5000
vCard/CSV, un .ics appena sotto 256 KB). Sul PC dell'autore conviene poi confermare le stime della Parte B con gli strumenti
gia descritti in CLAUDE.md (righe "tempi:", StallLog, "costo righe", "scorri N", `dotnet-trace`).
