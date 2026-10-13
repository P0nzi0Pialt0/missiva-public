# Audit delle funzionalità mancanti

- Commit di main letto: `59ed8bafa7b59942f04bef8c084bbb5d3455bca9` (59ed8ba, "Dati di prova senza indirizzi e nomi veri;
  CLAUDE.md: sezione per le sessioni online").
- Data: 3 ottobre 2026. È una sessione online su Linux: Missiva non è stata compilata né eseguita, e nessuna prova è stata
  fatta. Ogni giudizio viene dalla lettura del codice.
- I riferimenti `file:riga` valgono per quel commit, anche quelli a CLAUDE.md e LEGGIMI.md.
- **CONFERMATO**: letto nel codice, con file e riga; per un'assenza, cercato senza trovarlo (sono indicati i termini e le
  cartelle). **SOSPETTO**: per esserne certi manca qualcosa, e il punto dice cosa.
- Quello che riguarda Google, Microsoft o Windows fuori dal repository (limiti, regole di pubblicazione, costi) viene dalla
  memoria, non da una prova. È segnato "da verificare" e va controllato sulla loro documentazione prima di decidere.
- Esclusi perché già decisi: la verifica dell'editore Microsoft, DirectManipulation, l'anteprima di Office con i gestori di
  Windows, JavaScript nelle email, il "fissato" di Outlook via Graph, una riscrittura in un altro framework.

## In breve

- Per gli amici ci sono due blocchi, e vengono prima di ogni funzione:
  - l'accesso a Gmail chiede a ognuno di creare un progetto su Google Cloud (F-05);
  - l'installazione chiede .NET 10 SDK e PowerShell 7 e compila sul posto (F-01). Anche la disinstallazione da App
    installate chiama PowerShell 7.
- Nell'uso di ogni giorno pesa di più il limite di 3 MB per allegati e immagini con gli account Microsoft (F-15): non c'è
  un'alternativa.
- Delle 21 mancanze dichiarate in CLAUDE.md e controllate nella Parte 1:
  - 18 mancano ancora (una per scelta e una che non riguarda il codice);
  - 1 è a metà;
  - 2 sono già fatte.

  In LEGGIMI.md cinque frasi descrivono un comportamento che il codice non ha più.
- Leggendo sono saltati fuori due difetti piccoli ma visibili:
  - per Google, la finestra dell'annullamento promette ai partecipanti una nota che poi viene buttata (1-06);
  - gli eventi di tutto il giorno oltre il terzo non si aprono in nessuna vista del calendario (F-42).
- Le funzioni mai provate dal vivo sono 14 gruppi (Parte 3). Le quattro con il danno più alto scrivono sul server:
  - risposte automatiche;
  - regole di Outlook;
  - contatti di Google;
  - azioni dalle notifiche.

---

## Parte 1: mancanze dichiarate

Ogni "Non ancora", "Non fatto", "Non gestito", "Non c'è" e "Aperto" di CLAUDE.md, confrontato con il codice.

| N. | Dichiarato (CLAUDE.md) | Stato | Prova nel codice |
|---|---|---|---|
| 1-01 | :442-444 "Aperto": salto di 3000-3800 px a fine flick, "da rivedere" | FATTO | Era la pagina successiva. `AppendKeepingPosition` è usato in MainWindow.xaml.cs:2126, MainWindow.Unified.cs:177 e :339. |
| 1-02 | :478 riga con meno elementi, o un elenco che disegna da sé: "non fatto" | A METÀ | **Fatta la riga**: un solo elemento, MessageRowElement.cs:23, scelto in MainWindow.xaml:23 e MainWindow.xaml.cs:43.<br>**Manca l'elenco**: è ancora una ListBox virtualizzata.<br>**Resta da togliere**: il modello vecchio, "da togliere quando A ha finito", è ancora in MainWindow.xaml:795-966 con l'interruttore `MISSIVA_RIGA_VECCHIA` (MainWindow.xaml.cs:42-43). |
| 1-03 | :526 "Non gestito": cambio del solo colore d'accento con Missiva aperta | MANCA | App.cs:353-354 ascolta solo la categoria General. App.cs:369 esce se chiaro o scuro non cambia. `MessageRowElement.RefreshAll` (MessageRowElement.cs:32) è chiamato solo da MainWindow.xaml.cs:2000. Cercati in `*.cs` ColorValuesChanged, DWMCOLORIZATION e 0x0320: niente. |
| 1-04 | :622 finestra di scrittura preparata in anticipo: "Non fatto" | MANCA (scelta voluta) | C'è solo il correttore acceso dopo il primo disegno (ComposeWindow.xaml.cs:72). Nessuna ComposeWindow né RichEditor viene creata fuori vista: le crea solo un comando dell'utente. |
| 1-05 | :806 "Non c'è Google Meet" | MANCA | GmailProvider.Calendar.cs:114 lo dice in un commento. La casella Teams compare solo con un Tenant (EventWindow.cs:268). Il link Meet si legge (GoogleCalendar.cs:88-90) ma non si crea mai: non c'è `conferenceData` in scrittura. |
| 1-06 | :806 e LEGGIMI.md:399: nessuna nota nell'annullamento di Google | MANCA, e la finestra dice il contrario | CalendarView.cs:687-688 chiede anche per Google una nota che i partecipanti "ricevono ... con questa nota". GmailProvider.Calendar.cs:171 la scrive solo nel registro, poi annulla senza. È un difetto: per Google la casella va tolta o spiegata. |
| 1-07 | :819 "Non c'è 'Altri contatti'" | MANCA | Si legge solo `people/me/connections` (GoogleContacts.cs:22). Gli ambiti sono GoogleAuth.cs:35 e :41. Cercati otherContacts e contacts.other: niente. |
| 1-08 | :869 "Non ancora: l'agenda di oggi accanto alla posta" | MANCA | Il calendario prende il posto della posta (MainWindow.Calendar.cs:54-64). I dati ci sono già: `upcoming` (MainWindow.Calendar.cs:106), riempito a :143-151. Vedi F-31. |
| 1-09 | :869 "Non ancora: Gmail (Calendar API)" | FATTO | `HasCalendar` in GmailProvider.Calendar.cs:15, lettura a :59, scritture a :116-194. Restano due frasi vecchie: MainWindow.xaml.cs:3494 ("oggi c'è solo per gli account Microsoft") e LEGGIMI.md:281. |
| 1-10 | :869 "Non ancora: CalDAV" | MANCA | CalendarView.cs:277 lo annuncia per "più avanti". Models.cs:551: `HasCalendar` è falso di base. Cercati PROPFIND, REPORT e caldav: solo note. Vedi F-32. |
| 1-11 | :869 "Non ancora: le caselle condivise" (calendario) | MANCA | GraphProvider.cs:1172-1173: `HasCalendar => !IsShared`; servirebbe Calendars.ReadWrite.Shared. |
| 1-12 | :931 "Non ancora: allegati sul PC" | MANCA | Offline.cs:167-171 salva degli allegati solo il nome e le immagini incorporate fino a 1 MB. |
| 1-13 | :968 "Non ancora: Posta in uscita su disco, Copia, categorie, Posponi e date dei contrassegni in coda" | MANCA | ActionQueue.cs:8: `QueuedKind { Move, Read, Unread, Flag, Unflag }`. La Posta in uscita sta solo in memoria (ActionQueue.cs:352 e :366, suggerimento in MainWindow.Offline.cs:142-143). |
| 1-14 | :1026 "Non ancora": ricevute PEC collegate con "Nascondi ricevute", altri gestori, elenco AgID | MANCA | Pec.cs:159 legge X-Riferimento-Message-ID in `PecInfo.Reference` (Pec.cs:29). Pec.cs:34 definisce `IsReceipt`. Nessuno dei due è usato altrove.<br>I gestori noti sono due (MailAutoconfig.cs:69-70).<br>Pec.cs:403 manda l'utente a controllare da sé l'elenco AgID. Vedi F-23. |
| 1-15 | :1259 "non ancora: gruppi di Outlook" | MANCA | GraphProvider.cs:1102 legge solo `me/contacts`. Cercati contactFolders, distribution e contactGroup: niente. Vedi F-40. |
| 1-16 | :1259 "non ancora: scheda della persona dei destinatari" | MANCA | A e Cc sono testo (ReadingPage.cs:457-459). L'azione "person" apre sempre il mittente (ReadingView.xaml.cs:1218-1219). |
| 1-17 | :1320 "Non fatte: sezioni comprimibili" (Persone) | MANCA | Il record `Header` (PeopleView.Sections.cs:31) non ha uno stato aperto o chiuso. Cercati collaps, expand, comprim, IsExpanded ed Expander in PeopleView*.cs: solo `Visibility.Collapsed`. |
| 1-18 | :1430 "Non fatto": nome della cartella aperta nell'intestazione dell'account | MANCA | MainWindow.xaml:517 mostra solo l'indirizzo (`Header`, ViewModels.cs:100). |
| 1-19 | :1444 "Non fatto": numero dei non letti su Preferiti e Tutti gli account chiusi | MANCA | MainWindow.xaml:409 e :453 mostrano solo il titolo. La somma va solo nelle righe (MainWindow.Unified.cs:62-63). |
| 1-20 | :1649 "Non ancora: CONDSTORE (CHANGEDSINCE)" | MANCA | ImapProvider.cs:173 elenca la capacità solo nella prova della connessione. Cercati CHANGEDSINCE, ModSeq, HIGHESTMODSEQ e QRESYNC: niente. |
| 1-21 | :230-233 Google: da "In test" a "In produzione", verifica e CASA: "ancora da fare" | MANCA (non è codice) | Nessun client Google predefinito (GoogleAuth.cs:149-154). Vedi F-05. |

### Frasi di LEGGIMI.md superate dal codice

Gli amici leggeranno LEGGIMI.md, quindi queste frasi contano.

| LEGGIMI.md | Dice | Il codice |
|---|---|---|
| :400-401 | "i contatti di Gmail si leggono soltanto" | Si modificano dopo "Consenti la modifica dei contatti": GoogleContacts.Write.cs:66 (crea), :80 (modifica) e :106 (elimina); GmailProvider.Contacts.cs:26-35.<br>LEGGIMI.md:118-121 fa aggiungere al progetto Google solo l'ambito di sola lettura. Chi usa un progetto suo non sa che serve anche l'ambito `contacts`. Cosa succede in quel caso: SOSPETTO, va provato. |
| :404-405 | Regole di Gmail "solo sui nuovi messaggi ancora da leggere" | Valgono per i messaggi letti o no (Rules.cs:552-553, `GetArrivedAsync`). Lo stesso LEGGIMI, a :236-237, dice già il giusto. |
| :247-248 | "nelle conversazioni l'icona della persona accanto a Inoltra" | L'icona è tolta (ReadingPage.cs:215-216): la scheda si apre da foto e nome (ReadingPage.cs:147-153). |
| :362-363 | Il clic sulla notifica "apre la Posta in arrivo ... con il messaggio selezionato" | Apre il messaggio in una finestra sua (App.cs:954-957); la Posta in arrivo si apre solo per "N nuovi messaggi" (App.cs:962). È vecchio anche il commento di App.cs:797-799. |
| :281 | "Calendario per gli account Microsoft" | C'è anche per Google (GmailProvider.Calendar.cs:15). |

---

## Parte 2: mancanze non dichiarate

Ogni punto ha questa forma:
- **Dove**: il punto d'aggancio, o il pezzo che c'è a metà, con file e riga.
- **Scenario**: una situazione concreta in cui la mancanza pesa.
- **Importanza**: separata per l'autore e per gli amici.
- **Stato**: CONFERMATO (con cosa ho cercato e dove) o SOSPETTO (con cosa manca per esserne certi).
- **Proposta**: come farla, con una stima grossolana (ore o giorni di lavoro sul PC dell'autore, prove comprese) e le dipendenze.

### 1. Distribuzione agli amici

#### F-01 Pacchetto pronto invece della compilazione sul posto

**Dove**
- installa.ps1:42 esegue `dotnet publish` sul PC di chi installa; LEGGIMI.md:15 chiede .NET 10 SDK.
- La voce di App installate chiama `pwsh.exe` (installa.ps1:63), e disinstalla.ps1:11 lo riavvia con pwsh.exe.

**Scenario**
- Un amico con un Windows 11 appena comprato non ha .NET 10 SDK, PowerShell 7 né i sorgenti, e il repository dei sorgenti è
  privato (CLAUDE.md:11).
- La compilazione scarica anche i pacchetti NuGet: serve la rete, e ci vogliono minuti.
- Per togliere Missiva da App installate, Windows lancia pwsh.exe, che su un Windows normale non c'è.
- I due script sono UTF-8 senza BOM (i primi byte sono `23 20 49` e `23 20 44`). Windows PowerShell 5.1 li leggerebbe con
  la codifica locale e storpierebbe le lettere accentate. SOSPETTO: l'effetto non è stato provato.

**Importanza**: l'autore Bassa · amici Alta.

**Stato**: CONFERMATO.
- Cercati in tutto il repository `.github`, `*.iss`, `*.wxs`, `*.msix*`, `*.appinstaller`, `*.msi`, `*.zip`, winget e
  release: niente.
- Nessun tag git.

**Proposta**
- Due strade:
  1. L'autore compila con lo stesso `dotnet publish` e distribuisce uno zip con:
     - Missiva.exe (circa 245 MB, Missiva.csproj:35);
     - un `installa-amici.ps1` che copia soltanto, senza compilare;
     - un disinstalla che funziona con powershell.exe 5.1 (script salvati con BOM).
  2. Un programma d'installazione per utente, senza amministratore (per esempio Inno Setup), che scrive le stesse chiavi
     di installa.ps1:47-88.
- **Stima**: strada 1 mezza giornata; strada 2 1-2 giorni.
- **Dipendenze**:
  - un posto pubblico per il file: il repository dei sorgenti è privato;
  - F-03 per SmartScreen;
  - LEGGIMI.md aggiornato (Parte 1).

#### F-02 Versione nell'app e controllo degli aggiornamenti

**Dove**
- La versione è `<Version>0.3.0</Version>` in Missiva.csproj:15. La legge solo installa.ps1, alle righe :9 e :61.
- app.manifest:3 dice invece 2.0.0.0.
- Impostazioni non ha una pagina Informazioni (SettingsWindow.cs:94-96).
- Il menu delle impostazioni ha solo Impostazioni, Aggiungi account e Apri la cartella dei registri (MainWindow.xaml.cs:4657-4671).

**Scenario**
- Un amico scrive "non mi arriva la posta": l'autore non sa che versione ha.
- L'amico non sa che ne esiste una nuova.
- Una correzione resta ferma sui PC degli amici finché l'autore non li avvisa uno per uno.

**Importanza**: l'autore Bassa · amici Alta.

**Stato**: CONFERMATO. Cercati GetName().Version, FileVersionInfo, InformationalVersion, ProductVersion, About,
"Informazioni su", Novità, releases e /latest in `*.cs`, `*.xaml`, `*.ps1` e `*.csproj`: nessun uso.

**Proposta**
- Impostazioni, Generali, Informazioni, con:
  - la versione, presa dall'assembly;
  - "Copia le informazioni per l'assistenza": versione, versione di Windows e tipi di account, senza indirizzi.
- Un controllo al giorno di un piccolo file pubblico con l'ultima versione: solo lettura, nessun dato inviato. Se ce n'è
  una nuova: "C'è Missiva X: scaricala".
- L'aggiornamento automatico vero (scaricare l'exe e sostituirlo mentre è in uso) viene dopo, con la firma (F-03).
- Allineare app.manifest alla versione.
- **Stima**: 1 giorno senza aggiornamento automatico; 3-4 giorni con.
- **Dipendenze**: lo stesso posto pubblico di F-01.

#### F-03 Firma del codice e SmartScreen

**Dove**: nessun passo di firma in installa.ps1 (dopo :42).

**Scenario**
- Oggi Missiva nasce compilata sul PC, quindi senza il marchio "scaricato da Internet": SmartScreen non dovrebbe
  intervenire.
- Con lo zip di F-01 il file arriva da Internet. Al primo avvio compare "Windows ha protetto il PC", con "Esegui comunque"
  nascosto sotto "Ulteriori informazioni". Un amico si ferma lì, o pensa a un virus.

**Importanza**: l'autore Bassa · amici Alta, appena si passa allo zip.

**Stato**
- CONFERMATO che la firma manca: cercati signtool, Authenticode, thumbprint, codesign e SmartScreen; trovato solo il
  marchio sugli allegati (Safety.cs:60).
- SOSPETTO il comportamento di SmartScreen: va provato su un PC che non ha mai visto il file.

**Proposta**
- **Subito**: nelle istruzioni per gli amici, una schermata di "Esegui comunque", e lo sblocco dello zip (Proprietà,
  Sblocca) prima di estrarlo.
- **A regime**, due strade:
  - un servizio di firma per sviluppatori individuali (costi e requisiti d'identità da verificare: alcuni servizi
    accettano solo aziende, o solo certi paesi);
  - la pubblicazione su Microsoft Store come MSIX, che porta anche Condividi (F-08). Le condizioni per un account
    individuale sono da verificare.
- **Stima**:
  - istruzioni 1 ora;
  - firma nella pubblicazione mezza giornata, una volta ottenuto il certificato;
  - Store 3-5 giorni.

#### F-04 Primo avvio senza account

**Dove**
- MainWindow.xaml.cs:120 apre Aggiungi account da solo, ma solo se la finestra è visibile.
- `OnAddAccount` è a :4497-4502.

**Scenario**
- L'amico chiude Aggiungi account. Oppure Missiva parte nascosta (`--nascosta`), e allora :120 non scatta.
- Resta una finestra vuota con:
  - "Seleziona un elemento da leggere" (ReadingView.xaml:8);
  - il pulsante "Aggiungi account" in fondo alla colonna delle cartelle (MainWindow.xaml:387-390).
- Non c'è una parola su cosa fare, né su cosa serve per Gmail.

**Importanza**: l'autore Bassa · amici Media.

**Stato**: CONFERMATO, A METÀ. Cercati Benvenut, Welcome, EmptyState e "Nessun account" in UI: c'è solo il dialogo
dei link mailto senza account (MainWindow.xaml.cs:4488).

**Proposta**
- Senza account, al posto dell'elenco una pagina di benvenuto con:
  - "Aggiungi account";
  - cosa serve per ogni tipo di account;
  - "Guida" (F-46).
- **Stima**: 2-4 ore.

#### F-05 Gmail senza un progetto Google Cloud per ogni amico

**Dove**
- GoogleAuth.cs:149-154: il client si prende solo dalle Impostazioni, altrimenti "Manca l'app Google di Missiva".
- AccountWindow.xaml:114-123 e AccountWindow.xaml.cs:534-541: i campi ID client e secret.
- LEGGIMI.md:95-125: i sette passi.
- Per Microsoft invece c'è un client predefinito (MicrosoftAuth.cs:23, precompilato in AccountWindow.xaml.cs:33).

**Scenario**
- Un amico con Gmail deve:
  - creare un progetto su Google Cloud;
  - attivare la Gmail API;
  - configurare la schermata di consenso;
  - pubblicare l'app;
  - aggiungere l'ambito della posta;
  - creare un client "App desktop";
  - copiare ID e secret, che si vede una volta sola.
- Se lascia l'app "In test", l'accesso scade dopo 7 giorni (LEGGIMI.md:105-106) e Missiva smette di funzionare dopo una
  settimana.
- È il blocco più grosso.

**Importanza**: l'autore Bassa (ha già il suo progetto) · amici Alta.

**Stato**: CONFERMATO.
- Cercati in `*.cs` una costante con un client Google, DefaultGoogle e GoogleDefault: niente.
- Il secret è cifrato con DPAPI per l'utente (Store.cs:490-491): non si può nemmeno preparare un `impostazioni.json` da
  copiare.

**Alternative**, valutate dal codice; le regole di Google sono da verificare.

a) **Il client dell'autore dentro Missiva, come per Microsoft.**
   - **Codice**: una costante e il ripiego in `Client()` (GoogleAuth.cs:152-154). I campi delle Impostazioni restano per
     chi vuole il proprio progetto.
   - **Il secret**: per un client desktop Google non lo considera segreto (lo dice già Store.cs:24-25).
   - **Il progetto dell'autore** deve essere "In produzione". Senza verifica:
     - Google mostra l'avviso "app non verificata", come già oggi per i progetti personali (LEGGIMI.md:123-125);
     - per quanto ricordo, limita a 100 gli utenti di un'app non verificata con ambiti riservati. Bastano per gli
       amici; da verificare.
   - **Rischio**: tutti dipendono da quel progetto. Se Google lo sospende, o l'autore lo chiude, si fermano tutti insieme.
   - **Ambiti**: quelli in più (calendario, contatti, modifica dei contatti) devono essere nella schermata di consenso;
     per il progetto dell'autore lo sono (CLAUDE.md:829).
   - **Verifica**: quella vera (marchio e CASA per l'ambito riservato, CLAUDE.md:230-233) serve solo oltre il tetto.

b) **Gmail come "Altro account" IMAP, con una password per le app.**
   - **Oggi**: si può già fare a mano.
     - Cambia tipo, Altro account (AccountWindow.xaml:69 e :58). `OnChooseImap` (AccountWindow.xaml.cs:54-58) controlla
       solo che l'indirizzo sia valido.
     - La configurazione automatica trova i server dal database dei provider di Thunderbird (MailAutoconfig.cs:41).
     - AccountWindow.xaml.cs:463 conta già il server SMTP di Gmail fra quelli che salvano da sé la Posta inviata.
   - **Cosa si perde**: tutto quello che fa GmailProvider, cioè:
     - la ricerca con la sintassi di Gmail;
     - le etichette;
     - le conversazioni;
     - Annulla con le etichette;
     - calendario e contatti.
   - **Requisiti e rischi**: serve la verifica in due passaggi, e Google potrebbe togliere le password per le app (da
     verificare).
   - **Giudizio**: un ripiego, non la strada principale.

c) **Una procedura guidata dentro Missiva per il progetto di ognuno.** Toglie gli errori ma non i sette passi; utile solo
   se a) non si può fare.

**Proposta**
- a), e b) spiegato in Aggiungi account come ripiego: "Se l'accesso con Google non va, usa una password per le app".
- **Stima**:
  - a) 2-4 ore di codice, più il lavoro dell'autore sulla console di Google (pubblicazione, ambiti);
  - b) mezza giornata, per farne una scelta esplicita con le istruzioni.
- **Dipendenze**:
  - il progetto dell'autore in produzione;
  - privacy e termini sul sito, che esistono già (CLAUDE.md:209-213).

### 2. Integrazione con Windows

#### F-06 Aprire file .eml, .ics e .vcf con Missiva

**Dove**
- installa.ps1:72-88 registra solo i link mailto:.
- Program.cs:59 e App.cs:99 leggono dalla riga di comando solo i link mailto:.
- `--eml` esiste solo in Debug: MainWindow.xaml.cs:167 sta nel blocco `#if DEBUG` che va da :147 a :364.

**Scenario**
- Un collega manda un .eml dentro uno zip, un sito fa scaricare un biglietto .ics, un telefono esporta la rubrica in .vcf.
- Il doppio clic in Esplora file non apre Missiva.
- `Missiva.exe file.eml` porta solo Missiva in primo piano: Program.cs:62 manda "apri".

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO. Cercati FileTypeAssociations, OpenWithProgids, FileAssociations e i tre suffissi in installa.ps1,
Program.cs e App.cs: niente.

**Proposta**
- **Registrazione**: ProgID per utente (HKCU) `Missiva.Eml`, `Missiva.Ics` e `Missiva.Vcf`:
  - in OpenWithProgids sotto i tre suffissi, così Missiva compare in "Apri con" senza prendersi il predefinito;
  - in `Capabilities\FileAssociations`, così si può scegliere come app predefinita.
- **Apertura**: un percorso di file sulla riga di comando passa alla copia aperta sul canale con nome, come i link mailto
  (`Program.SendToRunning`). Poi:
  - .eml si apre con `OpenEmlWindow`, che c'è già;
  - .ics con la scheda dell'invito, o con "Aggiungi al calendario" (F-35);
  - .vcf con Importa di Persone (PeopleView.cs:709-712).
- **Stima**: 1-2 giorni.

#### F-07 Simple MAPI ("Invia a, Destinatario posta" e "Invia come allegato" di Office)

**Dove**: nessun punto. installa.ps1:79-88 non scrive `Software\Clients\Mail`, e Missiva non ha una DLL nativa.

**Scenario**: con Missiva come unica app di posta, in questi casi non succede nulla, o compare un errore MAPI:
- in Word, "Invia come allegato PDF";
- in Esplora file, "Invia a, Destinatario posta";
- "Invia per e-mail" da un gestionale o dallo scanner.

**Importanza**: l'autore Media · amici Media (gli uffici lo usano spesso).

**Stato**
- CONFERMATO che manca: cercati MAPISendMail, mapi32, `Clients\Mail`, MAPI, DllExport e UnmanagedCallersOnly: niente.
- SOSPETTO quanto si possa fare senza amministratore: dove Windows cerchi il client MAPI predefinito per il solo utente è
  da verificare.

**Proposta**
- Una piccola DLL nativa (C++, oppure .NET NativeAOT con UnmanagedCallersOnly) che esporta MAPISendMail e MAPISendMailW.
- La DLL scrive destinatari, oggetto, testo ed elenco dei file in un file temporaneo e lo passa a Missiva sul canale con
  nome. Missiva apre Nuovo messaggio con gli allegati.
- Due DLL, a 32 e a 64 bit (Office a 32 bit è ancora comune), registrate sotto `Clients\Mail\Missiva`.
- **Stima**: 3-5 giorni, con le prove su Office.

#### F-08 Condividi di Windows

**Dove**: nessun punto. Missiva non ha un pacchetto (nessun appxmanifest).

**Scenario**: in Foto o in Esplora file, "Condividi" non propone Missiva. Per mandare una foto bisogna aprire Missiva, Nuovo
messaggio, Allega.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati ShareTarget, DataTransferManager e Condividi: solo un parametro di CreateFileW
(Safety.cs:56).

**Proposta**
- Serve l'identità di pacchetto: un MSIX, o un pacchetto "sparse" accanto all'exe. Si fa dopo F-03.
- **Stima**: 2-3 giorni, una volta che c'è l'MSIX.

#### F-09 Elenco di salto sulla barra delle applicazioni

**Dove**
- La barra delle applicazioni mostra solo il numero dei non letti (MainWindow.xaml.cs:1233-1235).
- "Nuovo messaggio" c'è solo nel menu dell'icona vicino all'orologio (App.cs:177).

**Scenario**: tasto destro sull'icona nella barra delle applicazioni: niente "Nuovo messaggio" né "Calendario".

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati JumpList, JumpTask e ThumbButton: niente.

**Proposta**
- Un `System.Windows.Shell.JumpList` con "Nuovo messaggio", "Nuovo evento" e "Calendario".
- Ognuno avvia Missiva.exe con un argomento, che passa alla copia aperta sul canale con nome come "apri".
- **Stima**: 2-4 ore.

#### F-10 Trascinare allegati e messaggi fuori, in Esplora file

**Dove**
- MainWindow.xaml.cs:1391: il trascinamento dei messaggi porta un tipo privato, `MessageDrag`, che capiscono solo le
  cartelle di Missiva.
- Gli allegati nel riquadro sono pulsanti HTML dentro WebView2 (ReadingPage.cs:349).

**Scenario**
- Trascinare la fattura PDF dal messaggio al desktop, o un messaggio in una cartella d'archivio come .eml.
- Oggi serve Salva e la scelta della cartella; per il messaggio, Salva con nome.

**Importanza**: l'autore Media · amici Bassa.

**Stato**: CONFERMATO.
- Cercati FileDrop come sorgente, FileGroupDescriptor, FileContents, VirtualFile e SetFileDropList: niente.
- In ReadingView, ReadingPage e AttachmentPreview nessun trascinamento.

**Proposta**
- **Messaggi**: aggiungere al DataObject di :1391 i formati FileGroupDescriptorW e FileContents (file virtuali). Il
  contenuto .eml si chiede con `GetRawAsync` solo al rilascio.
- **Allegati**: WebView2 non trascina file verso fuori. La via semplice è trascinare dall'anteprima dell'allegato
  (AttachmentPreview, che è WPF).
- **Stima**: messaggi 1-2 giorni; allegati dall'anteprima 1 giorno.

### 3. Posta

#### F-11 Annulla iscrizione (List-Unsubscribe)

**Dove**
- **Graph**: le intestazioni arrivano già con il corpo (`internetMessageHeaders`, GraphProvider.cs:654), ma se ne tiene
  solo Authentication-Results (GraphProvider.cs:754-755).
- **Gmail e IMAP**:
  - i messaggi sotto 300 KB arrivano interi;
  - per quelli più grandi si chiede solo Authentication-Results (SenderCheck.cs:135).
- **Il dato** andrebbe accanto a `MessageBody.AuthenticationResults` (Models.cs:252).
- **Il pulsante** andrebbe nella pagina (ReadingPage.cs, intestazione o `Warnings`).

**Scenario**: per smettere di ricevere una newsletter bisogna aprirla, cercare il link in fondo e passare dal browser. Il
nuovo Outlook e Gmail mettono "Annulla iscrizione" in cima al messaggio.

**Importanza**: l'autore Alta · amici Media.

**Stato**: CONFERMATO. Cercati List-Unsubscribe, unsubscribe, "annulla iscrizione" e disiscri in Core e UI: niente.

**Proposta**
- **Lettura**: List-Unsubscribe e List-Unsubscribe-Post.
- **Quando mostrare "Annulla iscrizione"**:
  - solo se il mittente supera i controlli di SenderCheck (niente fascia rossa);
  - mai nella Posta indesiderata.
- **Cosa fa**:
  - con List-Unsubscribe-Post (un clic, RFC 8058): una POST HTTPS fatta da Missiva (non dal riquadro), dopo una conferma
    con il dominio in vista;
  - con un solo link https: lo apre nel browser;
  - con un solo mailto: prepara una scrittura da inviare a mano.
- **Dopo**: proporre una regola "i prossimi nel Cestino".
- **Stima**: 1-2 giorni.
- **Dipendenze**: nessun permesso nuovo.

#### F-12 Blocca mittente e segnala phishing

**Dove**
- MainWindow.xaml.cs:3421-3431: "Segnala" sposta nella Posta indesiderata, o riporta nella Posta in arrivo.
- Per Graph è uno spostamento come gli altri (GraphProvider.cs:865).
- Voce del menu: ContextMenus.cs:91.

**Scenario**
- Lo stesso mittente insistente torna ogni settimana.
- Un phishing che imita la banca va segnalato come tale.
- Oggi l'unica strada è creare a mano una regola "da questo indirizzo: elimina".

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO, A METÀ.
- C'è "Segnala come posta indesiderata".
- Cercati Blocca, blockedSender, SafeSender, reportMessage, markAsJunk e phish: niente. `BlockedSenders()` in
  ReadingView.xaml.cs:856 riguarda le immagini, non i mittenti.
- **Trovato leggendo**: la scelta delle immagini esterne non guarda la cartella (ReadingView.xaml.cs:680). Un messaggio
  finito nella Posta indesiderata, con il dominio di un mittente consentito (vero o falsificato), carica le immagini da
  Internet.

**Proposta**
- **"Blocca mittente"** nel sottomenu Segnala:
  - crea una regola "da questo indirizzo: elimina": di Outlook per Microsoft, di Missiva per Gmail e IMAP (le regole ci
    sono già);
  - la lista si vede in Impostazioni, Regole.
- **"Segnala phishing"**: sposta nella Posta indesiderata e, per gli account di lavoro, propone l'inoltro come allegato
  (F-16) all'indirizzo indicato dall'amministratore.
- **Immagini**: sempre bloccate nella Posta indesiderata (2 ore).
- **Stima**: 1 giorno in tutto.
- **Dipendenze**: per Outlook, il consenso alle impostazioni della casella, già usato dalle regole. Se Graph v1.0 abbia
  un'azione di segnalazione è da verificare.

#### F-13 Conferme di lettura chieste da chi scrive

**Dove**
- Missiva chiede le conferme per i messaggi che invia (MimeOptions.cs:48, GraphProvider.cs:1499, ComposeWindow.xaml.cs:1747).
- Non legge Disposition-Notification-To nei messaggi ricevuti.
- `$MDNSent` serve solo a escludere quella parola chiave dalle categorie (ImapProvider.cs:502).
- **Agganci**: il segno di letto (`MarkRead`, ReadingView.xaml.cs:720) e una riga in `ReadingPage.Warnings`.

**Scenario**
- Un ufficio pubblico o un cliente chiede la conferma di lettura. Outlook chiede "Inviare la conferma?"; Missiva tace.
- Il mittente pensa che il messaggio non sia stato letto.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**
- CONFERMATO per Gmail e IMAP: cercati Disposition-Notification, isReadReceiptRequested, MDN e
  message/disposition-notification fra i messaggi ricevuti.
- SOSPETTO per Microsoft: se Exchange mandi da sé la conferma quando Missiva segna letto non si vede dal codice.

**Proposta**
- Una riga "Il mittente ha chiesto una conferma di lettura", con Invia e Non inviare.
- Mai automatica, per riservatezza.
- Una volta per messaggio (`$MDNSent` per IMAP).
- La conferma è un message/disposition-notification costruito con MimeKit.
- **Stima**: 1 giorno.

#### F-14 Avviso di allegato dimenticato

**Dove**: ComposeWindow.xaml.cs:1462-1472. Prima dell'invio si controllano solo i destinatari e l'oggetto.

**Scenario**: "Ti mando in allegato il preventivo", Invia, e il preventivo non c'è.

**Importanza**: l'autore Media · amici Alta.

**Stato**: CONFERMATO. Cercati allegat, "in allegato", allego, attached e dimentica nel percorso di invio: niente.

**Proposta**
- Prima di :1472, l'avviso scatta quando:
  - il testo nuovo (senza firma e citazione) contiene allegat, allego, alleghiamo, "in allegato", accluso o attached;
  - e non ci sono né file né allegati inoltrati.
- Il controllo è un `Dialog.Ask` "Hai scritto di un allegato, ma non c'è", con "Invia comunque" e "Allega".
- **Stima**: 2-4 ore.

#### F-15 Limiti di dimensione, e allegati oltre 3 MB con Microsoft

**Dove**
- GraphProvider.cs:1454-1455: "Per ora allegati e immagini possono pesare al massimo 3 MB in tutto".
  - Sta dentro `WriteDraftAsync`, quindi scatta al salvataggio automatico e all'invio, dopo l'attesa di Annulla invio.
  - I file si leggono interi in memoria (GraphProvider.cs:1451).
- Le schede dei file aggiunti non mostrano la dimensione (ComposeWindow.xaml.cs:1241-1244).
- Per SMTP nessun controllo della dimensione.

**Scenario**
- Con un account Microsoft personale (Outlook) un PDF di 5 MB o tre foto del telefono non partono in nessun modo, e
  l'errore arriva dopo Invia.
- Con Gmail un file di 30 MB viene rifiutato dal server con il testo grezzo dell'errore.

**Importanza**: l'autore Alta · amici Alta.

**Stato**: CONFERMATO.
- Cercati createUploadSession e uploadSession: niente.
- Cercati MaxSize, 552 e "troppo grand": solo i limiti di PEC, .p7m e anteprima.
- Il limite è dichiarato in LEGGIMI.md:427, ma senza una strada alternativa.

**Proposta**
- **Graph**: sopra 3 MB, sessione di caricamento a pezzi (createUploadSession sull'allegato della bozza). Usa lo stesso
  permesso Mail.ReadWrite. Il limite effettivo degli account personali è da verificare.
- **Prima di inviare**: un avviso già quando si aggiunge il file (`AddFiles`), con il totale e il limite dell'account; la
  dimensione su ogni scheda.
- **SMTP**: leggere il limite che il server dichiara (SIZE, che MailKit espone dopo il collegamento) e spiegare in italiano
  un rifiuto 552.
- **Stima**: sessione di caricamento 1-2 giorni; avvisi 4 ore.
- **Dipendenze**: nessun permesso nuovo.

#### F-16 Inoltra come allegato e reindirizza

**Dove**
- `ComposeMode` ha solo New, Reply, ReplyAll e Forward (Models.cs:271).
- I byte del messaggio ci sono già: `GetRawAsync`, Models.cs:495.
- Il menu dell'inoltro è a MainWindow.xaml.cs:3389 e ContextMenus.cs:78.
- Le regole di Outlook conoscono le due cose solo come azioni (Rules.cs:61-62).

**Scenario**: l'ufficio informatico chiede di inoltrare il messaggio sospetto "come allegato", per vederne le intestazioni.

**Importanza**: l'autore Bassa · amici Bassa (Media per chi lavora in un ufficio).

**Stato**: CONFERMATO. Cercati ForwardAsAttachment, "come allegato", Redirect, Resent- e Reindirizza nella scrittura:
niente.

**Proposta**
- **"Inoltra come allegato"**: Nuovo messaggio con il .eml di `GetRawAsync` fra gli allegati. Per Graph è da verificare se
  basta un allegato di file .eml.
- **Reindirizza** (Resent-From, Resent-To) si fa solo per SMTP; per Graph non c'è, meglio lasciarlo fuori.
- **Stima**: mezza giornata la prima; 1 giorno la seconda.

#### F-17 Schede di Gmail (Principale, Social, Promozioni)

**Dove**
- GmailProvider.cs:102 (`SupportsFocused => false`): Evidenziata e Altra esistono solo per Microsoft (GraphProvider.cs:111).
- Le notifiche di Gmail prendono tutti i non letti della Posta in arrivo, senza guardare la categoria
  (GmailProvider.cs:540-550).
- Un filtro si aggancerebbe come `has:attachment` in GmailProvider.cs:439.

**Scenario**
- Chi in Gmail usa le schede vede in Missiva promozioni e social mescolate alla posta vera.
- Riceve una notifica per ogni promozione, mentre l'app di Gmail avvisa di solito solo per Principale.

**Importanza**: l'autore Media · amici Media.

**Stato**
- CONFERMATO nel codice: cercati category:, CATEGORY_, Promozioni e "Social": niente.
- SOSPETTO quanto pesi: dipende dalle schede attive nell'account.

**Proposta**
- Per Gmail la stessa interfaccia di Evidenziata e Altra: Principale = X-GM-RAW `category:primary`, Altra = il resto.
- Un'impostazione per account "Notifiche solo per Principale".
- **Stima**: 1-2 giorni.
- **Dipendenze**: nessuna (X-GM-RAW c'è già).

#### F-18 Conversazioni per gli account IMAP

**Dove**
- ImapProvider.cs:906-907: `ConversationId` vuoto. ImapProvider.cs:918-919: la conversazione è il messaggio stesso.
- `ListItems` (ImapProvider.cs:25-26) non chiede References.
- Il raggruppamento dipende da `ConversationId` (ViewModels.cs:964, ReadingView.xaml.cs:292).

**Scenario**: con una casella su dominio proprio, o con la PEC, uno scambio di dieci messaggi con un cliente sono dieci righe
sparse, e il riquadro non mostra la storia.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO. È dichiarato in LEGGIMI.md:418 come limite, non come lavoro da fare. Cercati THREAD,
ThreadingAlgorithm e MessageThreader: niente.

**Proposta**
- Chiedere References e In-Reply-To con l'elenco, come le intestazioni dell'importanza (MimeOptions.cs:15).
- Calcolare un `ConversationId` locale: il primo Message-Id della catena, oppure l'oggetto ripulito come ripiego.
- Se il server ha THREAD=REFERENCES, usare quello.
- Le risposte che stanno in Posta inviata si trovano con una SEARCH HEADER sul Message-Id, per la sola conversazione aperta.
- **Stima**: 2-4 giorni.

#### F-19 Ordinamenti di Gmail e IMAP oltre la data

**Dove**
- GmailProvider.cs:368 e ImapProvider.cs:514 ordinano solo per data.
- `SortBy` non ha la dimensione (Models.cs:209).
- Il suggerimento di MainWindow.xaml.cs:1947 dice "Gmail" anche per gli account IMAP e per i .eml.

**Scenario**: trovare i messaggi più pesanti per liberare spazio, o ordinare per mittente una cartella di fatture.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO (`ImapCapabilities.Sort` non è usato).

**Proposta**
- Con la capacità SORT del server (Dovecot e molti hosting l'hanno; Gmail no): ordinare per mittente, oggetto e dimensione.
- Correggere il suggerimento per IMAP.
- **Stima**: 1 giorno.

#### F-20 Ricerche salvate

**Dove**
- Store.cs:97 e :104-105 tengono solo le ultime 10 ricerche recenti.
- I Preferiti (Store.cs:198) potrebbero tenerle.

**Scenario**: ogni mese la stessa ricerca, "da:commercialista ha:allegati dal:1/9/2026", va riscritta, o sperare che sia
ancora fra le recenti.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati SavedSearch, "ricerche salvate" e SearchFolder: solo `SearchFolderAsync`, che cerca dentro
una cartella.

**Proposta**
- "Salva ricerca" accanto a Filtri. Si salva il testo di SearchTerms, cifrato come le ricerche recenti.
- Nei Preferiti compare come una cartella che rifà la ricerca.
- **Stima**: 1-2 giorni.

#### F-21 Ignora conversazione

**Dove**: nessun punto. Si aggancerebbe al menu del messaggio (ContextMenus.cs:72-95) e a `Rules.ApplyToNewAsync`
(Rules.cs:556), per ConversationId.

**Scenario**: una catena "rispondi a tutti" di trenta persone su un argomento che non interessa: ogni risposta è una notifica.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati Ignora, mute, silenzia e Muted: niente.

**Proposta**
- I nuovi messaggi di quella conversazione vanno nel Cestino (come fa Outlook), oppure restano letti senza notifica (come
  fa Gmail).
- Si fa con una regola locale per ConversationId: solo Gmail e Microsoft; IMAP dopo F-18.
- **Stima**: 1 giorno.

#### F-22 Firma e cifratura in uscita (S/MIME, PGP)

**Dove**
- L'unica parte S/MIME è la verifica dei messaggi ricevuti (Pec.cs:212-216).
- Nessuna decifratura di application/pkcs7-mime.
- Il MIME in uscita si costruisce in GmailProvider.cs:1459, ImapProvider.cs:1524 e MimeOptions.cs:41.
- Per Graph c'è già l'invio in MIME (GraphProvider.cs:954-966).

**Scenario**: un professionista con un certificato di firma, o un'azienda con S/MIME interno, non può firmare né leggere un
messaggio cifrato.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**
- CONFERMATO: cercati MultipartSigned.Create, ApplicationPkcs7Mime, EnvelopedCms, OpenPgp ed Encrypt nel percorso d'invio:
  si trovano solo nei test.
- SOSPETTO come appare oggi un messaggio cifrato ricevuto: non l'ho seguito fino al riquadro.

**Proposta**
- Prima la lettura: decifrare con il certificato nello store di Windows (WindowsSecureMimeContext di MimeKit).
- Poi la firma in uscita.
- PGP solo se qualcuno lo chiede.
- **Stima**: lettura 2 giorni; firma e cifratura 3-5 giorni.

#### F-23 Ricevute PEC collegate al messaggio inviato

**Dove**
- Pec.cs:159 legge X-Riferimento-Message-ID in `PecInfo.Reference` (Pec.cs:29). Pec.cs:34 definisce `IsReceipt`. Nessuno
  dei due è letto altrove.
- La PEC si riconosce solo aprendo il messaggio, non nell'elenco.

**Scenario**
- Ogni invio PEC genera due ricevute per destinatario: la Posta in arrivo si riempie.
- Per sapere se una diffida è stata consegnata bisogna cercare la ricevuta giusta.

**Importanza**: l'autore Media (SOSPETTO: se usa una casella PEC; LEGGIMI.md:78-79 la cita come esempio) · amici Media (per i
professionisti).

**Stato**: CONFERMATO (è anche la riga 1-14 della Parte 1).

**Proposta**
- Chiedere con l'elenco X-Ricevuta e X-Riferimento-Message-ID (MimeOptions.cs:15).
- Collegare le ricevute al messaggio di Posta inviata con lo stesso Message-Id.
- Sulla riga inviata: "Accettata, consegnata a 2 su 3".
- Nella Posta in arrivo: "Nascondi ricevute".
- **Stima**: 2-3 giorni.

### 4. Account

#### F-24 Profili pronti per iCloud, Yahoo, Libero e simili

**Dove**
- MailAutoconfig.cs:41 e :53: il database dei provider di Thunderbird trova i server.
- MailAutoconfig.cs:173-176 legge solo i server e salta le istruzioni che lo stesso database contiene.
- L'unico elenco fisso è quello dei gestori PEC (MailAutoconfig.cs:67-70).
- L'errore di accesso è generico (ImapProvider.cs:98-100).
- AccountWindow.xaml:61 cita come esempi solo hosting di domini propri.

**Scenario**
- Un'amica con iCloud scrive indirizzo e password dell'ID Apple, e il server rifiuta.
- Missiva dice "con la verifica in due passaggi serve una password per le app". Non dice dove crearla, né che per iCloud
  serve sempre.
- Per Libero o Yahoo vale lo stesso, con regole diverse.

**Importanza**: l'autore Bassa · amici Alta.

**Stato**: CONFERMATO. Cercati icloud, yahoo, libero, "password per le app" e "app password": i domini compaiono solo
per loghi e immagini (Store.cs:136, SenderLogos.cs:51-55).

**Proposta**
- Una piccola tabella (iCloud, Yahoo, Libero, Virgilio, Tiscali, GMX, Outlook e Gmail via IMAP) con:
  - il nome del servizio;
  - "serve una password per le app" e dove si crea, come testo;
  - l'eventuale opzione "accesso da programmi di posta" da accendere.
- Il messaggio si mostra prima del campo password e dopo un rifiuto.
- **Stima**: mezza giornata, più il controllo delle procedure attuali di ogni servizio.

#### F-25 Exchange in sede (EWS)

**Dove**: `AccountKind` ha solo Microsoft, Gmail e Imap (Models.cs:3). AccountWindow.xaml:61 propone "Exchange con IMAP".

**Scenario**: un amico in un ente con Exchange in sede, senza IMAP, non può aggiungere la casella.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati EWS, exchange.asmx, autodiscover e ActiveSync: niente.

**Proposta**
- Non farlo ora: EWS è un protocollo grande e in calo (2-3 settimane).
- Nella guida: "Exchange in sede funziona solo se l'ente ha acceso IMAP".

#### F-26 Altri indirizzi scoperti da soli

**Dove**
- Gli indirizzi si scrivono a mano (ManageAccountWindow.cs:286). Per Gmail c'è una nota a :219.
- Gli ambiti chiesti sono solo:
  - Google: posta, openid, email e profile (GoogleAuth.cs:23-24);
  - Microsoft: User.Read, Mail.ReadWrite e Mail.Send (MicrosoftAuth.cs:17).

**Scenario**: chi ha in Gmail tre indirizzi "Invia messaggio come" deve riscriverli uno per uno, e un errore di battitura
manda il messaggio con il mittente riscritto da Gmail.

**Importanza**: l'autore Media (SOSPETTO: se usa alias) · amici Bassa.

**Stato**: CONFERMATO. Cercati sendAs, proxyAddresses e gmail.settings: niente.

**Proposta**
- Un pulsante "Trova gli indirizzi" in Gestisci account:
  - **Gmail**: con `users.settings.sendAs.list` della Gmail API, che è già attiva nei progetti (LEGGIMI.md:102). Che basti
    l'ambito della posta è da verificare.
  - **Microsoft 365**: con `me?$select=proxyAddresses`, da verificare con User.Read.
  - **Account Microsoft personali e IMAP**: restano a mano.
- **Stima**: 1 giorno.

#### F-27 Foto dei contatti

**Dove**
- `Contact` non ha una foto (Contacts.cs:15-50).
- Google: PersonFields senza `photos` (GoogleContacts.cs:17).
- Graph: GraphFields senza foto, e nessuna richiesta /photo (Contacts.cs:550).
- vCard: PHOTO viene saltato (Contacts.cs:318).
- Gli avatar sono solo iniziali o logo (PersonCard.cs:52-53, ViewModels.cs:1265).

**Scenario**: in Persone e nella scheda del mittente amici e parenti hanno le iniziali, mentre il telefono mostra le loro foto.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercato photo in Core e UI: solo il salto della vCard.

**Proposta**
- **Da dove**:
  - dai contatti di Google, con `photos` e l'ambito dei contatti già dato;
  - da quelli di Outlook, con `me/contacts/{id}/photo/$value` e Contacts.ReadWrite.
- **Come**: salvate cifrate come i loghi, e usate nell'avatar quando l'indirizzo è di un contatto.
- **Colleghi**: le loro foto vorrebbero un permesso nuovo (da verificare).
- **Stima**: 1-2 giorni.

#### F-28 Risposte automatiche e filtri sul server per Gmail

**Dove**
- `HasAutoReplies` e `HasServerRules` sono falsi di base (Models.cs:615 e :629); sono veri solo in Graph
  (GraphProvider.cs:1015 e :1038).
- Le regole di Gmail girano solo con Missiva aperta (Rules.cs:558).
- AutoRepliesWindow ha già la variante "un messaggio solo", usata per gli account personali.

**Scenario**
- Per la risposta automatica delle ferie su Gmail bisogna andare su Gmail sul web.
- Le regole di Missiva per Gmail non agiscono con il PC spento.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO. Cercati vacation, settings.basic, sieve e managesieve: niente.

**Proposta**
- **Permesso**: un ambito in più (`gmail.settings.basic`), chiesto solo al clic. Credo sia riservato come quello della
  posta, ma è da verificare. Va aggiunto alla schermata di consenso del progetto.
- **Risposte automatiche**: getVacation e updateVacation nella stessa AutoRepliesWindow.
- **Filtri**: `users.settings.filters` per le regole semplici (mittente, oggetto: sposta, etichetta, elimina) nella stessa
  RulesView.
- **Altri IMAP**: ManageSieve dove il server lo offre, dopo.
- **Stima**: risposte automatiche 1-2 giorni; filtri 2-3 giorni.
- **Dipendenze**: l'ambito Google nuovo, e la Gmail API attiva (LEGGIMI la fa già attivare).

#### F-29 Copia di sicurezza ed esportazione delle impostazioni

**Dove**
- Impostazioni non ha Esporta né Importa (SettingsWindow.cs:94-96).
- Tutto è cifrato con DPAPI per l'utente di Windows (Store.cs:490-494).
- Su un altro PC i testi cifrati diventano vuoti senza avviso (Store.cs:400-403).

**Scenario**
- Un amico cambia PC o reinstalla Windows, e perde:
  - firme e modelli;
  - le regole di Missiva;
  - categorie e Preferiti;
  - per Gmail senza i contatti di Google, o per IMAP, tutti i contatti di Missiva (`contatti.dat`, che esiste solo su quel
    PC).
- Copiare la cartella dei dati non basta, e Missiva non lo dice.

**Importanza**: l'autore Media · amici Alta.

**Stato**: CONFERMATO. Cercati Esporta, Importa, backup, "copia di sicurezza" ed export in UI: solo vCard e CSV di Persone
(PeopleView.cs:60).

**Proposta**
- Impostazioni, Generali, "Copia di sicurezza": un file con impostazioni, firme, modelli, regole e contatti di Missiva.
  - È cifrato con una password scelta dall'utente (AES-GCM, chiave da PBKDF2).
  - Non contiene token né password degli account: dopo l'importazione si rientra in ogni account.
- Importa sul PC nuovo.
- Un promemoria al mese se non c'è una copia recente.
- **Stima**: 2-3 giorni.

#### F-30 Migrazione da Outlook

**Dove**: il primo passo di Aggiungi account (AccountWindow.xaml:24-27). In Persone esiste già Importa CSV di Outlook
(PeopleView.cs:712).

**Scenario**: chi arriva dall'Outlook classico ha firme, account e magari archivi .pst, e deve rifare tutto a mano.

**Importanza**: l'autore Bassa · amici Media.

**Stato**: CONFERMATO. Cercati pst, "profilo di Outlook", mbox e "importa da": niente.

**Proposta**
- **Si fa**:
  - importare le firme dell'Outlook classico (i file .htm della cartella delle firme dell'utente, con HtmlToFlow);
  - proporre gli indirizzi dei suoi account in Aggiungi account.
- **Non si fa**: gli archivi .pst, perché servirebbero una libreria e il caricamento dei messaggi su un server.
- **Stima**: firme 1 giorno.

### 5. Calendario e Persone

#### F-31 Agenda di oggi accanto alla posta (dichiarata, riga 1-08)

**Dove**
- La griglia `MainArea` (MainWindow.xaml:354-362).
- Gli eventi di oggi e di domani sono già in `upcoming` (MainWindow.Calendar.cs:106, riempito ogni mezz'ora a :143-151).

**Scenario**: mentre risponde a un cliente, l'autore vuole sapere se alle 15 è libero. Deve passare al Calendario, che toglie la
posta dallo schermo.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO. Cercati Agenda, "Il mio giorno" e "My Day": niente.

**Proposta**
- Una colonna a destra del riquadro di lettura con gli eventi di oggi e domani, chiusa di base.
- Deve essere una colonna vera e non un pannello sopra la pagina, perché WebView2 non si lascia coprire.
- **Stima**: 2-3 giorni.

#### F-32 CalDAV e CardDAV

**Dove**: CalendarView.cs:277 ("arriverà più avanti"); Models.cs:549-551 e :589 (calendario e contatti falsi per IMAP).

**Scenario**: un'amica con iCloud, o con Fastmail, o con un Nextcloud, aggiunge la posta, ma calendario e contatti restano
vuoti.

**Importanza**: l'autore Bassa (SOSPETTO: dipende dai suoi altri account) · amici Media.

**Stato**: CONFERMATO. Cercati caldav, carddav, PROPFIND, REPORT e well-known: solo note.

**Proposta**
- **Scoperta**: well-known e SRV, come `FromSrvAsync` (MailAutoconfig.cs:195).
- **Lettura**: elenco dei calendari, eventi con calendar-query ed espansione delle ricorrenze chiesta al server, lettura con
  il parser di Invitation. La scrittura viene dopo.
- **Password**: la stessa della posta (per iCloud la password per le app).
- **Stima**: lettura 1-2 settimane; scrittura altre 1-2.

#### F-33 Calendari condivisi e di altre persone

**Dove**
- Graph legge solo `me/calendars` (GraphProvider.cs:1186): niente calendarGroups né users/{x}/calendar, e il solo
  Calendars.ReadWrite (MicrosoftAuth.cs:269).
- Google mostra già i calendari condivisi che l'utente ha nel suo elenco (GmailProvider.Calendar.cs:63).
- La casella condivisa è esclusa (GraphProvider.cs:1172-1173).

**Scenario**: in famiglia un calendario "Casa" condiviso su Outlook; in ufficio il calendario di un collega o di una sala.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**
- CONFERMATO che manca "Aggiungi calendario".
- SOSPETTO per Microsoft: non si sa se `me/calendars` dia anche i calendari condivisi aggiunti in Outlook. Va provato con
  un account che ne ha uno.

**Proposta**
- Leggere anche `me/calendarGroups/{id}/calendars`.
- Per i calendari di altre persone, Calendars.Read.Shared: un permesso da aggiungere alla registrazione dell'app, e per gli
  enti un nuovo consenso.
- **Stima**: 1-2 giorni.

#### F-34 Google Meet (dichiarato, riga 1-05)

**Dove**
- EventWindow.cs:266-268: Teams solo con un Tenant.
- `ToGoogle` non manda conferenceData (GoogleCalendar.cs:205-222).
- La POST non ha conferenceDataVersion (GmailProvider.Calendar.cs:122).

**Scenario**: per fissare una videochiamata dal calendario di Gmail bisogna aprire Google Calendar per avere il link Meet.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO.

**Proposta**
- Una casella "Riunione di Google Meet" per i calendari Google: `conferenceData.createRequest` con un requestId casuale e
  `conferenceDataVersion=1`. Nessun ambito nuovo.
- Insieme, correggere la nota dell'annullamento (1-06).
- **Stima**: mezza giornata.

#### F-35 Importa ed esporta .ics

**Dove**
- La scheda PUBLISH non ha pulsanti (ReadingPage.cs:296, Invitation.cs:49).
- `CreateEventAsync` c'è già (Models.cs:561).
- L'.ics allegato ha solo l'icona (FileKinds.cs:23).

**Scenario**: un biglietto del treno, un webinar o la scuola mandano un .ics. Missiva mostra la scheda "Evento" ma non lo
mette nel calendario.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO. Cercati "Aggiungi al calendario", "Importa calendario", ToIcs e SaveIcs: niente.

**Proposta**
- "Aggiungi al calendario" sulla scheda PUBLISH e sugli .ics allegati, con la scelta del calendario come in EventWindow.
- "Esporta in .ics" nei dettagli dell'evento.
- L'importazione di un file .ics intero dopo F-06.
- **Stima**: 1 giorno.

#### F-36 Modifica della ripetizione di una serie

**Dove**
- EventWindow.cs:312-314 nasconde la ripetizione quando si modifica.
- GraphProvider.cs:1253 e GmailProvider.Calendar.cs:149 rifiutano di spostare la serie a un altro giorno.
- `EventFields` non chiede la ricorrenza (Calendar.cs:105-106).

**Scenario**: il corso del martedì passa al giovedì, e bisogna andare su Outlook sul web o su Google Calendar.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO.

**Proposta**
- Leggere la ricorrenza della serie e mostrarla in modifica con gli stessi controlli della creazione.
- PATCH della ricorrenza intera.
- **Stima**: 1-2 giorni.

#### F-37 Posponi i promemoria del calendario

**Dove**
- La notifica del promemoria ha solo testo (MainWindow.Calendar.cs:184-188).
- Quella dei contrassegni ha già Posponi promemoria (MainWindow.Flags.cs:176, gestita in App.cs:935).

**Scenario**: il promemoria della riunione arriva 15 minuti prima, mentre si finisce una mail. Non si può farlo tornare fra
5 minuti, né premere "Partecipa".

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO.

**Proposta**
- Pulsanti "Posponi" (5 minuti, 10 minuti, all'inizio).
- "Partecipa" solo con un link di riunione, con la stessa regola di `Join`.
- Gli argomenti come per i contrassegni.
- **Stima**: mezza giornata.

#### F-38 Attività (To Do, Google Tasks)

**Dove**
- La colonna delle app ha solo Posta, Calendario e Persone (MainWindow.xaml:366-377).
- La cosa più vicina sono i contrassegni con scadenza (MainWindow.Flags.cs).

**Scenario**: chi usa le attività di Outlook o di Google non le vede.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**: CONFERMATO. Cercati todo, Tasks, tasks/v1 e attività: niente.

**Proposta**
- Rimandare: i contrassegni con scadenza coprono il caso più comune.
- Se servisse: Tasks.ReadWrite (un permesso nuovo nella registrazione) e l'ambito tasks di Google.
- **Stima**: 3-5 giorni.

#### F-39 Doppioni dei contatti

**Dove**
- `ContactBook.Add` toglie solo per Id (Contacts.cs:157), e ogni contatto importato ha un Id nuovo (Contacts.cs:19).
- PeopleView.Sections.cs:16 tiene di proposito lo stesso contatto in due account.

**Scenario**: importando due volte la stessa vCard, o il CSV di Gmail e poi quello di Outlook, ogni contatto compare due
volte, anche nei suggerimenti dei destinatari.

**Importanza**: l'autore Bassa · amici Media.

**Stato**: CONFERMATO. Cercati duplicat, doppion, merge, unisci e dedup: niente.

**Proposta**
- All'importazione, saltare o unire i contatti con lo stesso indirizzo, con un riepilogo "12 nuovi, 30 già presenti".
- "Unisci doppioni" solo fra i contatti di Missiva.
- **Stima**: 1 giorno.

#### F-40 Gruppi di contatti di Outlook e di Google (Outlook è dichiarato, riga 1-15)

**Dove**
- GraphProvider.cs:1102 legge solo `me/contacts`.
- GoogleContacts.cs:22 legge solo le connessioni, non i gruppi.
- I gruppi di Missiva sono locali (Contacts.cs:35-40).

**Scenario**: la lista "Genitori classe 3B" fatta in Outlook, o un'etichetta di Google Contatti, non si trova scrivendo
in A.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**
- CONFERMATO che manca.
- SOSPETTO che si possa fare per Outlook: per quanto ricordo Graph v1.0 non espone le liste di distribuzione personali;
  da verificare.

**Proposta**
- **Google**: le etichette (contactGroups, con l'ambito dei contatti già dato) diventano gruppi nei suggerimenti.
- **Outlook**: se Graph non le dà, lasciarle fuori e dirlo nella guida.
- **Stima**: Google 1 giorno.

### 6. Qualità d'uso

#### F-41 Controlli senza nome per l'Assistente vocale

**Dove**
- **Controlli senza nome**:
  - la casella di ricerca (MainWindow.xaml:148; "Cerca" è un testo a parte, :152);
  - la freccia delle sottocartelle (MainWindow.xaml:573-576, solo un suggerimento);
  - la stella dei Preferiti (MainWindow.xaml:631-632);
  - Seleziona tutto (MainWindow.xaml:627-628);
  - la casella di ogni riga (MessageRowElement.cs:263);
  - ID client e secret di Google (AccountWindow.xaml:121 e :123);
  - due tendine delle Impostazioni (SettingsWindow.cs:241 e :317).
- **Stato mancante**: le intestazioni che aprono e chiudono (MainWindow.xaml:404, :448 e :499) non dicono se sono aperte.
- **Annunci mancanti**: nessuno per Annulla, l'invio, la Posta in uscita e gli errori (cercati LiveSetting e
  RaiseNotificationEvent: niente).
- **Fuoco invisibile**: il fuoco della tastiera non si vede su cartelle e voci delle Impostazioni (Styles.xaml:13,
  MainWindow.xaml:687).

**Scenario**
- Con l'Assistente vocale la casella di ricerca si annuncia senza nome.
- Dopo Elimina, la barra "Elemento eliminato, Annulla" non viene letta.

**Importanza**: l'autore Bassa · amici Bassa (Alta per chi usa davvero un lettore di schermo).

**Stato**
- CONFERMATO per i controlli elencati. La base è buona: 258 usi di AutomationProperties.
- Come li legga davvero l'Assistente vocale è da provare.

**Proposta**
- `AutomationProperties.Name` sui controlli elencati.
- Lo stato aperto o chiuso per le intestazioni (ToggleButton o un AutomationPeer).
- LiveSetting Polite sulle barre di Annulla, invio e Posta in uscita e sullo stato dell'account.
- Un FocusVisualStyle sottile per NavItem.
- **Stima**: 1 giorno.

#### F-42 Tastiera e calendario

**Dove**
- **Tastiera in generale**:
  - nessun F6 né guida alle scorciatoie: dei tasti funzione è gestito solo F5 (MainWindow.xaml.cs:4432);
  - nessun tasto di accesso.
- **Calendario**:
  - nessun gestore di tasti in CalendarView.cs;
  - un evento in un'ora scelta si crea solo con il doppio clic (CalendarView.cs:412-419, :371 e :472);
  - lo spostamento si fa solo trascinando (CalendarView.cs:536-549);
  - Ctrl+N apre un nuovo messaggio anche nel calendario (MainWindow.xaml.cs:4433 chiama `OnNew`, :3996, che non guarda la
    vista).
- **Cartelle e conversazioni**: si aprono solo con la freccia; MainWindow non gestisce Key.Left né Key.Right.
- **Suggerimenti della ricerca**: le schede Tutto, Mail, Persone e File non si raggiungono con la tastiera
  (MainWindow.xaml:188-195).
- **Difetto trovato leggendo**: gli eventi di tutto il giorno oltre il terzo non si raggiungono in nessuna vista, né con il
  mouse né con la tastiera.
  - "+N altri" è un testo, non un pulsante (CalendarView.cs:372-375 e :484-485).
  - La vista Giorno esclude gli eventi di tutto il giorno dalla griglia delle ore (Calendar.cs:205).

**Scenario**
- In una settimana con quattro eventi di tutto il giorno nella stessa data (un compleanno, una festività, "ufficio chiuso",
  un viaggio), il quarto non si apre mai.
- Ctrl+N nel calendario apre una mail invece di un evento.

**Importanza**: l'autore Media · amici Media.

**Stato**: CONFERMATO.

**Proposta**
- **Prima di tutto, perché è un difetto**: "+N altri" come pulsante che apre l'elenco della giornata.
- Ctrl+N secondo la vista: Nuovo evento nel calendario, Nuovo contatto in Persone, come nel nuovo Outlook.
- F6 per passare fra cartelle, elenco e riquadro.
- Frecce destra e sinistra per aprire e chiudere sottocartelle e conversazioni.
- Una finestra "Scorciatoie".
- Nel calendario: frecce fra le ore e Invio per creare.
- **Stima**: "+N altri" e Ctrl+N 2-4 ore; il resto 2-3 giorni.

#### F-43 Contrasto elevato

**Dove**
- Nessun controllo del contrasto elevato.
- **Colori fissi**:
  - in `App.SetTextColors` (App.cs:387-415);
  - nella riga disegnata (MessageRowElement.cs:549 e :669, `Brushes.White`);
  - nella pagina (ReadingPage.cs:493-494 per carta e inchiostro; :531-533 per le fasce di avviso);
  - il rosso degli errori #C42B1C, uguale nei due temi (EventWindow.cs:23, AutoRepliesWindow.cs:20, RuleWindow.cs:18,
    ManageAccountWindow.cs:18, RulesView.cs:22, AccountWindow.xaml:180).

**Scenario**
- Con un tema a contrasto elevato di Windows le righe dell'elenco restano nei colori di Missiva, e la pagina del messaggio
  non segue i colori di sistema.
- Se Chromium forza i colori, le fasce rossa e gialla potrebbero non distinguersi più (SOSPETTO, non provato).
- Il rosso degli errori su un fondo scuro come #202020 ha un contrasto di circa 2,9 a 1, sotto il 4,5 a 1 delle linee
  guida. È un calcolo, non una misura.

**Importanza**: l'autore Bassa · amici Bassa.

**Stato**
- CONFERMATO che manca: cercati HighContrast, ForcedColors, forced-colors, prefers-contrast e "contrasto elevato": niente.
- SOSPETTO l'effetto visivo: da provare con un tema a contrasto elevato di Windows.

**Proposta**
- Con il contrasto elevato attivo, SystemColors per fondo, testo, selezione e bordi. La riga disegnata prende già i colori
  dalle risorse.
- Nella pagina, `@media (forced-colors: active)` con bordi al posto dei fondi per le fasce.
- Il rosso degli errori come risorsa per tema.
- **Stima**: 2-3 giorni.

#### F-44 Errori senza via d'uscita

**Dove**: sette casi, ognuno letto nel file indicato.

1. **Impostazioni illeggibili**: Store.cs:461-473 riparte da impostazioni vuote senza dirlo, e il primo salvataggio
   (Store.cs:484-486) sovrascrive il file.
   - Si perdono account, token, firme e regole, senza una copia.
   - SOSPETTO quando succeda: un disco rovinato, una modifica a mano, o un file scritto da una versione più nuova e letto
     da una più vecchia (per esempio tornando a missiva_bozza1).
2. **Invio non riuscito**: ComposeWindow.xaml.cs:1512 mostra l'errore con il solo OK.
   - Anche quando il motivo è la password (ImapProvider.cs:98-100 imposta `Fix = Password`) non c'è "Aggiorna password".
   - Non c'è nemmeno "Salva bozza".
3. **WebView2 assente o non avviabile**: ReadingView.xaml.cs:813-821 scrive un indirizzo come testo grigio, senza link né
   Riprova. Lo dice per qualunque errore d'avvio, anche una cartella bloccata o il disco pieno.
4. **Password IMAP non decifrabile**, per esempio con le impostazioni copiate su un altro PC:
   - `Store.Unprotect` solleva un'eccezione (ImapProvider.cs:55-56);
   - `Explain` non la riconosce (ImapProvider.cs:107);
   - sotto l'account resta "Non raggiungibile:" con il testo di Windows, senza la barra "Aggiorna password";
   - i testi cifrati diventano vuoti in silenzio (Store.cs:400-403).
5. **Errori non gestiti dell'interfaccia**: App.cs:49-53 li scrive solo nel registro. `Store.Save` non ha un try
   (Store.cs:477-488): con il disco pieno un salvataggio fallisce senza che l'utente lo sappia.
6. **Elenco e ricerca non riusciti**: solo una riga di stato, senza Riprova (MainWindow.xaml.cs:2137 e :4241;
   MainWindow.Unified.cs:348).
7. **Testo degli avvisi non copiabile**: Dialog.cs:38 usa un TextBlock. Un amico non può incollare l'errore in un messaggio
   all'autore.

**Scenario**: l'amico apre Missiva e non trova più i suoi account (caso 1), o legge "Messaggio non inviato" con un testo
tecnico e non sa cosa fare (caso 2). L'autore non è lì a leggere il registro.

**Importanza**: l'autore Media · amici Alta.

**Stato**: CONFERMATO, con le righe indicate (per il caso 1 il "quando" è SOSPETTO, come detto).

**Proposta**, punto per punto:
1. A ogni avvio riuscito, una copia `impostazioni.json.bak`. Con un file illeggibile non sovrascrivere: rinominarlo e dirlo.
2. Nella finestra dell'errore d'invio, i pulsanti secondo `MailException.Fix` (Aggiorna password, Accedi di nuovo) e
   "Salva bozza".
3. Link cliccabile e Riprova. Distinguere il runtime assente (GetAvailableBrowserVersionString) dagli altri errori.
4. Riconoscere CryptographicException come Fix Password.
5. Un avviso, una volta per sessione, quando un salvataggio non riesce.
6. Riprova nella riga di stato.
7. Testo selezionabile e pulsante "Copia".

**Stima**: 2-3 giorni in tutto; il punto 1 da solo 2 ore.

#### F-45 Cose regolabili solo dal codice

| Cosa | Dove | In Impostazioni |
|---|---|---|
| Controllo dei nuovi messaggi ogni 2 minuti | App.cs:107 | no |
| Copia degli ultimi 30 giorni, fino a 180 MB, solo Posta in arrivo | Offline.cs:417-418; MainWindow.Offline.cs:271-272 | no |
| Corpi scaricati in anticipo: 10 | MainWindow.xaml.cs:2264 | no |
| Barra di Annulla dopo Elimina e Sposta: 8 s | MainWindow.xaml.cs:3046 | no (Annulla invio sì) |
| Orari di Posponi (8:00, più tardi oggi, fine settimana, settimana prossima) | MainWindow.xaml.cs:3572-3579; SnoozeWindow.cs:32 | no |
| Calendario: settimana dal lunedì, settimana lavorativa da lunedì a venerdì, apertura alle 7:30 | CalendarView.cs:132, :137, :446-448 | no (Impostazioni ha solo Generali, Account e Posta: SettingsWindow.cs:51-53) |
| Promemoria predefinito: 15 minuti | EventWindow.cs:198; Calendar.cs:66 | no |
| Notifiche solo per la Posta in arrivo | Models.cs:512-513 | in parte (per account, e il suono) |
| Formato delle date fisso in italiano, non quello di Windows | ViewModels.cs:582, :892-898 | no |
| Lingua del correttore ortografico: italiano | ComposeWindow.xaml:258 | no |
| Carattere del riquadro di lettura | ReadingPage.cs:480 | no (lo zoom sì) |

**Scenario**
- Un amico che scrive spesso in inglese ha tutto sottolineato in rosso.
- Chi lavora il sabato vuole un'altra settimana lavorativa.
- Chi vuole meno traffico non può allungare i due minuti.

**Importanza**: l'autore Bassa · amici Media.

**Stato**: CONFERMATO.

**Proposta**
- Una pagina Impostazioni, Calendario: primo giorno della settimana, giorni lavorativi, ore, promemoria predefinito.
- La lingua del correttore in Scrittura e risposta.
- 30 giorni e 180 MB in una sezione "Senza rete".
- Il resto può restare fisso.
- **Stima**: 1-2 giorni.

#### F-46 Novità e guida

**Dove**
- Nessuna pagina Informazioni o Novità (SettingsWindow.cs:94-96), e nessuna voce nel menu delle impostazioni
  (MainWindow.xaml.cs:4657-4671).
- installa.ps1 non installa LEGGIMI.md.
- I passi per Google stanno solo in LEGGIMI.md:95-133, e LEGGIMI.md ha frasi superate (Parte 1).

**Scenario**
- Dopo un aggiornamento l'amico non sa cosa è cambiato. Per esempio, che il clic sulla notifica ora apre una finestra a
  parte.
- Quando non sa fare qualcosa, non c'è un "Aiuto".

**Importanza**: l'autore Bassa · amici Media.

**Stato**: CONFERMATO. Cercati Novità, Guida, Aiuto, Help, F1 e LEGGIMI in UI e Core: niente.

**Proposta**
- Una pagina "Novità" mostrata una volta dopo ogni aggiornamento (confrontando la versione salvata nelle impostazioni).
- "Guida", e il tasto F1, aprono LEGGIMI: installato accanto all'exe, o sul sito.
- **Stima**: mezza giornata, più tenere aggiornato LEGGIMI.

---

## Parte 3: funzioni mai provate dal vivo

L'elenco nasce dalle voci "Mai provato", "Mai provate", "Non provati", "Non provato" e "NON provati" di CLAUDE.md: 14
gruppi. È ordinato per danno possibile: prima quello che scrive sul server e agisce anche fuori da Missiva.

### Regole per tutte le prove

- **Quale versione**: quella installata. La versione di prova non scrive nulla sulla casella: va solo nel registro.
- **Cosa usare**: dove si può, un secondo account per scrivere e ricevere, e oggetti di prova (un contatto "Mario Rossi
  Prova", messaggi "Prova 1", "Prova 2").
- **Come procedere**: una cosa alla volta, controllando il risultato sul sito del servizio (Outlook sul web, Gmail, Google
  Contatti) e non solo in Missiva.
- **Se qualcosa va storto**: il registro (Impostazioni, Apri la cartella dei registri) contiene le righe dell'azione.
  Oggetti dei messaggi e titoli degli eventi vi appaiono come "(nascosto)", quindi lo si può mandare.

### P-01 Risposte automatiche di Outlook

**Origine**: CLAUDE.md:1083.

**Danno possibile**: alto. Risposte vere a chiunque scriva, esterni compresi, e non si ritirano. In più, AutoReplies.cs:58
legge un valore sconosciuto del pubblico esterno come "tutti".

**Prima**: Outlook sul web aperto sulla pagina delle risposte automatiche, e un secondo account (per esempio Gmail) per
scrivere.

**Passi**
1. Tasto destro sull'account Microsoft personale (Outlook), Risposte automatiche…. La prima volta, "Consenti l'accesso
   alle impostazioni della casella".
2. Aprire la finestra e premere Salva senza cambiare nulla. In Outlook sul web stato, testo e "solo ai contatti" (o tutti)
   devono essere come prima.
3. Scegliere "Attive solo in questo periodo": da fra 5 minuti a fra 20, testo "Prova di Missiva". Salva.
4. Scrivere dal secondo account:
   - prima dell'inizio: nessuna risposta;
   - durante il periodo: una risposta;
   - dopo la fine: nessuna.
5. Programmarne una dal 24 ottobre alle 22 al 26 ottobre alle 9, a cavallo del cambio d'ora del 25. In Outlook sul web
   devono comparire gli stessi orari. Subito dopo il controllo, passare al punto 6: lasciata così, a quelle date partirebbe
   davvero.
6. Sotto l'account compare la barra. Premere Disattiva: in Outlook sul web risultano spente, con il testo conservato.

**Cosa deve vedere**: orari identici in Missiva e in Outlook sul web, una sola risposta per periodo, la barra che compare e
sparisce.

**Se va male**: spegnerle subito da Outlook sul web.

### P-02 Regole di Outlook

**Origine**: CLAUDE.md:1409.

**Danno possibile**: alto.
- Le regole girano sul server anche a Missiva chiusa, e valgono su tutti i dispositivi.
- Esegui ora sposta o elimina fino a 500 messaggi.
- Su e Giù cambiano le regole una alla volta (RulesView.cs:406-412). Se una richiesta fallisce a metà, compare "Ordine
  delle regole non cambiato", ma sul server l'ordine è già cambiato in parte.

**Prima**
- In Outlook sul web, una cartella "Prova regole".
- Sempre lì, una regola con un'eccezione (per esempio "tranne se l'oggetto contiene X"). È il caso delle condizioni che
  Missiva non mostra.

**Passi**
1. Impostazioni, Posta, Regole, account Microsoft personale, "Consenti l'accesso alle impostazioni della casella".
2. L'elenco deve avere le stesse regole, nello stesso ordine, di Outlook sul web. Quella con l'eccezione deve mostrare le
   "altre condizioni" e non avere Esegui ora.
3. Nuova regola: "mittente = il secondo account", "sposta in Prova regole".
   - In Outlook sul web compare la stessa regola.
   - Con Missiva chiusa (Esci), un messaggio dal secondo account finisce in Prova regole.
4. Nella regola con l'eccezione cambiare solo il nome. In Outlook sul web eccezione e condizioni sono intatte.
5. Su e Giù sulla regola nuova: lo stesso ordine in Outlook sul web, controllato anche se Missiva mostra un errore.
6. Esegui ora solo sulla cartella Prova regole, con 2 o 3 messaggi dentro: la conferma dice il numero giusto.
7. Una regola creata in Outlook sul web che inoltra al secondo account: in Missiva ha il bordo rosso e
   Spegni, e Spegni la spegne anche sul web.
8. Eliminare le regole di prova: spariscono anche da Outlook sul web.

**Cosa deve vedere**: sul web, sempre lo stesso stato che mostra Missiva.

**Se va male**: correggere da Outlook sul web.

### P-03 Contatti di Google: modifica ed eliminazione

**Origine**: CLAUDE.md:838 e :1341.

**Danno possibile**: alto. I cambiamenti arrivano al telefono e a tutti i dispositivi, e un'unione sbagliata può togliere
campi che Missiva non mostra.

**Prima**: in Google Contatti, un contatto "Mario Rossi Prova", di cui fare una schermata, con:
- due indirizzi di posta con etichette;
- cellulare, ufficio, fax e un secondo cellulare;
- indirizzo con via, CAP e città;
- compleanno, nota, sito, un'etichetta e una foto.

**Passi**
1. Persone, sezione dell'account Google: "Consenti la modifica dei contatti", nel browser, con lo stesso account. Dopo,
   l'intestazione dice "Google" senza "sola lettura".
2. Controllare che il calendario di quell'account ci sia ancora. Il nuovo accesso sostituisce il token:
   GmailProvider.Calendar.cs:33-46 richiede gli ambiti già dati, ma se nella pagina di Google si toglie una casella, quel
   permesso sparisce senza avviso.
3. Modifica: cambiare solo il ruolo, Salva. In Google Contatti, e sul telefono, tutto il resto deve essere identico alla
   schermata: fax, secondo cellulare, compleanno, etichetta, foto, parti dell'indirizzo.
4. Cambiare solo il cellulare: fax e secondo cellulare restano.
5. Cambiare l'indirizzo: in Google Contatti c'è quello nuovo, senza resti del vecchio. Un rifiuto di Google qui è il caso
   "indirizzo come testo solo", mai provato.
6. Aprire Modifica in Missiva, intanto cambiare la nota dal telefono, poi Salva in Missiva. Missiva deve riaprire la finestra
   con i dati riletti, non sovrascrivere.
7. Nuovo contatto con Dove "Contatti di Google di ...": compare sul telefono.
8. Elimina "Mario Rossi Prova". La domanda ha Annulla come scelta predefinita e dice che il contatto sparisce dal telefono.
   Dopo la conferma sparisce da Google Contatti.

**Cosa deve vedere**: nessun campo perso, e l'eliminazione solo dopo la conferma.

**Se va male**: Google Contatti ha un ripristino delle modifiche recenti (fino a quanti giorni è da verificare).

### P-04 Azioni dalle notifiche

**Origine**: CLAUDE.md:109-110.

**Danno possibile**: medio-alto.
- Elimina e Posponi agiscono senza mostrare il messaggio.
- Il messaggio si ritrova da account, cartella e id (MainWindow.Notifications.cs:102-111). Un id che il server ha riusato
  indicherebbe un altro messaggio.
- Il testo scritto nelle notifiche con la casella, cioè quelle mostrate prima del 27 settembre (CLAUDE.md:104-106), parte
  senza finestra.

**Prima**: dal secondo account, tre messaggi "Notifica 1", "Notifica 2" e "Notifica 3". La prova va fatta per Gmail e per
un account Microsoft personale.

**Passi**
1. Con Missiva aperta, alla notifica di "Notifica 1" premere Segna: risulta contrassegnato in Missiva e sul sito.
2. "Notifica 2": menu "...", Posponi di un'ora. Il messaggio va in Posposti, e dopo un'ora torna in Posta in arrivo, da
   leggere, con una notifica.
3. "Notifica 3": Elimina. Sul sito è nel Cestino, ed è proprio "Notifica 3".
4. Rispondi: si apre la finestra della risposta e non parte niente da solo. Chiudere con Scarta.
5. Ripetere i passi 1-3 con Missiva nell'area di notifica, e poi con Missiva chiusa (Esci). Windows la avvia nascosta, fa
   l'azione e, se non deve restare nell'area di notifica, la richiude.
6. "Posponi..." dal menu "...": la finestra della scelta compare al centro dello schermo.

**Cosa deve vedere**: ogni azione sul messaggio giusto, e nessun messaggio estraneo nel Cestino.

**Se va male**: ripristinare dal Cestino.

### P-05 Caselle condivise di Microsoft 365

**Origine**: CLAUDE.md:905.

**Danno possibile**: medio-alto. Un messaggio che parte dall'indirizzo sbagliato (personale invece della casella, o "per
conto di") non si ritira.

**Prima**: serve un account di lavoro con accesso a una casella condivisa. Se l'autore non ne ha uno, si fa con il primo amico che
ce l'ha.

**Passi**
1. Tasto destro sull'account di lavoro, Aggiungi casella condivisa…, scrivere l'indirizzo. Poi "Consenti l'accesso alle
   caselle condivise".
2. Cartelle e messaggi devono essere gli stessi di Outlook sul web.
3. Rispondere a un messaggio della casella, verso un indirizzo di prova:
   - il destinatario vede come mittente la casella (o "per conto di", se manca Invia come);
   - la copia è nella Posta inviata della casella, non in quella personale.
4. Provare la ricerca nella casella, e la lettura senza rete.

**Cosa deve vedere**: il mittente giusto, la copia al posto giusto, e gli errori spiegati in italiano se manca un permesso.

### P-06 Consenso di Google per calendario e contatti

**Origine**: CLAUDE.md:809 e :838.

**Danno possibile**: medio. Il token nuovo sostituisce quello di prima.

**Passi**
1. Calendario, account Gmail, "Consenti l'accesso al calendario". Nella pagina di Google togliere la casella del
   calendario. Missiva deve dire "Non hai dato a Missiva il permesso..." e la posta deve continuare a funzionare.
2. Ripetere dando il permesso: gli eventi compaiono.
3. Poi "Consenti la modifica dei contatti", togliendo la casella del calendario. Controllare se il calendario resta: dal
   codice no, perché GmailProvider.Calendar.cs:46 tiene solo gli ambiti che Google ha dato.
4. Scegliere nel browser un altro account Google: Missiva deve rifiutarlo.

**Cosa deve vedere**: la posta non si perde mai, e ogni permesso tolto viene detto.

### P-07 Riga dell'elenco disegnata da un solo elemento

**Origine**: CLAUDE.md:542.

**Danno possibile**: medio-basso. Il trascinamento sposta messaggi.

**Passi**
1. Clic sulla pastiglia di un allegato in una riga: si apre l'anteprima.
2. Trascinare un messaggio di prova:
   - su una cartella dello stesso account: si sposta, e Annulla lo riporta;
   - con Ctrl premuto: si copia;
   - su un altro account: compare un avviso e non si sposta nulla.
3. Tasto destro su una riga: il menu è del messaggio giusto.
4. Con l'Assistente vocale, scorrere le righe: legge "Non letto, mittente, oggetto, ora...".
5. Controllare le righe con il logo vero di un'azienda.

### P-08 Avvio con Windows

**Origine**: CLAUDE.md:87.

**Danno possibile**: basso.

**Passi**
1. Impostazioni, Generali, Avvio e chiusura: accendere "Avvia Missiva all'accesso a Windows".
2. In Gestione attività, App di avvio, Missiva compare attiva.
3. Disconnettersi e rientrare:
   - c'è l'icona vicino all'orologio e nessuna finestra;
   - un messaggio di prova dà la notifica;
   - il clic sull'icona apre la finestra subito.
4. Spegnere l'opzione: la voce sparisce dalle App di avvio.

**Da sapere**: se la si spegne in App di avvio di Windows, Missiva continua a mostrarla accesa. App.cs:229-246 scrive solo la
voce Run e non legge lo stato di Windows.

### P-09 Missiva come app di posta, e disinstallazione

**Origine**: CLAUDE.md:43.

**Danno possibile**: basso (chiavi del registro del solo utente).

**Passi**
1. Lanciare installa.ps1.
2. Impostazioni di Windows, App, App predefinite, Missiva, MAILTO: sceglierla.
3. In una pagina web, un link di posta con oggetto e testo: si apre Nuovo messaggio con A, oggetto e testo. Provare con
   Missiva chiusa e con Missiva aperta.
4. Disinstallare da App installate (oggi chiama PowerShell 7: F-01). I link di posta non devono più aprire Missiva.

### P-10 File trascinati nella scrittura

**Origine**: CLAUDE.md:1177.

**Danno possibile**: basso.

**Passi**: trascinare da Esplora file sulla finestra di scrittura:
- un PDF: diventa un allegato;
- un'immagine sopra il testo: va dentro il testo;
- una cartella: viene rifiutata;
- un .exe: chiede conferma;
- più file insieme.

Poi inviare a sé stessi e controllare cosa arriva.

### P-11 Foto e nome del mittente con il mouse vero

**Origine**: CLAUDE.md:1236.

**Danno possibile**: nessuno.

**Passi**
1. Clic sulla foto e sul nome, in un messaggio da solo e in una conversazione: si apre la scheda.
2. Clic sull'anteprima o sulla data di un messaggio chiuso: si apre il messaggio.
3. Tab fino alla foto, poi Invio.
4. Ripetere in tema chiaro e scuro, nella finestra del messaggio (doppio clic), in quella di una cartella, in un .eml
   allegato e in una PEC.

### P-12 Persone: chiamate, Copia, Google Contatti nel browser, rotella

**Origine**: CLAUDE.md:1299.

**Danno possibile**: nessuno.

**Passi**
1. Chiama su un numero: si apre l'app delle chiamate di Windows.
2. Copia, poi Incolla nel Blocco note.
3. Senza il permesso di modifica, Modifica su un contatto di Google apre Google Contatti nel browser con l'account giusto
   (da provare con due account Gmail).
4. Rotella e touchpad sull'elenco: scorre come l'elenco dei messaggi.

### P-13 Barra laterale

**Origine**: CLAUDE.md:1433 e :1446.

**Danno possibile**: nessuno.

**Passi**
1. Aggiungere un Preferito, poi chiudere Preferiti e account. L'intestazione chiusa è evidenziata dove sta la cartella
   aperta.
2. "Apri in un'altra finestra" su una cartella.
3. Con l'account chiuso, leggere un messaggio dal telefono: il numero sull'intestazione scende.
4. Con l'Assistente vocale, l'intestazione dice "indirizzo, N non letti".

### P-14 Barra di scorrimento e zoom

**Origine**: CLAUDE.md:607 e :291.

**Danno possibile**: nessuno.

**Passi**
1. Trascinare con il mouse il cursore della barra in una cartella lunga: segue il mouse senza salti e arriva in fondo.
2. Pizzico sul touchpad nel riquadro: ingrandisce, e al messaggio dopo torna normale.
3. Ctrl+rotella: lo zoom cambia, resta dopo il riavvio, ed è lo stesso in Impostazioni, Riquadro di lettura.

### Fuori elenco

Le scritture del calendario non hanno un "Mai provato", ma CLAUDE.md:853-854 dice "Nella prova solo il registro": sono state
provate solo con i server finti di `--prova-bozze`. Riguardano:
- eventi con partecipanti, che mandano inviti veri;
- modifiche e annullamenti;
- risposte agli inviti.

Se l'autore non le usa già, vanno provate come P-02, con il secondo account come unico invitato.

---

## Le dieci cose da fare per prime

1. **F-05**: il client Google dell'autore dentro Missiva, con Gmail via IMAP come ripiego. Senza, un amico con Gmail non entra.
2. **F-01, con F-03 e LEGGIMI aggiornato**: uno zip pronto, senza SDK né PowerShell 7, con le istruzioni per SmartScreen.
   Senza, un amico non installa.
3. **P-01, P-02, P-03 e P-04**: prove a mano. Sono le quattro funzioni che scrivono sul server senza essere mai state
   provate, e vanno provate dall'autore prima che le usi qualcun altro.
4. **F-44, punti 1 e 2**: una copia delle impostazioni senza sovrascrivere un file illeggibile, ed errore d'invio con una
   via d'uscita. Un amico che perde gli account, o resta davanti a un errore tecnico, non ha nessuno accanto.
5. **F-15**: allegati oltre 3 MB con gli account Microsoft. È l'unico limite che blocca un gesto di tutti i giorni.
6. **F-02**: versione nell'app e controllo degli aggiornamenti. Con Missiva su più PC bisogna sapere chi ha cosa e far
   arrivare le correzioni.
7. **F-29**: copia di sicurezza. Contatti di Missiva, regole e modelli vivono solo su quel PC e non passano a un PC nuovo.
8. **Tre difetti piccoli e visibili**: "+N altri" cliccabile e Ctrl+N nel calendario (F-42), e la nota dell'annullamento di
   Google (1-06). In tutto mezza giornata.
9. **F-14**: avviso di allegato dimenticato. Poche ore, utile a tutti.
10. **F-11**: annulla iscrizione. È il guadagno più grande nell'uso quotidiano dell'autore.

Subito dopo: F-24 (iCloud, Yahoo, Libero), F-17 (schede di Gmail e notifiche delle promozioni) e F-37 (Posponi i promemoria).
