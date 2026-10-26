# Esito degli audit

## Riepilogo (5 ottobre 2026)

Dieci blocchi chiusi fra il 4 e il 5 ottobre 2026, ognuno con la sua sezione qui sotto e la sua parte "Non verificato".
Suite `--prova-bozze` all'ultimo commit di codice: 843 controlli, "tutto bene" (erano 573 prima degli audit). Versione
installata sul PC dell'autore il 5 ottobre alle 00:55, dopo una prova della versione finale su una copia dei dati (avvio in 0,8 s,
sei messaggi aperti dalla copia su disco, nessun errore; senza rete, perché il firewall blocca l'eseguibile di prova).

| Blocco | Che cosa | Esito |
|---|---|---|
| 1 | Pulizia dell'HTML: email che chiudevano o bloccavano Missiva | tutto confermato e corretto |
| 2 | Loghi dei mittenti: SVG, icone, richieste alla comparsa in elenco, logo senza autenticità | tutto confermato e corretto |
| 3 | Chi scrive davvero: fascia rossa aggirabile, nome che è un altro indirizzo, immagini in Posta indesiderata | corretto; una parte di S-24 falsa su Windows |
| 4 | Velocità, primo gruppo | 9 punti corretti |
| 5 | Difetti veri trovati nel rapporto delle funzionalità | 12 correzioni |
| 6 | Funzioni: allegato dimenticato, annulla iscrizione, allegati grandi con Microsoft | fatte |
| 7 | Copia di sicurezza (Esporta e Importa) | fatta |
| 8 | Velocità, secondo gruppo | 7 corretti, 3 misurati e lasciati |
| 9 | Velocità, terzo gruppo, e due segnalazioni dell'autore dopo la prima installazione | 9 corretti, 3 misurati e lasciati |
| 10 | Sicurezza, punti di gravità Bassa | 13 corretti, 2 solo valutati (S-06, S-08) |

Dopo l'installazione, il 7 ottobre, due difetti segnalati dall'autore (non letti fermi a 1, elenco in ritardo dopo la notifica):
cause, correzioni e controlli nell'ultima sezione, "Dopo l'installazione". Suite a 847 controlli. Non ancora installato.

Audit dell'11 ottobre 2026 (sicurezza, 36 punti T-, e codice, 71 punti C-), verificati l'11 e il 12 ottobre: numeri, scelte per
L'autore, cambi già fatti e prove a mano in "Audit dell'11 ottobre: riepilogo, scelte per l'autore e prove a mano". Suite a 1018.
Le 52 scelte di quel riepilogo, messe in pratica il 12 e il 13 ottobre sul ramo `scelte` (18 fatte, 17 chiuse com'erano, 13 non
fatte, 4 aperte): che cosa è stato fatto, le scelte nuove, i cambi che si vedono e le prove a mano in fondo, "Scelte del 12
ottobre: che cosa è stato fatto e che cosa resta". Suite a 1111. Non ancora unito in main né installato.

Revisione del 13 ottobre 2026 (sei consigli di un revisore esterno sul modulo Core, K-01..K-06 di `sicurezza-core.md`),
verificati il 13 e il 14 ottobre sul ramo `auditor`: quattro confermati e corretti, due confermati in parte; in tre la
correzione non è quella proposta. Esito, scelte per l'autore e prove a mano nell'ultima sezione, "Revisione del 13 ottobre:
sei consigli sul modulo Core (K-01..K-06)". Suite a 1137. Non ancora unito in main né installato.

Confermato dall'autore con l'uso (4 ottobre, versione con i blocchi 1-7): posta e loghi all'avvio, Canc, avviso di allegato
dimenticato, allegato grande inviato con l'account Microsoft (il caricamento a pezzi funziona con il server vero).

Aperto:
- mentre un'animazione è in corso (anche lo scorrimento con la rotella) clic e tasti possono arrivare con fino a mezzo secondo
  di ritardo: corretto solo il caso di Canc e Ctrl+Z (blocco 9, punto 12). CHIUSO l'8 ottobre: misurato con tasti e clic
  veri, non ritardano; ritardavano i comandi di prova (ultima sezione, "parte B, primo gruppo");
- un avvio della versione di prova su otto senza risposta ai comandi per oltre 30 s, non ripetuto (blocco 8);
- due righe "tempi:" di `GmailProvider` scrivono il nome della cartella nel registro installato (blocco 10);
- le chiavi delle mappe di cartelle e categorie in impostazioni.json restano in chiaro (S-26, per scelta);
- funzioni con importanza Media per l'autore non fatte: F-06, F-07, F-10, F-12 (blocca mittente), F-17, F-18, F-23, F-26, F-28,
  F-31, F-34, F-35, F-37; le funzioni per gli amici (F-01, F-02, F-03, F-05) non erano richieste.

Mai provato dal vivo, da fare a mano (elenco completo nella Parte 3 di `funzionalita.md`): disinstallazione da App
installate, Annulla durante il caricamento vero di un allegato grande, annulla iscrizione premuto davvero, copia di sicurezza
portata su un altro PC, risposte automatiche e regole con Outlook vero, modifica di un contatto di Google.

I rapporti di questa cartella sono stati scritti nel cloud leggendo il codice, senza eseguire Missiva. Qui, punto per punto,
quello che è successo sul PC dell'autore (Windows 11, .NET 10.0.12, 16 core logici): se il difetto si riproduce, se è stato
corretto, quale controllo lo prova, i numeri prima e dopo. "Prima" è il commit `3ae3675`.

Parole usate: CONFERMATO SU WINDOWS (riprodotto qui), NON CONFERMATO (provato, non si riproduce), NON VERIFICATO (non
provato, con il motivo); CORRETTO o NON CORRETTO. I controlli sono in `Core/ProvaBozze.Audit.cs` (`HtmlAuditTests`, 32
controlli, `LogoAuditTests`, 26, `SenderAuditTests`, 22, `SpeedAuditTests`, 9, `DefectAuditTests`, 41, `Speed2AuditTests`, 17, `Speed3AuditTests`, 15, e `SecurityLowAuditTests`, 54; anche da soli con `Missiva.exe
--prova-audit risultato.txt`, con `velocita` in fondo solo quelli del blocco 4, con `difetti` solo quelli del blocco 5, con `velocita2` solo quelli del blocco 8, con `velocita3` solo quelli del blocco 9 e con `sicurezza` solo quelli del blocco 10; con `pdf` gira `PdfTestsAsync` da sola) e girano nella suite `--prova-bozze`.

## Blocco 1: pulizia dell'HTML (`Core/MailHtml.cs`)

Fatto il 4 ottobre 2026. Misure con un banco fuori dal repository (`C:\Progetti\Missiva\loop\audit-I\banco`, console
net10.0-windows in Release) che compila la copia di `MailHtml.cs` di `3ae3675` accanto a quella nuova, con il
`ColorConverter` vero di WPF; gli overflow anche nella Missiva di Debug, in un processo a parte (`--page`, e il nuovo
`--testo` per `ReadableText`). Tempi in millisecondi o secondi di un giro (mediana di cinque sotto i 300 ms).

### Punti dei rapporti

- **V-01** (overflow dello stack in `ToText` e nel testo dei link). CONFERMATO SU WINDOWS, CORRETTO. Prima: il processo
  si chiude con 0xC00000FD a 8.031 `<div>` annidati (48 KB) sul thread principale e a 8.089 su un thread del pool
  (tutti e due con 1.536 KB di stack), a 5.364 con 1.024 KB, a 1.292 con 256 KB; lo stesso per `Prepare` con un link
  che contiene 8.031 `<span>`; nella Missiva di Debug 8.031 (principale e `--page`) e 8.090 (pool). `Prepare` senza
  link non va in overflow (30.000 livelli: finisce in 12 s). Dopo: 200.000 livelli finiscono in 0,2 s su tutti e tre
  i thread, la Missiva di Debug con 100.000 esce con 0. Correzione: `TextOf` e `ToText` con una pila al posto di
  `TextContent`, che in AngleSharp è ricorsivo. Controllo: "20.000 elementi annidati su un thread con 256 KB di stack".
- **V-02** ("<" che non aprono un tag). CONFERMATO SU WINDOWS, CORRETTO. Prima: 100.000 "<" (98 KB) 1,2 s e 9,5 GB
  chiesti; 40.000 "a < b" 1,1-1,3 s e 9,2 GB. Dopo: 25-60 ms e 8,6 MB; 4-20 ms e 8 MB. Correzione: `EscapeStrayLt`
  scrive come `&lt;` i "<" del testo prima dell'analisi; diversa dalla patch del rapporto, che li cambiava anche dentro
  commenti, nomi di attributo, `xmp`, `iframe`, `plaintext` (provata qui: lì l'albero non è quello di prima): ora segue
  i tag come l'analisi dell'HTML e tocca solo il testo, e solo se sono più di 64. Trovato in più: AngleSharp ricopia il testo
  anche quando a spezzarlo sono tag di chiusura che non chiudono nulla, tag ignorati (`<td>` fuori da una tabella,
  `<html>`, `<head>`), "<" dentro `style`, `textarea`, `title`, `xmp`, testo in una tabella fuori dalle celle (per
  ognuno 1,5 GB con 40.000 ripetizioni): questi li ferma il tetto di memoria del punto V-03 c. Controlli: "V-02,
  100.000 "<"", "V-02, 40.000 "a < b"", "S-30, testo spezzato da 40.000 tag di chiusura", "160 "<" sciolti nel testo".
- **V-03** (annidamento profondo). CONFERMATO SU WINDOWS, CORRETTO.
  - a (tema scuro che risale agli antenati): 20.000 `<b><i>` mai chiusi 22,9 s con il tema scuro contro 4,7 con il
    chiaro. Corretto come nel rapporto (il genitore, già visto, sa se sta dentro un fondo vivace).
  - b (`Closest("pre")` in `ToText`): 20.000 `<div>` 14,9 s di `ToText` contro 1,7-3,1 dell'analisi di AngleSharp da
    sola (`Prepare` 4,5 s con il tema chiaro, 6,5 con lo scuro). Corretto con la pila di V-01.
  - c (tetto): l'analisi di AngleSharp da sola costa 0,2-0,5 s per 5.000 `<div>` e 1,7-3,1 per 20.000. Ora
    `MailHtml.ParseGuard` conta mentre AngleSharp lavora (ganci `OnToken` e `OnCreated`, sull'albero vero, non su una
    stima fatta prima) e oltre un tetto ferma l'analisi: 512 livelli (Blink tiene 512 elementi aperti e oltre attacca i
    nodi come fratelli: con Edge 154, 5.000 `<div>` annidati danno un albero di 513 livelli), 250.000 elementi e mai
    più di due per carattere, 16 milioni di passi (un gettone per la profondità a cui arriva), 256 attributi in un tag,
    8 MB più 1 KB per carattere di memoria chiesta (soglie scelte qui, sulle misure: da rivedere se servono). Oltre il tetto il messaggio si apre come
    solo testo, letto senza AngleSharp in una passata (`RoughText`), con gli indirizzi web come link e la fascia
    "Messaggio troppo complesso: mostrato come solo testo." (`ReadingPage.Warnings`, `Result.PlainOnly`); lo stesso
    testo per `ToText` e `ReadableText`. Le 673 email vere della copia di prova arrivano a profondità 84, 2.482
    elementi, 34 attributi, 30 byte per carattere. Dopo: 20.000 `<div>` 6-9 ms, 20.000 `<b><i>` 4-7 ms, 1.000 tabelle
    annidate 6-11 ms (prima 0,13-0,60 s). Controlli: "510 <div> annidati", "oltre il tetto", "nel riquadro la fascia",
    "V-03, ...".
- **V-04** (espressioni regolari quadratiche). CONFERMATO SU WINDOWS per tutte e sei, CORRETTO. Dentro `Prepare`, prima
  e dopo: "url(" 10.000 volte in uno stile (40 KB) 12,5-13,0 s, ora 0,5-22 ms; "<meta " 10.000 volte 1,7 s (scuro),
  ora 3 ms; "pixel" 20.000 volte nell'indirizzo di un'immagine 0,9-1,1 s, ora 1,5 ms; "url(" e 30.000 spazi in un
  attributo 2,5 s, ora 0,5 ms; una parola di 30.000 lettere e una "\" 3,7-3,9 s, ora 0,5 ms; "rgb(" 20.000 volte in
  un colore 0,16 s e 20.000 eccezioni, ora 6-11 ms e nessuna. Le regex da sole a 256.000 caratteri ora 0,6-1,4 ms.
  Correzioni, in parte diverse dalla patch: `SpacerName`, `OwnDarkMode`, `CssEscapedFunction` con `NonBacktracking`
  (come la patch); `RemoteUrlFunction` riscritta con le stesse corrispondenze di prima (la patch la allargava a
  `url(''//...`, che Chromium non carica); `ColorToken` com'era, ma la coda di un valore dopo l'ultima ")" passa da
  una regex senza parentesi (`OpenTail`: stesse corrispondenze; quella della patch, provata qui, spezza
  `rgba(var(--x-black), .5)` in parole e ne ricolorerebbe "black"); `CssUrl` con `NonBacktracking` e le forme del
  punto "Fuori tema". Controlli: "V-04, ...".
- **V-07** (una FormatException per ogni parola che non è un colore). CONFERMATO SU WINDOWS, CORRETTO. Eccezioni per
  `Prepare` nel tema scuro, prima: newsletter da 50 KB 146, 200 KB 348, 1 MB 1.456, 5 MB 6.944, 5.000 stili negli
  elementi 10.000, `<style>` con 2.000 regole 24.000; dopo zero. Un'eccezione qui costa 1,6 µs (`ParseColor("solid")`
  da 1.660 a 48 ns): `<style>` con 2.000 regole 69 ms, ora 26; 5.000 stili 143, ora 89; sulle newsletter il guadagno
  è dentro il rumore. `ParseColor` dà gli stessi colori e gli stessi null di prima su 2.191.364 valori (i 141 nomi di
  `Colors` in ogni combinazione di maiuscole, storpiati, con U+212A; # da 0 a 10 cifre; `rgb()`, `hsl()`, `url()`,
  `sc#`; 300.000 parole a caso) e nessuna eccezione sui 2.128.626 che `ColorToken` può produrre. Controlli: "tema
  scuro, 100 regole e 200 celle", "ParseColor su 29992 valori".
- **Fuori tema: `CssUrl` e `url('...')` con una parentesi**. CONFERMATO SU WINDOWS, CORRETTO. Prima
  `background:url('https://.../a(b).png')` restava nel frammento e non contava fra le immagini bloccate. Edge senza
  finestra (lo stesso motore di WebView2) legge come indirizzi validi anche: fra virgolette con l'altro tipo di
  virgolette dentro, fra virgolette o senza con `\'`, con la "h" scritta `\68`; tutte passavano, ora sono bloccate.
  `url(''//...)`, `url(a b)` e `url(a(b))` per Chromium non sono indirizzi. Effetto sulle email vere: 4 su 673 (lo
  stesso mittente) hanno in `@font-face` un indirizzo fra virgolette con apici dentro, che prima passava e ora è
  bloccato (immagini bloccate da 18-19 a 22-23; con le immagini consentite nulla cambia). Controlli: "url(...) esterni
  bloccati e contati", "le forme di url(...) riconosciute anche prima".
- **S-29** (`CssEscapedFunction` quasi cubica). CONFERMATO SU WINDOWS, CORRETTO. La regex da sola: 250, 500, 1.000,
  2.000 "\" in 7,6, 61, 545, 4.013 ms (esponente 2,9). `Prepare` con 2.000 "\" in uno stile (2 KB): 3,9-4,0 s, ora
  0,3 ms; con 50.000 ora meno di 1 ms. Controlli: "S-29, ...".
- **S-30** (allocazioni enormi e costo più che lineare con l'annidamento). CONFERMATO SU WINDOWS, CORRETTO con V-02 e
  V-03. Il limite sulla dimensione dell'HTML proposto dal rapporto non c'è: i tetti sono su quello che costa.

### Trovato in più, corretto

- Elementi di formato mai chiusi: AngleSharp li riapre tutti in ogni paragrafo. `<p><font color=N>x` ripetuto 1.000
  volte (25 KB) dà 501.503 elementi (2,3 s chiaro, 5,7 s scuro, 985 MB); 2.000 volte (51 KB) 2.003.003 elementi (18 s
  chiaro, 55 s scuro, 3,7 GB). Ora 0,1 e 0,2 s, come solo testo (tetto di profondità o degli elementi).
- Molti fratelli sotto lo stesso genitore: `InnerHtml` di AngleSharp cerca il fratello successivo di ogni foglia dal
  primo. Un messaggio di 100.000 righe chiuse da `<br>` (781 KB): 16,4 s chiaro, 19,9 s scuro, 128 s `ToText` (anche
  V-09); ora 0,27, 0,31 e 0,15 s, mostrato com'è. 100.000 commenti: 3,5 s, ora 0,04-0,07. Vale anche per le email
  normali lunghe: 5.000 stili negli elementi 76 ms chiaro, ora 35; `ToText` 335, ora 30. Correzione: `InnerHtml` di
  `MailHtml`, lo stesso di AngleSharp (`Node.ToHtml`) in una passata; quello che si toglie (script, moduli, style) non
  si stacca più dall'albero ma si salta (100.000 `<input>`: 1,2 s, ora 0,5).
- 500 `<div>` aperti e 100.000 `<div>x</div>` (1,2 MB): 6,4 s chiaro, 7,2 s scuro, 4,0 s `ToText`; ora 0,15 s, come
  solo testo (tetto dei passi).
- Un tag con 20.000 attributi: 54-57 ms, col quadrato; ora 8 ms, come solo testo.

### Uscita identica

Vecchia e nuova sugli stessi ingressi (`Prepare` con tema scuro e chiaro, immagini bloccate e consentite: frammento,
immagini bloccate, link; `ToText`, `ReadableText`, `PlainPreview`):

- 12 ingressi normali generati (quelli del rapporto, fino a 5 MB): 84 confronti, tutti identici.
- 434 testi di prova presi dai file .cs del repository: 3.038 confronti, tutti identici.
- 673 email vere (i 6 file .html dei dump e la copia cifrata dei corpi della cartella di prova, letti solo in memoria):
  4.711 confronti, 4.703 identici; gli 8 diversi sono le 4 email del punto "Fuori tema".
- 5 pagine di prova lasciate da altre sessioni in `C:\Progetti\Missiva\loop`: 35 confronti, identici.
- 6.708 varianti a caso degli ingressi sopra (da 1 a 6 ritocchi: "<", parentesi, virgolette, "\", tag): 46.956
  confronti, 60 diversi, tutti spiegati (48 dalle varianti delle 4 email, 6 da due `url()` fra virgolette in cui il
  ritocco ha messo una parentesi o un tag, 6 da un ritocco che lascia quasi tutto il messaggio dentro un contenuto
  grezzo: 144 elementi su 217.605 caratteri, oltre il tetto di memoria).
- I pezzi nuovi contro AngleSharp, su 1.145 ingressi e 6.828 varianti: `InnerHtml`, `TextOf` ed `Elements` sempre
  uguali a `Element.InnerHtml`, `TextContent`, `dom.All`.

Dove l'uscita cambia, oltre ai messaggi sopra i tetti: le `url()` del punto "Fuori tema" (bloccate); `url(...)` con
un `\` davanti a una parentesi o a una virgoletta (bloccata come prima, ma il motore `NonBacktracking` può prendere la
corrispondenza più lunga: resta `none` invece di `none` più un resto); con più di 64 "<" sciolti, gli spazi attorno a
un "<" scritto fra le righe di una tabella finiscono fuori dalla tabella insieme a lui (nelle prove dei pezzi l'albero
cambia in 76 varianti su 934 con la riscrittura forzata; le 8 guardate erano tutte così).

### Costo sulle email normali

Misure che sotto il millisecondo non concordano fra loro (tema chiaro, scuro, `ToText`):

- Vecchia e nuova alternate, una raccolta completa prima di ogni giro, mediana di 21 giri (9 per i 5 MB). Newsletter
  da 50 KB: 2,4 ora 3,7; 5,0 ora 5,4; 1,2 ora 1,9. 200 KB: 8,9 ora 10,5; 20,7 ora 24,7; 4,9 ora 4,9. 1 MB: 66 ora 75;
  118 ora 107; 30 ora 33. 5 MB: 408 ora 412; 513 ora 528; 275 ora 210.
- Di seguito, senza raccolte, il migliore di 7 giri. 50 KB: 3,0 ora 2,8; 4,0 ora 4,0; 1,7 ora 1,1. 200 KB: 15,6 ora
  16,3; 18,4 ora 18,3; 6,9 ora 7,2. 1 MB: 75 ora 84; 104 ora 104; 44 ora 37.
- I pezzi: l'analisi con i tetti costa 4-10 ms in più per MB di HTML (il gancio `OnToken`, per il quale AngleSharp
  crea un oggetto per gettone; `OnCreated` da solo non si distingue dal rumore); `EscapeStrayLt` 0,6 ms per MB;
  `InnerHtml` nuovo pari o più veloce; le regex `NonBacktracking` per chiamata pari (`CssUrl` su uno stile senza
  `url(` 99 ns contro 133, con `url(` 369 contro 298; `SpacerName` 120-180 ns contro 20-160); `ParseColor` di un nome
  83 ns contro 44, di una parola che non è un colore 48 contro 1.660.

In sintesi: fino a 200 KB da pari a 1-4 ms in più secondo la misura; a 1 MB con il tema chiaro 8-9 ms in più; `ToText`
più veloce sui messaggi grandi. Il primo uso delle regex `NonBacktracking` in un processo costa circa 45 ms in più
una volta sola (nel banco: `SpacerName` 21-25 ms, ora 56-60; `OwnDarkMode` 1, ora 7-9; `CssUrl` 1-2, ora 3-4): in
Missiva quelle di `OwnDarkMode` e `CssUrl` cadono nel riscaldamento che `ReadingView.Prepare` fa in sottofondo
all'avvio.

### Il caso peggiore sotto i tetti

Messaggi costruiti per restare appena sotto un tetto, o superarlo il più tardi possibile (tema scuro, chiaro, `ToText`,
due esecuzioni): 480.000 `<b></b>` (3,3 MB) 0,9-1,0, 0,35-0,39, 0,36-0,45 s; 480.000 `<br>` 0,75-0,80, 0,64-0,70,
0,64-0,68; 400.000 `<input>` (5,5 MB) 0,8-1,1, 0,8-1,0, 0,7-0,9; 400.000 link (20 MB) 1,2-1,4, 1,1-1,3, 1,0-1,3; un
testo di 4 MB con 400 pezzi accodati 0,33-0,53, 0,31-0,39, 0,33-0,36 (3,1 GB chiesti e subito liberati); 500 `<font>`
aperti e `<p>x` fino al tetto degli elementi 0,35-0,49, 0,30-0,45, 0,26-0,38.

### Scelte fatte qui, da rivedere

- I tetti oltre la profondità (elementi, passi, attributi, memoria) e le loro soglie: il rapporto proponeva solo la
  profondità, ma da sola non ferma gli elementi di formato riaperti né il testo spezzato.
- Oltre il tetto restano solo il testo e gli indirizzi web scritti per esteso: i link dietro una parola, le immagini
  e il formato si perdono (gli allegati restano nelle loro schede).
- `CssUrl` blocca più forme di quella del rapporto (anche l'altro tipo di virgolette e `\'`): per 4 email vere su 673
  un carattere esterno che prima passava ora è bloccato finché non si consentono le immagini.
- Due argomenti in più nella Debug: `--testo` e `--prova-audit`.

### Non verificato

- La Missiva installata (Release, un file solo, ReadyToRun) non è stata eseguita: le soglie dell'overflow sono della
  Debug e del banco in Release, tutti con lo stack da 1.536 KB.
- Nessuna prova nella finestra: un messaggio oltre il tetto aperto nella versione di prova, Rispondi sullo stesso, la
  fascia dentro WebView2. La fascia è stata vista solo nella pagina generata da `--page`, fotografata con Edge senza
  finestra (tema chiaro e scuro).
- Le righe "tempi: HTML pulito in sottofondo" e i contatori di dotnet-counters proposti dal rapporto non sono stati
  misurati; il costo del primo uso delle regex all'avvio di Missiva nemmeno.
- Come Chromium legge le `url()` è stato provato con Edge senza finestra, non dentro il riquadro di Missiva.
- `HtmlToFlow` (bozze riprese) e `SenderLogos` (pagine dei siti dei mittenti) analizzano HTML con AngleSharp senza
  tetti: non toccati, non provati.
- Le regex quadratiche di `PlainPreview` (`ImagePlaceholder`, `MarkdownLink`, `CutMarkdownLink`) restano: lavorano
  sulle anteprime, di poche centinaia di caratteri. Non riprovate.
- Su un PC con Windows in turco le regex `NonBacktracking`, costruite all'avvio e non più alla compilazione, trattano
  la "i" secondo quella lingua: non provato.

## Blocco 2: loghi dei mittenti (`Core/SenderLogos.cs`)

Fatto il 4 ottobre 2026. "Prima" è il commit `b2589dc`. Le prove che chiudono o fermano il processo (overflow dello
stack, disegni che non finiscono) con un banco fuori dal repository (`C:\Progetti\Missiva\loop\audit-II\banco`, console
net10.0-windows in Release con SharpVectors.Wpf 1.8.6 usato come lo usava Missiva e la `SafeSvg` di prima copiata
com'era); tutto il resto nella Missiva di Debug con i controlli, lanciati prima e dopo ogni correzione. Niente rete
vera: un ascoltatore su 127.0.0.1 che conta le connessioni, nomi di server che non esistono, DNS e HTTP finti
(`MailDns.TestTxt`, `SenderLogos.UseTestHandler`). Una sola eccezione, detta al punto S-17.

### Punti del rapporto

- **S-01** (`SafeSvg` si aggira, SharpVectors carica risorse esterne). CONFERMATO SU WINDOWS, anche la parte che il
  rapporto aveva solo letto nel sorgente. CORRETTO.
  - Con le impostazioni di prima SharpVectors apre connessioni http verso 127.0.0.1 per: `<image>` (due per
    immagine), `@import` in `<style>`, `<?xml-stylesheet?>`, `<use>` esterno, `fill="url(http...)"`. Non per
    `@font-face`, `font-face-uri`, DTD esterno ed entità esterne (zero connessioni).
  - Il filtro di prima si aggira: `&#64;import` (accettato, una GET); un SVG in UTF-16 e uno gzip (accettati se nei
    byte c'è "<svg": in un commento in UTF-16, nel campo del nome del file in gzip), che SharpVectors decodifica da
    solo (due GET l'uno). Nella Missiva di Debug sei SVG così: 8 connessioni.
  - Percorsi di rete: `@import "file://server-che-non-esiste/..."` dà "Impossibile trovare il percorso di rete" dopo
    2,4 s; `<use>` verso `\\server\...` o `file://server/...` e `<image>` verso `\\127.0.0.1\...` costano 2,3-2,4 s
    (la ricerca del server). Il tentativo parte; che esca un hash NTLM non è stato guardato.
  - Con `ExternalResourcesAccessMode = Ignore` e `CanUseBitmap = false` le connessioni http spariscono tutte, ma il
    `<use>` verso un percorso di rete costa ancora 2,3-2,4 s: le impostazioni da sole non bastano.
  - Trovato in più, stessa superficie (l'icona SVG di un sito noto, senza clic): 3.000 `<g>` annidati (17 KB; 1.000
    passano) o una maschera che usa sé stessa finiscono lo stack e chiudono il processo; non finiscono mai, con un
    core al 100%, `fill:url(http...)` in un attributo `style` (e 1 GB di memoria al secondo: 7,4 GB visti), una
    parentesi mai chiusa in un colore (`rgb(1,2`, `url(#g`), e in un foglio di stile `@media` con una condizione
    (quello che ha l'icona di un sito con il tema scuro), `@keyframes`, `@supports`; prima, passati i 10 secondi, il
    thread restava a girare. Si moltiplicano i `<use>` (cinque livelli da dieci, 1,4 KB: 16,6 s e 8,3 GB chiesti; sei
    livelli non finiscono), i motivi e le maschere annidati (quattro livelli 2,4-2,6 s, sei non finiscono).
  - Correzione: `Core/LogoSvg.cs`. I byte letti come UTF-8 (niente gzip, UTF-16, byte nulli), un lettore XML senza
    DTD, lista bianca di elementi e attributi (il resto si toglie; `script`, `image`, `foreignObject` e simili
    rifiutano l'SVG, come prima), `href` e `url()` solo interni, stili riscritti dichiarazione per dichiarazione
    (solo proprietà di presentazione, valori con parentesi e virgolette chiuse, niente "\" e ":"), fogli di stile
    senza regole `@` e con selettori semplici, tetti (64 livelli anche seguendo i riferimenti, 10.000 elementi
    disegnati, lavoro 20.000 contando motivi e maschere; da un foglio di stile solo sfumature e ritagli; motivi e
    maschere mai uno dentro l'altro; nessun ciclo). A SharpVectors arriva il testo riscritto, non i byte del sito, con
    `ExternalResourcesAccessMode = Ignore` e `CanUseBitmap = false`. Un disegno oltre 5 secondi si ferma davvero
    (`ControlledExecution` in `OnStaAsync`: provato nel banco, il thread si ferma e dopo si disegna ancora).
  - I loghi di prima: 3.827 SVG diversi presi dai programmi installati su questo PC, disegnati con il codice di prima
    e con il nuovo a 96 x 96: 3.703 disegnati prima e dopo, tutti identici pixel per pixel; 1 perso (un `fill` con
    `var(` e una parentesi mai chiusa); 8 in più (4 prima non finivano: `@keyframes`; 4 il filtro di prima li
    rifiutava); 115 senza logo prima e dopo. `LogoSvg.Clean` 0,6 s in tutto (0,15 ms l'uno). Appena sotto i tetti
    SharpVectors impiega 0,7-1,9 s (Release).
  - Controlli: "S-01, ..." (7).
- **S-02** (icone con la scelta automatica dei codec, misure controllate dopo). CONFERMATO SU WINDOWS, CORRETTO.
  Prima: un WebP ("Microsoft Webp Decoder"), due TIFF, un DDS e sette AVIF (con estensione .png) trovati su questo PC
  si decodificavano; un PNG valido a 8 bit di grigio che dichiara 16.000 x 16.000 pixel (243 KB) chiedeva 245 MB in
  154 ms, uno da 30.000 pixel 830 MB in 378 ms, prima del controllo delle misure. Ora: tipo dalla firma (PNG, JPEG,
  GIF, BMP, ICO) e decodificatore di quel tipo; misure lette dall'intestazione (`DeclaredSize`: anche il primo
  fotogramma del GIF, e ogni immagine dell'ICO, PNG o bitmap) e niente decodifica oltre 1024 pixel; del GIF solo il
  primo fotogramma. 1.001 immagini vere di questo PC: 752 danno lo stesso logo di prima byte per byte (297 PNG, 113
  JPEG, 64 GIF, 56 BMP, 222 ICO), 242 senza logo prima e dopo, 7 perse che erano gli AVIF. Controlli: "S-02, ..." (4).
- **S-03** (richieste alla sola comparsa di una riga). CONFERMATO SU WINDOWS con DNS e HTTP finti, CORRETTO. Per
  S-03 e S-04 il "prima" non è il codice di `b2589dc` com'era (non aveva i ganci per le prove) ma quello corretto con
  i controlli nuovi tolti (`Request` senza condizioni, `Logos` senza `IsAuthenticated`, la pagina senza la regola
  della fascia rossa): così, per un dominio mai visto con un record BIMI, partono la richiesta TXT e la GET
  dell'indirizzo `a=` scelto dal mittente. Ora `SenderLogos.Request` parte solo per i domini noti (`IsKnown`) e
  autenticati (S-04), e dice di no per gli altri; User-Agent di un browser qualunque, senza "Missiva". Il ritardo
  casuale proposto dal rapporto non c'è. Controlli: "S-03, ..." e "S-03 e S-04, ..." (3).
- **S-04** (logo per un From falsificato). CONFERMATO SU WINDOWS (nel codice di prima il logo non guardava le
  Authentication-Results in nessun punto; con i controlli nuovi tolti, vedi S-03, il logo resta accanto alla fascia
  rossa e nell'elenco per un DMARC fallito), CORRETTO.
  - Autenticato (`SenderCheck.Authenticated`, sulle Authentication-Results del server della propria casella,
    `SenderCheck.Own`): DMARC superato con `header.from` del dominio del marchio (anche un suo sottodominio); senza
    un risultato di DMARC (dominio senza politica, o `bestguesspass` di Microsoft) quello che DMARC controllerebbe:
    una firma DKIM superata di quel dominio, o SPF superato per un mittente della busta di quel dominio. Mai con la
    fascia rossa, con DMARC fallito o di un altro dominio.
  - Dove si annota: `SenderLogos.NoteBody`, quando il corpo arriva, perché le intestazioni arrivano con lui: aperto
    nel riquadro (`ReadingView.FetchBodyAsync`), scaricato in anticipo (`MainWindow.PrefetchListAsync`), copiato per
    leggerlo senza rete (`OfflineCopy`). Mai dalla Posta indesiderata né da un .eml aperto. Il segno sta in
    `domini.dat` (`Verified`, la data dell'ultima volta).
  - Dove vale: `Logos.DomainFor` e `Logos.Get` (elenco, riquadro, scheda della persona, notifiche) non danno un logo
    per un dominio non autenticato, nemmeno se il file c'è. Un messaggio il cui corpo è arrivato senza la prova resta
    senza logo anche se il dominio è autenticato (`IsDoubtful`, in memoria), e la pagina del riquadro non mette mai il
    logo accanto alla fascia rossa (`ReadingPage.Message`).
  - Aggiornamento: `domini.dat` di prima non ha il segno. Una volta sola, due secondi dopo l'avvio e in sottofondo,
    `AuthenticateFromCopies` rilegge le copie dei corpi già sul PC (`BodyCache.Each`), che hanno le
    Authentication-Results. Sulla copia dei dati di prova: 4 account, 654 copie rilette in 0,75 s, 100 domini su 102
    con almeno un messaggio autenticato; dei 63 loghi su disco 56 restano subito, 7 non hanno nessuna copia sul PC e
    tornano (il file resta) al primo messaggio autenticato che arriva. Contando solo DMARC alla lettera sarebbero 54.
  - Controlli: "S-04, ..." e "S-03 e S-04, ..." (6).
- **S-16** (`IsPublic` e proxy). CONFERMATO SU WINDOWS tutti e due, CORRETTO. Prima passavano per pubblici
  `64:ff9b::c0a8:101`, `2002:c0a8:101::1`, `::c0a8:101`, `2001:db8::1`, un indirizzo Teredo, `192.0.0.1`,
  `192.0.2.1`, `198.18.0.1`, `198.51.100.7`, `203.0.113.9`; e con un proxy di sistema il controllo riceveva
  l'indirizzo del proxy (provato con un proxy finto su un indirizzo di esempio e un gancio che legge a chi il gestore
  sta per collegarsi, senza collegarsi). Ora: IPv6 solo globali (2000::/3), per NAT64 (`64:ff9b::/96`) e 6to4 conta
  l'IPv4 dentro, mai Teredo e `2001:db8::/32`; IPv4 senza le reti di prova e di misura; `UseProxy = false`. Un
  prefisso NAT64 scelto dal gestore della rete non si riconosce. Controlli: "S-16, ..." (2).
- **S-17** (l'SVG `l=` non si confronta con il certificato, il logo salvato non scade). CONFERMATO SU WINDOWS con una
  radice di prova, DNS e HTTP finti; CORRETTO, tutte e due le parti.
  - Il logo BIMI ora è l'SVG dentro il certificato del marchio (estensione 1.3.6.1.5.5.7.1.12, `subjectLogo`, un
    indirizzo `data:` con l'SVG gzip: `EmbeddedLogo`); l'indirizzo `l=` non si scarica più. Un certificato senza
    quel logo non dà un logo BIMI (resta l'icona del sito).
  - Unica richiesta vera di tutto il blocco: un record TXT di BIMI e il certificato (8,7 KB) di un marchio noto,
    scaricato una volta e tenuto fuori dal repository. Con quello: catena valida fino alla radice fissata in 55 ms
    senza scaricare nulla, rifiutato per un altro dominio, logo dentro di 1.056 byte, accettato dal filtro di S-01, e
    il PNG che ne esce è uguale byte per byte a quello salvato il 28 settembre dall'indirizzo `l=`.
  - Trovato in più: la catena del certificato non aveva `DisableCertificateDownloads`, e con un certificato a cui
    manca l'anello di mezzo Windows andava a prenderlo all'indirizzo scritto nel certificato (una connessione a
    127.0.0.1 nella prova, fuori dal controllo degli indirizzi). Ora non scarica.
  - Scadenza: un logo salvato si riverifica dopo 30 giorni, una volta per sessione, quando una riga lo mostra
    (`Logos.Get`, `IsStale`); resta quello vecchio finché arriva la risposta; se la rete o il sito non rispondono
    resta, ma non oltre 60 giorni; se il sito non lo ha più esce.
  - Controlli: "S-17, ..." (4).
- **Segnalato dal blocco 1: la pagina del sito in AngleSharp senza tetti.** CONFERMATO SU WINDOWS, CORRETTO. Prima
  (`DeclaredIcons`, Debug): 1.000 `<font>` mai chiusi in 25 KB 1,0 s e 302 MB chiesti, 20.000 `<div>` annidati in 100
  KB 3,0 s, 100.000 "<" in 98 KB 1,1 s e 9,5 GB chiesti; la pagina può arrivare a 768 KB. Ora
  `MailHtml.ParseWithinLimits` (gli stessi tetti di `ParseGuard`): al massimo 106-116 ms e 32 MB, e oltre i tetti
  nessuna icona dichiarata (resta `favicon.ico`). Una pagina normale di 150 KB dà le stesse icone; 400 pagine HTML
  vere di questo PC fra 60 e 768 KB (documentazione di programmi installati) stanno tutte sotto i tetti, la più lenta
  109 ms. Controlli: "icone dichiarate, ..." (2).

### Che cosa cambia per chi usa Missiva

- Meno loghi, come concordato: solo per i domini da cui la posta arriva da almeno 14 giorni e di cui un messaggio,
  aperto o scaricato in anticipo, ha superato DMARC. Un marchio con il certificato BIMI non ha più il logo dal primo
  messaggio: aspetta anche lui i 14 giorni.
- Al primo avvio dopo l'aggiornamento i loghi compaiono qualche secondo dopo l'elenco (la rilettura delle copie), e
  quelli dei domini senza un messaggio autenticato sul PC tornano più avanti (7 su 63 nella copia di prova).
- Un messaggio che non supera il controllo resta con le iniziali anche se il dominio ha il logo: nel riquadro sempre,
  nell'elenco da quando il suo corpo è arrivato. Prima di allora la riga mostra il logo del dominio, e così la
  notifica di un messaggio appena arrivato.
- Dietro un proxy obbligatorio i loghi non si scaricano.
- Le icone AVIF, WebP e TIFF dei siti non si usano più; gli SVG con immagini incorporate nemmeno, come prima.

### Scelte fatte qui, da rivedere

- "Autenticato" anche senza DMARC, con una firma DKIM o un SPF del dominio del From: il rapporto chiedeva DMARC
  superato. Vale 2 loghi su 63 e 3 domini su 102 nella copia di prova.
- Il segno "autenticato" non scade: basta un messaggio autenticato, una volta.
- `ControlledExecution` per fermare il thread del disegno: .NET lo sconsiglia in generale (lo stato del processo può
  restare a metà); qui il thread è solo del disegno, e l'alternativa era un thread che gira per sempre.
- I tetti di `LogoSvg` e le sue liste (elementi, proprietà, selettori): scelti sulle misure di questo PC e sugli SVG
  dei programmi installati, non su un campione di loghi veri dei siti. Filtri, marcatori, caratteri SVG e
  animazioni si tolgono; un `fill` scritto male rifiuta l'SVG.
- Un SVG senza lo spazio dei nomi di SVG si accetta ancora (SharpVectors lo disegnava).
- I 30 e 60 giorni della riverifica dei loghi.
- Tre ganci di prova in più nella Debug (`UseTestHandler`, `TestConnect`, `TestMarkRoot`).

### Non verificato

- La Missiva installata (Release, un file solo, ReadyToRun) non è stata eseguita: `ControlledExecution`, il disegno
  degli SVG e la rilettura delle copie sono provati solo nella Debug e nel banco in Release.
- Nessuna prova nella finestra: i loghi nell'elenco dopo l'aggiornamento, la riga che perde o prende il logo quando
  arriva il corpo, il riquadro, la scheda della persona, le notifiche. La versione di prova non è stata avviata,
  perché cercherebbe i loghi su siti veri.
- Se verso un percorso di rete esce davvero l'autenticazione NTLM: visto solo che Windows cerca il server.
- I loghi veri dei siti e dei marchi: un solo certificato del marchio vero (DigiCert), nessuna icona SVG scaricata da
  un sito. I certificati di SSL.com e i Common Mark Certificate non sono stati provati.
- Le Authentication-Results vere: `Authenticated` è provato con intestazioni scritte a mano nelle forme di Gmail e
  di Microsoft, e con quelle delle 654 copie della cartella di prova solo nei conteggi; un server IMAP generico no.
- Una rete con NAT64 o con un proxy vero.
- Le pagine principali di siti veri sotto i tetti dell'analisi: provate solo pagine di documentazione; se una pagina
  vera li supera, quel sito resta con `favicon.ico`.
- Il codice di `b2589dc` com'era per S-03 e S-04 (vedi S-03): provato il codice corretto con i controlli tolti.

## Blocco 3: chi scrive davvero (`Core/SenderCheck.cs`, `Core/AddressBook.cs`)

Fatto il 4 ottobre 2026. "Prima" è il commit `05ffacc`. Ogni punto con i suoi controlli lanciati nella Missiva di
Debug prima e dopo la correzione (`--prova-audit`). I confronti sulla posta vera con un file temporaneo, tenuto fuori
dal repository (`C:\Progetti\Missiva\loop\audit-III`), che legge in memoria la copia dei dati di prova (4 account, 654
copie dei corpi, 1.056 messaggi fra elenchi salvati e copie, rubrica di 364 voci di cui 214 persone a cui si è
scritto) e scrive solo conteggi. Nessuna richiesta di rete, la versione di prova non è stata avviata.

### Punti dei rapporti

- **S-23** (un ";" fra virgolette, o il primo `dmarc=`, tolgono la fascia rossa). CONFERMATO SU WINDOWS, CORRETTO.
  - Prima: con l'intestazione del rapporto (`smtp.mailfrom="a;dmarc=pass"@dominio-ostile; dmarc=fail
    header.from=banca`) `Parse` dava DMARC "pass" e nessuna fascia; con due `dmarc=` valeva il primo.
  - Trovato in più, stessa famiglia. Gmail ripete il mittente della busta, scelto da chi manda, anche nel commento
    dello SPF, e nei commenti le virgolette non contavano: una parentesi nel mittente (`"a("@dominio`) lasciava il
    commento aperto e tutto quello che seguiva spariva, `dmarc=fail` compreso (nessuna fascia); un commento chiuso
    prima del tempo (`"a);dkim=pass header.d=banca;x=("@banca`) faceva valere il `dkim=pass` inventato (niente fascia
    rossa con SPF fallito, e autenticato per il logo); una proprietà scritta dentro il valore di un'altra
    (`smtp.helo="x smtp.mailfrom=banca"`) si leggeva come vera.
  - Ora: i risultati si dividono ai ";" fuori dalle stringhe fra virgolette e dai commenti, con "\" che protegge il
    carattere dopo (`SenderCheck.Split`, RFC 8601 e 5322); il metodo è solo il primo nome=valore di ogni risultato; le
    proprietà si leggono dalle parole del risultato, divise fuori dalle virgolette (`Words`, `Property`), e una scritta
    due volte con valori diversi non vale; fra due risultati di DMARC o di SPF in disaccordo vale il peggiore (le
    firme DKIM sono più d'una per natura: restano tutte, come prima). Una virgoletta o una parentesi mai chiusa, o una
    virgoletta dentro un commento, dicono che lì c'è qualcosa scelto da chi manda: da quel punto si divide a ogni ";",
    dei risultati che cominciano dopo valgono solo quelli non superati, e l'intestazione (`Results.Malformed`) non
    basta per dire autenticato (`Authenticated`, quindi nemmeno per il logo). Una passata, al più due: 1,1 MB con
    300.000 parentesi e virgolette mai chiuse in 50-110 ms.
  - Le intestazioni vere: 641 Authentication-Results in 638 copie (Gmail 571, Microsoft 67). Lettura vecchia e nuova
    uguali in tutti i campi per 641 su 641 (232 con virgolette, 639 con commenti, nessuna con "\", nessuna con due
    risultati di DMARC o di SPF, nessuna "scritta male"). Fascia rossa: 4 prima, 4 dopo, le stesse. Autenticato per il
    dominio del From: 620 su 624 prima e dopo, gli stessi.
  - Controlli: "S-23, ..." (10; 8 non passavano con il codice di prima, gli altri due sono le forme vere e i tempi).
- **S-24** (la fascia gialla "nome di un contatto" si aggira). CONFERMATO SU WINDOWS in parte, CORRETTO.
  - (a) Stesso servizio di posta personale: CONFERMATO. "Mario Rossi" noto come un indirizzo di gmail.com, da un
    altro indirizzo di gmail.com: nessuna fascia. Ora dentro la posta personale (`Settings.ImageKey`) conta
    l'indirizzo intero; per le organizzazioni resta lo stesso dominio o un sottodominio.
  - (b) Caratteri invisibili: NON CONFERMATO. Il confronto di prima era quello della lingua di Windows
    (`CurrentCultureIgnoreCase`), che ignora già spazio a larghezza zero, cambi di direzione del testo e trattino
    facoltativo: con quelli la fascia c'era. Lettere sosia: CONFERMATO. Passavano la o cirillica, la a cirillica con
    la omicron greca, la M greca con la i ucraina, le lettere a larghezza intera, un accento aggiunto, uno spazio
    unificatore, la elle per la i, lo zero per la o. Ora i nomi si confrontano ridotti a quello che si vede
    (`SenderCheck.Skeleton`: forma di compatibilità di Unicode senza accenti aggiunti, senza caratteri invisibili, di
    formato e di controllo, cirillico e greco che imitano il latino, minuscole, i/l/1, o/0, "rn"/"m", spazi ridotti a
    uno), calcolato una volta per voce della rubrica.
  - Falsi allarmi tolti: nessuna fascia se l'indirizzo è un proprio account o un altro proprio indirizzo
    (`Settings.Accounts`, `Identities`); prima scattava. Resta com'era: nessuna fascia per un indirizzo a cui si è
    già scritto.
  - La posta vera: su 1.056 messaggi (249 mittenti) la fascia c'è per 1 messaggio prima e per 1 dopo, lo stesso.
    Nessuna fascia in più.
  - Controlli: "S-24, ..." (4; 3 non passavano prima).
- **S-25** (un nome che contiene un indirizzo diverso non dà avvisi, e l'indirizzo vero si nasconde). CONFERMATO SU
  WINDOWS, anche l'effetto del CSS che il rapporto aveva solo letto: nella pagina di prima fotografata a 420 DIP con
  Edge senza finestra restava "assistenza@banca-prova.exa…" e dell'indirizzo vero non si vedeva nulla. CORRETTO.
  - `SenderCheck.NameAddress`: l'indirizzo scritto nel nome (anche fra parentesi, con la chiocciola a larghezza
    intera, solo "@dominio"), o un sito scritto per esteso ("www.banca.example", "Banca.example"), quando non è
    l'indirizzo del mittente né della stessa organizzazione; dentro la posta personale vale solo lo stesso indirizzo.
    I siti solo con una fine di dominio fra le più comuni (`CommonEndings`: "Dr.Rossi" e "Mario.Rossi" non lo sono).
    Una passata sul nome, senza espressioni regolari.
  - `Address.Display`, calcolato una volta per indirizzo: in quel caso è l'indirizzo vero. Da lì lo prendono
    l'elenco (`MessageItem.Sender`, che ora tiene il suo testo: il disegno della riga non calcola nulla), le
    notifiche, i messaggi chiusi di una conversazione, le schede, i suggerimenti della ricerca.
  - Nel riquadro: fascia gialla "Il mittente si presenta come «...», ma questo messaggio arriva da un altro
    indirizzo: ...", e in grassetto l'indirizzo vero, che va a capo invece di accorciarsi (classe `real`). Dalla
    notifica niente Rispondi veloce (`QuickReplyRisk`).
  - Caratteri che non si vedono: `Address.Name` passa da `SenderCheck.CleanName` (via cambi di direzione del testo,
    larghezza zero, trattino facoltativo, riempitivi, caratteri di controllo; spazi e a capo ridotti a uno spazio):
    un punto solo, per ogni provider e per la copia locale.
  - La posta vera: 250 mittenti. Dei 9 nomi con una chiocciola nessuno cambia (lo stesso indirizzo o la stessa
    organizzazione). Dei 20 nomi con un punto e senza chiocciola 1 ha il sito di un'altra organizzazione: 1 messaggio
    su 1.056 con la fascia nuova e l'indirizzo al posto del nome. Nessun nome della rubrica ha caratteri da togliere.
  - Foto (pagina di `--page ... --headers`, Edge senza finestra, in `C:\Progetti\Missiva\loop\audit-III\foto`): a 420
    e a 900 DIP, chiaro e scuro, un nome che è un indirizzo, uno lungo con un indirizzo vero lungo (a 420 su tre
    righe, intero), un mittente normale (com'era). Guardate una per una.
  - Controlli: "S-25, ..." (6; 4 non passavano prima; gli altri: i nomi che non ingannano restano com'erano, e il
    testo della riga è lo stesso oggetto a ogni lettura).
- **F-12** (solo la parte "trovato leggendo": le immagini esterne nella Posta indesiderata; Blocca mittente e
  Segnala phishing non sono di questo blocco). CONFERMATO nel codice e con un controllo sulla decisione, CORRETTO.
  - Prima le due decisioni (`ReadingView.FetchBodyAsync` e `Render`) guardavano solo il mittente. Portate in una
    funzione (`ReadingView.LoadsImages`) che ricalcava il codice di prima, il controllo dava le immagini caricate da
    sole nella Posta indesiderata per un mittente consentito (cartella, etichetta di Gmail nei risultati di una
    ricerca, busta di posta certificata).
  - Ora nella Posta indesiderata (`InJunk`) mai da sole. "Mostra immagini" c'è ancora: lì vale per quel messaggio
    finché resta aperto (`imagesOnce`) e non ricorda il mittente; il suggerimento del pulsante lo dice.
  - Controlli: "F-12, ..." (2; il primo non passava con la decisione di prima; il secondo è la pagina).

### Che cosa cambia per chi usa Missiva

- Fasce rosse: le stesse di prima sulla posta di oggi. In più per i messaggi costruiti come sopra.
- Fascia gialla "nome di un contatto": anche da un altro indirizzo dello stesso servizio di posta personale (un
  amico che scrive dal suo secondo indirizzo di Gmail la prende, finché non gli si scrive lì), e con le lettere
  sosia. Non più per i propri indirizzi. Sulla copia di prova: nessuna in più.
- Fascia gialla nuova "Il mittente si presenta come ...". Per quei mittenti l'elenco, le notifiche e i messaggi
  chiusi mostrano l'indirizzo al posto del nome. Sulla copia di prova: 1 mittente su 250.
- Un marchio che si firma con il nome di un sito e scrive da un altro dominio (il nome "Negozio.example" da un
  servizio di invio con un dominio suo) prende quella fascia e si vede con l'indirizzo.
- Nella Posta indesiderata le immagini esterne non si caricano più da sole, nemmeno per i mittenti consentiti.
- Un mittente della busta con le virgolette che Gmail ripete nel commento: il messaggio resta senza logo (non vale
  come autenticato). Nessuno fra le 641 intestazioni vere.

### Scelte fatte qui, da rivedere

- Una virgoletta dentro un commento rende l'intestazione "scritta male". Per la RFC lì è un carattere qualsiasi; la
  scelta è prudente perché non si sa come Gmail scrive nel commento un mittente con le virgolette.
- "Vale il peggiore" solo per DMARC e SPF. Per DKIM basta una firma superata, come prima.
- `Skeleton` non è l'elenco intero dei caratteri che si confondono (Unicode TR 39): cirillico, greco, larghezza
  intera, accenti, i/l/1, o/0, rn/m. Due nomi che differiscono solo per un accento ora sono lo stesso nome.
- I siti nel nome (`CommonEndings`, 24 fini di dominio): il rapporto parlava di indirizzi. Tolta questa parte, sulla
  copia di prova non cambierebbe nessun mittente.
- Nel riquadro, per un nome che inganna, in grassetto c'è l'indirizzo vero e il nome scritto sta solo nella fascia.
- `CleanName` toglie tutti i caratteri di formato, anche quelli che uniscono le emoji composte (una famiglia diventa
  tre facce) e il separatore del persiano.
- "Mostra immagini" nella Posta indesiderata vale finché il messaggio resta aperto: riaprendolo si preme di nuovo.

### Non verificato

- La Missiva installata non è stata eseguita e la versione di prova non è stata avviata: nessuna prova nella
  finestra. La fascia e l'intestazione si sono viste solo nella pagina di `--page`, fotografata con Edge senza
  finestra; l'elenco, le notifiche, i messaggi chiusi e Rispondi dalla notifica solo nei controlli.
- Che un server vero (Gmail, Exchange) accetti un mittente della busta con virgolette, ";" o parentesi e lo riscriva
  così nella Authentication-Results: nel rapporto era SOSPETTO, e lo resta. Come Gmail lo scrive nel commento non
  si sa.
- Lo scorrimento dell'elenco non è stato rimisurato (`scorri N`): il testo della riga si calcola una volta per riga
  (controllo), ma i tempi a fotogramma non sono stati presi.
- F-12 nella finestra: che `Render` e `FetchBodyAsync` usino `LoadsImages` si legge nel codice; il pulsante "Mostra
  immagini" premuto su un messaggio della Posta indesiderata, e le immagini di nuovo bloccate riaprendolo, non sono
  stati provati. Nessuna richiesta di immagini vera è stata osservata, né prima né dopo.
- Un account IMAP generico e un account di lavoro: i confronti sulla posta vera sono di Gmail e Outlook.com.
- I nomi della rubrica salvati prima con una lettera sosia o un indirizzo di un altro nel nome restano com'erano
  (`AddressBook.Add` non li rifiuta): i suggerimenti dei destinatari non sono stati guardati.
- Le iniziali e il colore del cerchio vengono ancora dal nome scritto, anche quando si mostra l'indirizzo.

## Blocco 4: velocità di tutti i giorni (primo gruppo)

Fatto il 4 ottobre 2026. "Prima" è il commit `918790e`. I rapporti sono due e dicono cose sovrapposte: `velocita.md`
(qui v1) e `velocita-2.md` (qui v2). Due modi di misurare. Dove bastava contare, i server finti della suite (`SpeedAuditTests`,
9 controlli: i comandi arrivati al server IMAP finto, le notifiche, i calcoli). Dove serviva la finestra, la versione di
prova (`strumenti\prova.ps1`: la Debug su una copia dei dati, con `MISSIVA_NO_READ`, che legge la casella vera e non ci
scrive), pilotata con `MISSIVA_KEYS` e letta dalle righe "tempi:" e "interfaccia lenta:" del suo registro; le scritture
solo nel registro, o in coda con `--senza-rete` (coda della copia, mai partita). Sette avvii della prova, due senza rete.
Comandi nuovi di `MISSIVA_KEYS`, solo in Debug: "scegli 2 3 4", "sole", "immagini", "anteprima", "arrivo di prova
vecchio". I numeri sono di poche prove ciascuno (da 1 a 12): dicono l'ordine di grandezza, non una media.

### Punti dei rapporti

- **v1 V-01 = v2 V-18** (la cache delle strutture `known` si svuota tutta oltre 1000 voci). CONFERMATO SU WINDOWS sul
  server IMAP finto, CORRETTO (`9a0cda7`). Tre messaggi copiati sul PC, poi 1.100 messaggi elencati in un'altra
  cartella (22 pagine): prima 0 su 3 si aprivano ancora dalla copia su disco e il clic costava 2 UID FETCH; dopo 3 su 3
  e nessuna. Ora `SummaryCache` (in `ImapProvider.cs`, usata anche da Gmail): 2.000 voci, esce quella elencata o letta
  da più tempo. Controlli: "V-01/V-18, ..." (2).
- **v2 V-05, v1 V-09** (Segna letto o da leggere su molti messaggi rifà icona, area di notifica e registro per ogni
  messaggio). CONFERMATO SU WINDOWS nella prova senza rete, CORRETTO (`d3bb838`). 50 messaggi della Posta in arrivo
  scelti, Ctrl+U e Ctrl+Q due volte: prima 91-127 ms di interfaccia ferma per azione ("interfaccia lenta: blocco di
  95-134 ms") e 50 righe "numero sull'icona"; dopo 0-1 ms, una riga, nessun blocco, e il numero giusto (55 e 5). Ora
  `AccountItem.CountRead` cambia i non letti una volta per cartella (il contatore sull'intestazione dell'account resta
  giusto: controllo), le righe si cercano una volta sola, `UpdateBadge` accorpa i cambi in un solo aggiornamento dopo
  il disegno, l'immagine del numero si tiene per testo e colore. La riga "numero sull'icona" resta, una per
  aggiornamento. Il clic su un solo messaggio da leggere (v1 V-09): icona, area di notifica e registro ora si rifanno
  dopo il disegno e non dentro il clic, ma il guadagno NON è misurato (nella prova `MISSIVA_NO_READ` salta quel
  percorso). Controlli: "V-05, ..." (2).
- **v2 V-06** (account IMAP: ogni salvataggio di una bozza riscarica e ricarica gli allegati). CONFERMATO SU WINDOWS sul
  server IMAP finto, CORRETTO (`6a80ae4`). Secondo salvataggio di una bozza con due allegati: prima 4 UID FETCH (due
  strutture e due parti) prima dell'APPEND, dopo nessuna. Come in Gmail: sotto 5 MB il contenuto resta in
  `Attachment.Content`; una bozza ripresa dalle Bozze li riscarica una volta. `Heavy` (salvataggio automatico al più
  ogni 5 minuti oltre 5 MB di allegati) ora vale anche per IMAP: letto nel codice, senza controllo. Controlli: "V-06,
  ..." (2).
- **v1 V-04 = v2 V-13** (Elimina, Archivia e Sposta passano dal riquadro vuoto; con più righe un messaggio si apre per
  sbaglio). CONFERMATO SU WINDOWS, CORRETTO (`a35749a`).
  - Una riga, "clic 3" e Canc con `lampi.ps1`: prima 2 cambi di selezione, il riquadro vuoto una volta, e nei
    fotogrammi il segnaposto "Seleziona un elemento da leggere" da 0,25 a 0,5 s dopo il tasto; dopo 1 cambio, mai
    vuoto, due soli fotogrammi (il messaggio di prima, poi quello dopo).
  - Tre righe e trenta righe ("scegli", Canc): prima 4 e 31 cambi di selezione, e il riquadro apriva due messaggi, uno
    dei quali di una riga che stava uscendo (il suo corpo letto dal disco per niente: "corpo dalla copia su disco (6
    KB)"); dopo 1 cambio e un solo messaggio.
  - "Segnato letto per sbaglio" durante Elimina: NON CONFERMATO. Il messaggio aperto per sbaglio veniva superato prima
    di arrivare a `MarkRead`. Con "quando la selezione cambia" diventa letto il messaggio che si stava guardando, prima
    e dopo: è quello che quella scelta chiede.
  - Annulla di più righe (v2, lì SOSPETTO): CONFERMATO. Dopo Ctrl+Z di 30 righe il riquadro apriva il primo messaggio
    tornato e il registro diceva "prova: segnerei letto ..." (con "segna come letto subito", nella Missiva installata
    sarebbe stato segnato). Ora le righe tornano scelte e il riquadro resta vuoto; con una riga sola il riquadro la
    apre, con un solo cambio di selezione.
  - Correzione: `MessageRows.After` trova la riga da scegliere prima di togliere le altre. È la stessa che prima
    restava scelta dopo: 400 casi su 400 nel controllo (ogni riga, scelte a caso, conversazioni aperte, tutte le
    righe). Controllo: "V-04/V-13, ...".
- **v1 V-08 = v2 V-30** (chiudere l'anteprima fa due raccolte complete). CONFERMATO SU WINDOWS, CORRETTO (`47d6edd`).
  Anteprima di un PDF, poi clic su un altro messaggio: prima "anteprima chiusa, memoria liberata" 46-50 ms dentro
  l'apertura, e "clic, pagina mostrata" a 134-136 ms; dopo il messaggio a 58-66 ms e una raccolta sola di 21-29 ms tre
  secondi dopo, non mentre il riquadro apre un messaggio. Memoria di Missiva dopo la chiusura: prima 227, 224, 222 MB;
  dopo, quattro anteprime di fila, 237, 233, 228, 227 MB (non cresce).
- **v2 V-08** (regole: il testo del messaggio ricalcolato per ogni regola). CONFERMATO SU WINDOWS sul server IMAP
  finto, CORRETTO (`604b41f`). Tre regole sul testo e quattro messaggi: prima 12 analisi dell'HTML, sul thread di chi
  chiama; dopo 4, su un thread del pool. Il testo si calcola ancora solo se le altre condizioni della regola valgono.
  Controllo: "V-08, ...", con un Dispatcher come nell'app (`OnUiThread`).
- **v2 V-12, v1 V-17 prima parte** (ogni messaggio nuovo rifà i gruppi dell'elenco). CONFERMATO SU WINDOWS, CORRETTO
  (`83ac18e`). Il costo è più piccolo della stima dei rapporti (25-60 ms): con 280 righe caricate, "arrivo di prova"
  in cima, 14-16 ms con i gruppi rifatti e 4-10 senza. Ma con l'elenco scorso in giù rifare i gruppi portava la vista
  altrove: da 14.512 a 24.005 px, prima riga visibile dalla 223 alla 144 (e nella prima sessione da 11.402 a 31.380);
  senza, la stessa riga allo stesso pixel, 3 prove su 3, in 0,2 ms. Ora (`PrependRows`) i gruppi si rifanno solo se una
  riga apre un gruppo che l'elenco non ha. Su questo PC WPF mette al posto giusto anche il gruppo nuovo (controllo:
  l'elenco senza e con i gruppi rifatti è uguale): lì si rifanno lo stesso, per prudenza. Controllo: "V-12, ...".
- **v1 V-14, v2 V-19 seconda metà** (sole, "Mostra immagini" e tema rifanno la pulizia dell'HTML sul thread
  dell'interfaccia). CONFERMATO SU WINDOWS, CORRETTO (`efc8502`). Il sole quattro volte su una newsletter da 211 KB:
  prima "pagina costruita" 26-45 ms (una volta un blocco di 55 ms), dopo 0 ms, con la pulizia fatta prima in sottofondo
  in 11-22 ms (`RenderWarmAsync`). "Mostra immagini" e il cambio di tema passano dalla stessa funzione: non premuti
  nella prova ("Mostra immagini" su una email vera caricherebbe le immagini del mittente).
- **v2 V-09** (Rispondi e Inoltra: il testo dell'originale sul thread dell'interfaccia). CONFERMATO SU WINDOWS,
  CORRETTO (`7fae740`). Rispondi a una newsletter da 211 KB: prima 26 ms sul thread dell'interfaccia, dentro i 0,27 s
  dell'apertura della finestra; dopo 18 ms in sottofondo. Lo stesso per Rispondi con riunione (non provato). La parte
  quadratica sui `<br>` era già del blocco 1.
- **v1 V-05 = v2 V-27** (Invio rifà la ricerca dei suggerimenti). NON VERIFICATO, NON CORRETTO: non c'è stato tempo.

### Trovato in più

- Corretto, negli strumenti: i comandi di `MISSIVA_KEYS` giravano alla priorità di serie dei timer (Background), e
  dopo ogni await il lavoro riprende alla priorità di chi l'ha avviato. Sotto carico le riprese aspettavano tutto il
  resto: un corpo letto dal disco in 2 ms veniva ripreso dopo 0,3-0,8 s. Ora a priorità Input, come i tasti e i clic
  veri (`DevTools.WatchKeys`). I tempi "clic" presi prima con `MISSIVA_KEYS` possono essere più lunghi del vero.
- Non corretto: dopo Canc o Ctrl+Z (non dopo un clic) circa una volta su due il messaggio successivo compare dopo
  0,35-0,8 s, benché il corpo sia letto dal disco in 1-16 ms e l'HTML pulito in 0-16 ms: la ripresa sul thread
  dell'interfaccia arriva 0,51-0,53 s dopo, senza righe "interfaccia lenta". Succede uguale con l'ordine di prima
  (provati tutti e due nella stessa sessione: "pagina mostrata" 73-820 ms prima, 152-645 dopo, sei prove l'uno). La
  causa non è trovata, e non si sa se succede con i tasti veri: le righe "HTML pulito in sottofondo (N KB, da 0 a X
  ms) Y ms" ora lo dicono (X è il lavoro, Y quando l'interfaccia lo riprende).
- Non corretto: `ImapProvider.Dispose` chiude le connessioni con un LOGOUT che aspetta la risposta sul thread di chi
  chiama, fino ai 60 s di MailKit se il server non risponde. Visto scrivendo il controllo delle regole.

### Che cosa cambia per chi usa Missiva

- Elimina, Archivia e Sposta: il riquadro passa dal messaggio a quello dopo, senza il foglio "Seleziona un elemento da
  leggere" in mezzo. Finché il messaggio dopo non arriva resta quello di prima, sbiadito, come cambiando messaggio.
- Annulla dopo aver tolto più righe: tornano scelte tutte e il riquadro resta vuoto. Prima mostrava il primo messaggio
  tornato e, se era da leggere, lo segnava letto.
- Un messaggio nuovo che arriva mentre si scorre l'elenco in giù non sposta più la vista.
- Segna letto o da leggere su molti messaggi non ferma la finestra. Il numero sull'icona e i contatori di "Tutti gli
  account" si aggiornano un attimo dopo il disegno invece che durante.
- Dopo l'anteprima di un allegato la memoria si libera tre secondi dopo, non subito.
- Account IMAP: le bozze con allegati si salvano senza riscaricarli; con più di 5 MB di allegati il salvataggio
  automatico è al più ogni 5 minuti, come in Gmail.
- I messaggi già copiati sul PC continuano ad aprirsi dalla copia anche dopo molte pagine o molte conversazioni.

### Scelte fatte qui, da rivedere

- `SummaryCache` tiene 2.000 voci (prima il tetto era 1.000, poi si svuotava): qualche MB in più al massimo.
- Dopo Annulla di più righe il riquadro è vuoto: è quello che propone v2. L'alternativa è lasciare il messaggio che si
  vedeva.
- La raccolta dopo l'anteprima aspetta tre secondi, e se il riquadro sta aprendo un messaggio riprova fino a cinque
  volte (15 s), poi la fa comunque.
- I gruppi dell'elenco si rifanno ancora quando una riga apre un gruppo nuovo, anche se qui WPF lo mette al posto
  giusto da solo.
- La riga "numero sull'icona" resta nel registro della versione installata, una per aggiornamento.

### Non verificato

- La Missiva installata non è stata eseguita. Tutto quello che riguarda la finestra è visto nella versione di prova,
  con comandi mandati da `MISSIVA_KEYS` e non con tastiera e mouse.
- V-04: Archivia e Sposta passano dalla stessa funzione di Elimina (`RemoveRows`) ma è stato premuto solo Canc. Non
  provati: Elimina dalla finestra di un messaggio, dalle azioni rapide di una riga non scelta, in una conversazione
  aperta (lì solo il controllo su `After`), nella vista di tutti gli account. Il fuoco della tastiera dopo Canc è
  sull'elenco, come prima (dal registro); le frecce dopo Canc non sono state provate.
- V-12: provato con la riga finta di "arrivo di prova", non con un messaggio arrivato davvero; l'animazione
  d'ingresso della riga non è stata fotografata. La vista di tutti gli account usa la stessa funzione: non provata.
- V-05: un clic su un messaggio da leggere (v1 V-09), e gli account veri in linea (provato senza rete, con la coda).
- V-06: un server IMAP vero e i tempi di un salvataggio; `Heavy` per IMAP.
- V-01/V-18: quanto spesso la cache si svuotava nell'uso vero (nel rapporto era SOSPETTO, e lo resta).
- V-08: "Esegui ora" e il controllo dei nuovi messaggi nella finestra, con newsletter vere.
- V-14/V-19: "Mostra immagini" e il cambio di tema di Windows. La prima metà di v2 V-19 (i corpi scaricati in anticipo
  puliti prima del clic) non è di questo blocco.
- V-08/V-30: un'anteprima con molte pagine o molte immagini (provato un PDF di una pagina, 37 KB).
- I commit da `a35749a` a `efc8502` sono stati compilati uno per uno, ma la suite e le misure sono dell'ultimo.

## Blocco 5: difetti veri

Fatto il 4 ottobre 2026. "Prima" è il commit `3bc9967`. Il rapporto è `funzionalita.md`: oltre alle funzioni che mancano
elenca difetti del codice, e questo blocco sono quelli. Ogni punto è stato prima cercato nel codice di oggi e, dove si
poteva, visto fallire con un controllo (con la logica di prima, o togliendo per un momento la correzione); poi corretto;
poi il controllo passa. Controlli: `DefectAuditTests` in `ProvaBozze.Audit.cs`, 41, da soli in due secondi con
`Missiva.exe --prova-audit risultato.txt difetti`. I controlli sulle impostazioni lavorano in cartelle temporanee
loro, quelli sul registro in una chiave di prova poi tolta. Le finestre sono state guardate con la Debug su una cartella
di dati finta (un account IMAP sul server finto di `--imap-finto`, niente dati veri né rete) e, per il calendario, con
due avvii di `strumenti\prova.ps1` con `--calendario-finto`. Suite completa: 703 controlli, "tutto bene".

### Punti del rapporto

- **F-44 punto 1** (impostazioni.json illeggibile: si riparte da zero e il primo salvataggio lo sovrascrive).
  CONFERMATO SU WINDOWS, CORRETTO (`73d036b`). Con la logica di prima, in una cartella temporanea: file vuoto, troncato, JSON non
  valido, di zeri, "null", con un valore di una versione più nuova, sei su sei ripartivano senza account e il file
  veniva sovrascritto. Ora (`Store.Read`):
  - ogni salvataggio tiene il file di prima come `impostazioni.bak` (File.Replace); alla prima lettura riuscita dopo
    l'aggiornamento la copia nasce dal file appena letto;
  - il file che non si legge va da parte come `impostazioni-illeggibile-AAAAMMGG-HHMMSS.json` e non si tocca più; si
    riparte dalla copia di riserva, o senza account se non c'è o non si legge (allora va da parte anche lei);
  - un file che non si riesce né a leggere né a spostare (in uso da un altro programma, dopo quattro tentativi in mezzo
    secondo) blocca i salvataggi della sessione: mai sovrascritto;
  - all'avvio una finestra dice che cosa è successo, da che copia si è ripartiti e dove sono i file, con "Apri la
    cartella";
  - `Store.Save` scrive con file temporaneo, `Flush(true)` e rinomina (`SafeFile.Write`), non solleva più eccezioni e
    avvisa una volta per sessione se non riesce.
  Lo stesso `SafeFile.Write` per contatti.dat, la copia dei contatti di Outlook, coda.dat e rubrica.json. Trovato in più:
  contatti.dat e coda.dat illeggibili diventavano "nessun contatto" e "nessuna azione" e venivano sovrascritti al primo
  salvataggio; ora vanno da parte allo stesso modo (solo nel registro, senza finestra). Controlli: 9 sulle impostazioni,
  2 su contatti e coda. Costo: un salvataggio 2,6-3,2 ms contro 1,0 di prima (20 salvataggi di fila).
- **F-44 punto 4** (password o testo cifrato che non si decifra più diventa vuoto in silenzio). CONFERMATO SU WINDOWS,
  CORRETTO (`4124006`). Visto con il codice di prima: i testi cifrati rovinati tornavano vuoti e al salvataggio dopo erano persi
  (0 su 10 rimasti); la password IMAP e l'accesso a Google davano "CryptographicException: Dati non validi" senza
  rimedio. Peggio di quanto dice il rapporto: una regola di Missiva "se il mittente è ...: elimina" restava senza
  condizioni e corrispondeva a ogni messaggio. Ora:
  - al posto del cifrato che non si apre c'è "(testo cifrato non leggibile su questo PC, rif. ...)", e al salvataggio
    torna nel file il cifrato com'era (sul PC di prima si rilegge);
  - le regole con parti così non corrispondono mai;
  - un avviso all'avvio lo dice una volta (non a ogni avvio);
  - password IMAP: errore con `Fix = Password`, quindi la barra "Aggiorna password"; accesso a Google: "Accedi di
    nuovo"; segreto dell'app Google: detto con cosa fare, e la finestra Aggiungi account non si ferma più
    sull'eccezione.
  Controlli: 8.
- **F-44 punto 2** (errore d'invio con il solo OK). CONFERMATO in parte leggendo il codice, CORRETTO (`3e2147c`). Il messaggio non si
  perdeva: dopo OK la finestra di scrittura tornava com'era, e senza rete c'era già la Posta in uscita. Mancavano le
  scelte e il rimedio. Ora "Messaggio non inviato" dice che il messaggio è ancora lì e propone Riprova (oppure Aggiorna
  password o Accedi di nuovo, secondo `MailException.Fix`, che apre Gestisci account), Salva nelle Bozze e Torna al
  messaggio; lo stesso dalla Posta in uscita. Visto nella Debug con il server finto: la finestra, e Salva nelle Bozze
  che fa l'APPEND nelle Bozze. Controllo: 1 (testo e pulsanti).
- **F-44 punto 3** (WebView2 che non parte: una riga grigia uguale per ogni errore). CONFERMATO leggendo il codice, CORRETTO (`06aa2f8`).
  Ora il Runtime assente si riconosce (`WebView2RuntimeNotFoundException`, o Windows che non ne dà la versione) e
  l'avviso ha il link di Microsoft per installarlo (`go.microsoft.com/fwlink/p/?LinkId=2124703`, quello che c'era già
  come testo); per gli altri errori dice l'errore vero e apre la cartella dei registri. In tutti e due i casi Riprova
  ricrea il riquadro senza riavviare: visto nella Debug con l'avvio fatto fallire apposta
  (`MISSIVA_ERRORE_WEBVIEW=manca`), poi il messaggio si apre. Controllo: 1.
- **F-44 punto 5** (errori non gestiti dell'interfaccia solo nel registro). CONFERMATO leggendo il codice, CORRETTO (`06aa2f8`): una
  finestra, una volta per sessione, con il testo dell'errore e "Apri la cartella". Nel registro della prova (1,3 MB) non
  ce n'è nessuno, quindi non dovrebbe comparire spesso. La parte su `Store.Save` è nel punto 1.
- **F-44 punto 6** (elenco e ricerca non riusciti senza Riprova). CONFERMATO leggendo il codice, CORRETTO (`06aa2f8`): accanto alla riga
  dell'errore un link Riprova che rifà la stessa richiesta (cartella, tutti gli account, ricerca, ricerca in tutti gli
  account).
- **F-44 punto 7** (testo degli avvisi non copiabile). CONFERMATO leggendo il codice, CORRETTO (`06aa2f8`): il testo di `Dialog` si
  seleziona e si copia, e Ctrl+C senza selezione copia titolo e testo.
- **F-42** (calendario). CONFERMATO leggendo il codice, CORRETTO (`25f4760`). "+N altri" era un testo, e anche la vista Giorno si fermava a
  tre eventi di un giorno intero: il quarto non si apriva da nessuna parte. Ora è un pulsante che apre la vista Giorno,
  dove ci sono tutti (oltre sei la riga scorre). Ctrl+N nel calendario crea un evento; Ctrl+Maiusc+A un evento da
  ovunque e Ctrl+Maiusc+M un messaggio, come Outlook. Visto nella prova con il calendario finto: "+17 altri" premuto, il
  giorno con venti eventi, la finestra Nuovo evento da Ctrl+N e da Ctrl+Maiusc+A. Controllo: 1.
- **1-06** (Annulla riunione promette una nota che Google scarta). CONFERMATO leggendo il codice, CORRETTO (`25f4760`). Per Google la
  finestra non chiede più la nota: una conferma che dice che Google Calendar non la manda. Per Microsoft resta.
  Controllo: 1.
- **P-01** (risposte automatiche: un pubblico esterno sconosciuto letto come "tutti"). CONFERMATO SU WINDOWS, CORRETTO
  (`9883292`). Senza la correzione un `externalAudience` inventato tornava sul server come "all" anche senza toccare
  nulla. Ora il valore letto si tiene e torna com'era finché non si sceglie un altro pubblico; la finestra lo dice.
  Lo stato (`status`) sconosciuto resta letto come "spente": Disattiva deve poter spegnere. Controlli: 2.
- **P-02** (regole di Outlook: Su e Giù a metà). CONFERMATO SU WINDOWS, CORRETTO (`01a26d6`). Con il gestore finto di Graph che
  tiene i numeri delle regole e rifiuta il secondo PATCH: prima "Ordine delle regole non cambiato" con l'ordine
  vecchio, mentre sul server una regola aveva già il numero nuovo. Ora le regole si rileggono, si mostra quello che il
  server ha davvero e l'avviso dice "cambiato solo in parte, 1 di 2"; se la rilettura non riesce lo dice. Controlli: 4.
- **P-03, P-06** (nuovo accesso a Google: un permesso tolto sparisce senza avviso). CONFERMATO nel codice (tre posti
  che sostituivano gli ambiti senza confrontarli), CORRETTO (`e38a032`): `GoogleAuth.SetScopes` dice quale permesso
  manca rispetto a prima (calendario, contatti, modifica dei contatti) e come riaverlo. Vale anche per l'account
  aggiunto di nuovo da Aggiungi account, che non chiede calendario e contatti. Controlli: 3.
- **P-08** (la casella dell'avvio con Windows non legge lo stato vero). CONFERMATO leggendo il codice, CORRETTO (`58c6a62`): legge il
  valore in Run e `StartupApproved` di Windows; accesa dalla casella toglie anche lo "spento" di App di avvio.
  Controlli: 2, su chiavi di prova.
- **F-02** (app.manifest 2.0.0.0, progetto 0.3.0). CONFERMATO leggendo i file, CORRETTO (`58c6a62`): il manifesto dice 0.3.0.0. La
  versione nell'app e il controllo degli aggiornamenti, che sono la proposta di F-02, non sono di questo blocco.
  Controllo: 1.
- **F-19** (il suggerimento dice "Gmail" anche per IMAP e .eml). CONFERMATO leggendo il codice, CORRETTO (`58c6a62`). Solo la frase:
  l'ordinamento con SORT non è di questo blocco. Controllo: 1.
- **F-01** (disinstallazione con pwsh.exe). CONFERMATO SU WINDOWS per la codifica, leggendo gli script per il resto; CORRETTO per la parte chiesta (`58c6a62`): la voce di App
  installate chiama `powershell.exe` (5.1), e disinstalla.ps1 gira con 5.1 e con 7. Il sospetto del rapporto sulla
  codifica è vero: senza BOM Windows PowerShell leggeva male la "è" del messaggio finale. Né installa.ps1 né
  disinstalla.ps1 sono stati eseguiti: lo script è stato analizzato dai due PowerShell (0 errori di sintassi), e il
  collegamento al canale è stato provato su un canale di prova con lo stesso codice (da 5.1 "esci" arriva). Lo zip
  pronto per gli amici, che è la proposta di F-01, non è di questo blocco. Controllo: 1.
- **Frasi**: "Rispondi con riunione" e "Nuovo evento" dicevano che il calendario c'è solo per Microsoft: ora Microsoft
  e Google (`58c6a62`, `25f4760`). LEGGIMI.md (`e868875`): le cinque frasi del rapporto, verificate nel codice, più
  la riga di Persone ("per Gmail i contatti stanno in Missiva": sono quelli di Google) e la descrizione delle notifiche
  (non hanno più la casella della risposta); aggiunte le immagini nella Posta indesiderata, il mittente che si presenta
  con l'indirizzo di un altro, la copia di riserva delle impostazioni.

### Lasciati dai blocchi di prima

Tutti e tre CONFERMATI SU WINDOWS con un controllo che prima non passava (per il terzo, una misura) e CORRETTI (`708a53d`), 4 controlli:

- Blocco 4: la chiusura di un provider IMAP aspettava il LOGOUT sul thread di chi chiamava. Con il server finto muto:
  60.004 ms. Ora in sottofondo con cinque secondi di tetto, anche per Gmail: 0 ms.
- Blocco 3: la rubrica imparava "assistenza@banca-prova.example" come nome di un altro indirizzo. Ora no.
- Blocco 1: `HtmlToFlow` analizzava l'HTML delle bozze senza tetti (700 font mai chiusi e 700 paragrafi, 21 KB: 259 ms,
  e cresce con il quadrato). Ora passa da `MailHtml.ParseWithinLimits`; oltre i tetti l'editor riceve il solo testo.

### Che cosa cambia per chi usa Missiva

- Finestre nuove, tutte rare: all'avvio "Le impostazioni di Missiva non si leggevano" (o "non c'erano più"), e "Alcuni
  dati salvati non si leggono su questo PC"; "Impostazioni non salvate"; "Missiva ha avuto un errore imprevisto";
  "Google ha tolto un permesso a Missiva"; "Ordine delle regole cambiato solo in parte".
- Accanto a impostazioni.json compare impostazioni.bak.
- "Messaggio non inviato" ha tre pulsanti: Riprova (o Aggiorna password, Accedi di nuovo), Salva nelle Bozze, Torna al
  messaggio.
- Il testo di tutti gli avvisi si seleziona e si copia.
- Elenco o ricerca non riusciti: link Riprova accanto all'errore.
- Calendario: "+N altri" si preme; Ctrl+N crea un evento (per un messaggio dal calendario: Ctrl+Maiusc+M); annullando
  una riunione di Google non si chiede più la nota.
- Impostazioni, Avvio: la casella dice lo stato vero; spenta da Windows, compare spenta.
- Suggerimenti dei destinatari: chi scrive con l'indirizzo di un altro al posto del nome resta senza nome.

### Scelte fatte qui, da rivedere

- La copia di riserva è il salvataggio precedente, non una copia per avvio: se il file si rovina si perde al più
  l'ultima modifica. Una sola copia, senza storia.
- I file messi da parte non si cancellano mai: se succede più volte si accumulano nella cartella dei dati.
- Con il file delle impostazioni in uso da un altro programma la sessione non salva nulla. L'alternativa era salvare
  appena il file si libera.
- Il segnaposto dei cifrati non leggibili si vede nei nomi dei modelli, nelle regole e nei messaggi posposti. Se si
  modifica una regola che lo contiene, il cifrato di prima si perde (resta la parola nuova).
- contatti.dat e coda.dat illeggibili: da parte, ma senza finestra (solo il registro).
- L'errore non gestito dell'interfaccia apre una finestra, una volta per sessione. Se desse fastidio si toglie in una
  riga (`App.OnStartup`).
- "+N altri" porta alla vista Giorno, come il numero del giorno; Outlook apre un elenco sopra il mese.
- Ctrl+N in Persone apre ancora un messaggio, non un contatto.
- Riaccendere l'avvio dalla casella cancella il valore di Missiva in `StartupApproved` di Windows.
- Il LOGOUT in sottofondo ha cinque secondi: uscendo da Missiva può non partire, e il server chiude da sé.

### Non verificato

- La Missiva installata non è stata eseguita, e installa.ps1 e disinstalla.ps1 nemmeno. La disinstallazione da App
  installate, intera, resta da provare (P-09 del rapporto).
- Le finestre dell'avvio (impostazioni illeggibili) viste una volta, con una cartella finta senza account. Non viste:
  "Alcuni dati salvati non si leggono", "Impostazioni non salvate", l'errore imprevisto, il permesso di Google tolto,
  l'ordine delle regole: di queste è controllato il testo o la logica, non l'aspetto.
- Un vero cambio di profilo di Windows o un vero spostamento su un altro PC: i cifrati sono stati rovinati apposta. La
  barra "Aggiorna password" sotto l'account con la password non leggibile non è stata fotografata (il controllo guarda
  l'errore e il suo rimedio).
- Un'interruzione di corrente vera durante un salvataggio, e il disco pieno.
- Errore d'invio: Riprova e i pulsanti Aggiorna password e Accedi di nuovo non premuti (premuto Salva nelle Bozze);
  l'errore dalla Posta in uscita non provocato.
- Il link Riprova dell'elenco e della ricerca: non visto a schermo.
- WebView2: il Runtime davvero assente (qui c'è) e il link di Microsoft non aperto. L'altro errore (cartella bloccata,
  disco pieno) solo come testo.
- Calendario: la finestra dell'annullamento per un account Google non vista (controllato il testo); Ctrl+N e
  Ctrl+Maiusc+A mandati con `MISSIVA_KEYS`, non dalla tastiera.
- P-01 e P-02: con Outlook vero no, solo con il gestore finto di Graph. Il valore sconosciuto è inventato.
- P-03/P-06: un accesso vero a Google con una casella tolta.
- P-08: la casella nella Missiva installata (nella prova la chiave Run non si scrive e resta l'impostazione), e
  l'accordo con Gestione attività di Windows.
- Il testo selezionabile di `Dialog` con l'Assistente vocale.

## Blocco 6: funzioni (allegato dimenticato, annulla iscrizione, allegati grandi con Microsoft)

Fatto il 4 ottobre 2026. "Prima" è il commit `497d293`. Tre funzioni che il rapporto `funzionalita.md` dà per mancanti
(F-14, F-11, F-15): per ognuna si è prima guardato nel codice di oggi che mancasse davvero e dove si aggancia, poi è
stata fatta, con i suoi controlli e uno sguardo a schermo. Controlli: `FeatureAuditTests` in `ProvaBozze.Audit.cs`, 36,
da soli in un secondo con `Missiva.exe --prova-audit risultato.txt funzioni`. Solo dati finti e server finti: nessuna
richiesta di rete vera, nessun account vero. Le finestre sono state guardate con la Debug su una cartella di dati finta
(un account IMAP sul server finto di `--imap-finto`), la pagina del riquadro con `--page ... --headers` ed Edge senza
finestra, in chiaro e in scuro. Suite completa: 739 controlli, "tutto bene".

### F-14, avviso di allegato dimenticato (`3bec544`)

- **Che cosa c'è.** A Invia e a Pianifica invio, dopo la domanda sull'oggetto (`ComposeWindow.ReadyAsync`): se il testo
  parla di un allegato e non ce n'è nessuno, "Hai dimenticato l'allegato?" con la parola trovata, **Invia comunque** e
  **Torna al messaggio**. Vale per messaggi nuovi, risposte e inoltri.
- **Parole** (`ComposeWindow.AttachmentMention`): allego, alleghiamo, allegato e le sue forme ("in allegato"), allegando,
  accluso, attached, attachment, attaching, enclosed. Parole intere. Non "allegare" né "attach" (di solito sono richieste
  all'altro), non dopo senza, nessun, non, no, without.
- **Che cosa non conta**: le righe della firma (quella messa da Missiva; per una bozza ripresa dalle Bozze, che ha la
  firma nel testo senza segno, le righe uguali a quelle delle firme di chi scrive), le righe citate con ">" e tutto ciò
  che segue l'intestazione di una citazione ("Il ... ha scritto:", "On ... wrote:", "Da:" con "Inviato:" sotto,
  "-----Messaggio originale-----"). In una risposta appena aperta la citazione sta fuori dal documento; in una bozza
  ripresa di una risposta è nel testo, e per questo si taglia lì.
- **Che cosa conta come allegato**: i file aggiunti, quelli della bozza, quelli dell'originale di un inoltro (finché la
  bozza non è salvata partono con lui; dopo sono fra quelli della bozza, e se li togli l'avviso torna), le immagini messe
  nel testo fuori dalla firma.
- **Come si spegne**: Impostazioni, Posta, Scrittura e risposta, "Avvisami se manca l'allegato di cui ho scritto"
  (`Settings.AttachmentReminder`, acceso di base).
- **Provato**: 5 controlli (13 frasi che ne parlano e 11 che no; firma con spazi cambiati e fra più firme; la firma letta
  dall'RTF e messa in un documento; 5 forme di citazione; l'impostazione accesa anche con un file di prima). A schermo:
  la domanda in chiaro e in scuro, Torna al messaggio (la scrittura resta), Invia comunque (parte: nella prova solo nel
  registro), la casella delle impostazioni che lo spegne e lo riaccende.

### F-11, annulla iscrizione (`ec6823b`)

- **Che cosa c'è.** Nei messaggi che hanno l'intestazione List-Unsubscribe, sotto il mittente: "Ricevi troppi messaggi
  da questo mittente? **Annulla iscrizione**". Il clic apre sempre una conferma che dice che cosa succederà e verso
  quale sito o indirizzo, con l'indirizzo intero; "Resta iscritto" rinuncia.
- **Tre casi** (`Unsubscribe.For`), nell'ordine:
  - *un clic* (RFC 8058), solo se il messaggio ha List-Unsubscribe-Post esatto, un indirizzo https e il server della
    casella dice che viene davvero dal dominio del mittente (`SenderCheck.Own` e `Authenticated`, gli stessi dei loghi):
    Missiva manda una POST con il corpo `List-Unsubscribe=One-Click`. Poi "Iscrizione annullata", e la riga del
    messaggio dice "Richiesta ... inviata a ..." fino alla chiusura di Missiva;
  - *mailto:*: una scrittura già compilata con un solo destinatario, l'oggetto ("unsubscribe" se manca) e il testo del
    link, mai Cc e Ccn; parte solo se premi Invia;
  - *solo https*: la pagina nel browser, dopo la conferma, con i controlli dei link (`OpenLink`, `Safety.LinkWarning`).
- **La POST** (`Unsubscribe.OneClickAsync`, l'unico punto da cui parte, dopo la conferma): con il gestore dei loghi
  (`SenderLogos.CreateHandler`: senza cookie, senza proxy, solo verso indirizzi pubblici), nessun reindirizzamento
  seguito, 15 secondi, risposta non letta. Riuscita solo con una risposta 2xx; altrimenti lo dice e propone la pagina
  del mittente nel browser.
- **Mai**: nella Posta indesiderata, con la fascia rossa del mittente non verificato (o una busta PEC alterata), nelle
  bozze. Senza autenticazione mai la POST: restano la scrittura o la pagina, che fa l'utente.
- **Intestazioni**: arrivano con il corpo, senza richieste in più. Graph le ha già in internetMessageHeaders; Gmail e
  IMAP nel messaggio intero o, per quelli grandi, nella stessa richiesta delle Authentication-Results
  (`SenderCheck.FetchAsync`). Restano nella copia su disco. Solo i link fra parentesi angolari, https senza nome utente,
  con un nome di sito e sulla porta di https; oltre 8 KB, 2048 caratteri per link o 8 link non si guarda.
- **Nessuna azione sulla casella**: niente eliminazioni, niente regole.
- **Provato**: 18 controlli. Intestazioni con solo mailto, solo https, tutti e due, con commenti e a capo, più indirizzi,
  28 scritte male o non accettabili, lunghe 9 KB; quando il comando c'è e che cosa farebbe (7 casi senza autenticazione,
  Posta indesiderata, fascia rossa, bozze); il testo della conferma; la pagina. Con il gestore HTTP finto che conta:
  zero richieste senza conferma, zero in Posta indesiderata, zero senza autenticazione, una POST esatta con la
  conferma, reindirizzamento non seguito, errore 500, sito non raggiungibile. Il gestore vero: nessun reindirizzamento,
  cookie o proxy, e il collegamento passa dal controllo degli indirizzi pubblici (fermato dal gancio di prova prima
  della ricerca del nome). IMAP finto: le intestazioni nel messaggio intero e nella stessa richiesta per quello grande;
  Graph finto: una richiesta sola. A schermo: la riga a 900 e 420 pixel, dopo la richiesta, con la fascia rossa (non
  c'è), senza intestazione (non c'è); nell'app la conferma, "Resta iscritto" (nulla nel registro), la conferma data (nella
  prova "prova: annullerei l'iscrizione con una POST a ..."), l'avviso e la riga dopo.

### F-15, allegati da 3 MB in su con gli account Microsoft (`7cd11c1`)

- **Prima**: oltre 3 MB in tutto per salvataggio o invio, "Per ora allegati e immagini possono pesare al massimo 3 MB in
  tutto", detto dopo Invia.
- **Che cosa c'è** (`GraphProvider.WriteDraftAsync`, `UploadLargeAsync`): i file sotto 3 MB partono come prima; da 3 MB
  in su a pezzi, come vuole Graph: `POST {me}messages/{id}/attachments/createUploadSession`, poi PUT dei pezzi
  all'indirizzo restituito, con Content-Range e senza il token. Pezzi da 1,25 MB (4 volte 320 KiB), letti dal file uno
  alla volta. Vale per invio, bozze, salvataggio automatico, invio pianificato, caselle condivise (`users/indirizzo/`) e
  per una foto grande messa nel testo.
- **Durante**: la riga di stato della scrittura dice "Caricamento di X: N di M MB…".
- **Non si ricarica**: un allegato già sulla bozza resta lì al salvataggio dopo e all'invio.
- **Errore a metà**: "Caricamento di X interrotto a N di M MB: l'allegato non è stato aggiunto." e il motivo. La bozza
  resta coerente: quella creata per l'occasione si elimina; su una che c'era si toglie quello che la chiamata aveva
  caricato (anche un allegato arrivato intero di cui non si è potuto leggere l'id), così il salvataggio dopo non lascia
  doppioni. Senza rete vale quello di prima: Posta in uscita, o nuovo tentativo del salvataggio automatico.
- **Annullamento**: chiudendo la scrittura (Scarta) il caricamento si ferma al pezzo in corso, e un invio non ancora
  partito non parte (`OutgoingMessage.Cancel`).
- **Limite di Outlook**: 150 MB in tutto per messaggio. Lo dice la scrittura quando aggiungi il file (account Microsoft)
  e comunque il provider prima di ogni richiesta.
- **Provato**: 13 controlli con `UploadGraph`, un Graph finto con le sessioni di caricamento su un altro sito che
  controlla ogni PUT (senza token, Content-Range in fila e della misura dichiarata, pezzi multipli di 320 KiB e sotto 4
  MB) e ricompone il contenuto: uguale al file byte per byte; salvataggio dopo e invio senza PUT; errore al secondo
  pezzo del secondo file e nuovo salvataggio senza doppioni; un 429 ripetuto; allegato arrivato ma non riletto;
  annullamento al secondo pezzo su una bozza e in un invio; limite di 150 MB senza richieste; foto di 3 MB nel testo;
  casella condivisa.

### Trovato in più

- Il server IMAP finto non rispondeva a `BODY.PEEK[1.MIME]`, che MailKit chiede con il testo di un messaggio grande
  letto parte per parte: quella lettura nelle prove falliva sempre. Ora risponde, ma solo se chiesto
  (`FakeImap.TextPartMime`), perché due controlli di prima contano su quel fallimento: "ultimi 30 giorni sul PC ... non
  quello da 400 KB" e "al giro dopo ... busta e struttura del solo messaggio che manca" (`OfflineCopyTestsAsync`). Con
  il server finto che risponde i messaggi salvati sono 3 e non 2, come vuole `ImapProvider.Prefetchable` dal 28
  settembre (dei messaggi grandi si scarica il testo, non gli allegati). I due controlli sono rimasti com'erano: dicono
  una cosa che il codice non fa più, e vanno riscritti a parte. (Riscritti nel blocco 7, parte A.)

### Che cosa cambia per chi usa Missiva

- A Invia può comparire "Hai dimenticato l'allegato?".
- Nelle newsletter, sotto il mittente, "Ricevi troppi messaggi da questo mittente? Annulla iscrizione".
- Con gli account Microsoft gli allegati grandi partono, con il caricamento detto nella riga di stato; il limite è 150 MB.
- Impostazioni, Posta, Scrittura e risposta: una sezione "Allegati dimenticati".

### Scelte fatte qui, da rivedere

- F-14: l'oggetto non si guarda ("Preventivo in allegato" nel solo oggetto non avvisa). "Invia comunque" vale per il
  resto di quella finestra (Riprova, nuovo Invia dopo Annulla invio). Invio conferma "Invia comunque", come per
  "Inviare senza oggetto?". Un'immagine nel testo conta come allegato; in una bozza ripresa conta anche il logo della
  firma, e lì l'avviso non scatta.
- F-14: le parole sono un elenco fisso, in italiano e in inglese. "Ti giro il file", "ecco il documento" non avvisano.
- F-11: il comando è una riga sotto l'intestazione, come nel nuovo Outlook, non un link accanto al nome come in Gmail.
- F-11: chi rinuncia preme "Resta iscritto" (non "Annulla", che accanto ad "Annulla iscrizione" si confonde).
- F-11: preferenza un clic, poi mailto:, poi pagina. Gmail fa lo stesso; con il mailto: Gmail manda da sé, Missiva no.
- F-11: un reindirizzamento dopo la POST non si segue e non conta come riuscito, anche se molti siti rispondono così a
  richiesta eseguita: in quel caso Missiva dice che non lo sa e propone la pagina nel browser.
- F-11: solo https. I link http: di List-Unsubscribe non danno il comando.
- F-11: "Richiesta inviata" si ricorda fino alla chiusura di Missiva, non oltre: riaprendo, il comando torna.
- F-11: dopo l'annullamento non si propone una regola per i messaggi che arrivano ancora (il rapporto la suggeriva).
- F-11: PEC, file .eml e messaggi già nella copia su disco da prima di oggi non hanno il comando.
- F-15: pezzi da 1,25 MB e non da 3 MB e più: più richieste, ma ognuna sta nei 60 secondi anche su una linea lenta.
- F-15: dopo un errore a metà gli allegati grandi già caricati in quella chiamata si tolgono e si ricaricano al
  salvataggio dopo. Tenerli avrebbe chiesto di dire alla finestra un risultato a metà.
- F-15: un caricamento non si riprende dal punto in cui si è fermato (Graph lo permetterebbe).
- F-15: durante un invio lungo non c'è un pulsante Annulla: si ferma chiudendo la scrittura. Con Annulla invio acceso la
  finestra è nascosta durante il caricamento, e la riga di stato non si vede.
- F-15: i 150 MB contano i file come sono; Outlook conta il messaggio codificato, quindi il limite vero è più basso.
  Per Gmail e IMAP il rapporto chiedeva anche di leggere il limite del server (SIZE) e la dimensione sulle schede: non
  fatto, non era nel compito.

### Non verificato

- Nulla di questo è stato provato con server veri. Mai una POST di annullamento vera, mai un caricamento vero a
  Outlook, mai un invio vero.
- F-15 su Graph vero: il nome `AttachmentItem`, la risposta 201 all'ultimo pezzo, che l'allegato compaia subito
  nell'elenco della bozza, i limiti dei pezzi, il limite degli account personali. Tutto letto nella documentazione di
  Microsoft e rifatto nel Graph finto.
- F-15 a schermo: niente. La riga di stato durante il caricamento e la finestra "Allegati troppo grandi" non sono state
  viste (nella prova la bozza va solo nel registro, e serviva un account Microsoft). Controllati i testi.
- F-15: tempi e memoria con file da decine di MB; una linea lenta davvero (il pezzo che non arriva in 60 secondi).
- F-11: un sito vero che risponde a una POST di un clic; newsletter vere di Gmail e Outlook (come scrivono le
  intestazioni, quante hanno One-Click).
- F-11 con Gmail: il codice è lo stesso di IMAP, ma il controllo usa il server finto come IMAP generico.
- F-11 nell'app: visto solo il caso di un clic. La scrittura dal mailto: e la pagina nel browser non sono state aperte a
  mano (controllati la conferma e il link).
- F-14 nell'app: visto con un messaggio nuovo. Risposta, inoltro con allegati dell'originale, bozza ripresa e Pianifica
  invio non provati a mano (stessa strada nel codice; le regole sul testo sono nei controlli).
- La Missiva installata non è stata eseguita.
- La suite completa è dell'ultimo commit (`7cd11c1`). Al commit di F-11 (`ec6823b`) il server IMAP finto rispondeva
  sempre a `BODY.PEEK[1.MIME]`, e i due controlli di "Trovato in più" lì non passano.

## Blocco 7: i due controlli della copia degli ultimi 30 giorni, e la copia di sicurezza (F-29)

Fatto il 4 ottobre 2026. "Prima" è il commit `befa789`. Due parti: i due controlli lasciati dal blocco 6, che passavano
per il motivo sbagliato, e la copia di sicurezza che il rapporto `funzionalita.md` dà per mancante (F-29). Controlli della
parte B: `BackupAuditTests` in `ProvaBozze.Audit.cs`, 18, da soli in due secondi con `Missiva.exe --prova-audit
risultato.txt copia`, tutti in cartelle temporanee loro e con dati finti. Le finestre sono state guardate con la Debug su
cartelle di dati finte (un account IMAP sul server finto di `--imap-finto`), in chiaro e in scuro. Suite completa: 757
controlli, "tutto bene".

### Parte A: due controlli che passavano per il motivo sbagliato (`417b843`)

- **Che cosa fa davvero il codice.** CONFERMATO SU WINDOWS con il server finto che risponde come un server vero: la copia
  degli ultimi 30 giorni (`OfflineCopy.RunAsync`) prende un messaggio se `ImapProvider.Prefetchable` dice di sì, cioè se
  pesa meno di 300 KB (intero, `BODY.PEEK[]`) oppure se la parte da leggere, testo e immagini incorporate, sta sotto 2 MB
  (`BODY.PEEK[1.MIME]` e `BODY.PEEK[1]`, più le intestazioni; gli allegati mai, restano un nome sulla scheda). È così dal
  28 settembre 2026, ed è quello che CLAUDE.md dice in "Corpi scaricati in anticipo" e in "Senza rete": il codice è
  giusto, erano i controlli a dire ancora "non quello da 400 KB".
- **Perché passavano.** Il server IMAP finto non rispondeva a `BODY.PEEK[1.MIME]`: la lettura parte per parte falliva,
  il messaggio da 400 KB restava fuori per un errore, e i conti tornavano con quelli di prima.
- **Che cosa è cambiato.** Solo le prove. Il server finto risponde sempre a `BODY.PEEK[1.MIME]` (`FakeImap.TextPartMime`
  non c'è più), con intestazioni coerenti con la struttura che dichiara lui (text/html, utf-8, senza codifica: così vale
  per qualunque testo, non solo ASCII), e nella struttura mette la misura vera del testo, che prima era sempre 10. I due
  controlli ora dicono: 3 corpi copiati, i due sotto 300 KB interi, di quello da 400 KB con un allegato solo il testo
  (un solo `BODY.PEEK[1]`, nessun `BODY.PEEK[2]`, e nella copia c'è il testo con l'allegato come nome senza contenuto);
  fuori quello di 40 giorni fa e uno nuovo con oltre 2 MB di testo; al giro dopo busta e struttura si chiedono solo per
  quest'ultimo, che manca ancora. Da soli: `--prova-audit risultato.txt senza-rete`.
- Trovato in più: un messaggio che la copia non prende mai (oltre 2 MB da leggere) fa chiedere la sua busta e la sua
  struttura a ogni giro, ogni mezz'ora. È una richiesta piccola; NON CORRETTO, non era nel compito.

### Parte B: F-29, copia di sicurezza (`ac05d9e`, `305fc91`)

CONFERMATO leggendo il codice che mancava (né Esporta né Importa; copiando la cartella dei dati su un altro PC i testi
cifrati non si leggono: nella prova 15 su 15, più password e contatti). Ora c'è.

- **Dove.** Impostazioni, Generali, **Copia di sicurezza**: Esporta… e Importa…. Senza account (il primo avvio su un PC
  nuovo) anche dalla finestra Aggiungi un account: "Hai una copia di sicurezza di Missiva fatta su un altro PC? Importala".
- **Il file** (`BackupFile`, estensione `.missiva-copia`, formato 1). In chiaro solo l'intestazione: "MSVCOPIA", la
  versione del formato, i parametri (PBKDF2-HMAC-SHA256, 600.000 iterazioni, AES-256-GCM, le lunghezze, la lunghezza del
  contenuto), il sale e i nonce casuali. Poi il sigillo dell'intestazione, il contenuto cifrato e il suo tag. Solo classi
  di .NET (`Rfc2898DeriveBytes.Pbkdf2`, `AesGcm`, `RandomNumberGenerator`). Né nomi, né date, né quanti account.
- **Che cosa contiene**: le impostazioni (con account, altri indirizzi del Da, firme, modelli, regole di Missiva,
  categorie e colori, Preferiti, ordine e colori delle cartelle, contrassegni con le date, posposti, mittenti delle
  immagini, ricerche recenti, menu), i contatti e i gruppi di Missiva, la rubrica dei destinatari, i domini noti dei
  loghi. I testi che su disco sono cifrati con DPAPI entrano decifrati, dentro la cifratura del file: su un altro PC
  DPAPI non li leggerebbe.
- **Che cosa non contiene**: la posta e la sua copia, i loghi, la coda delle azioni fatte senza rete, gli invii
  pianificati. Accessi: mai l'accesso a Microsoft (`HomeAccountId`, e i token che stanno in `msal.cache`) né il token di
  Google; le password IMAP solo con la casella **Includi le password degli account IMAP**, spenta di base, con una riga
  che dice il rischio. Il segreto dell'app Google c'è.
- **Esporta**: password scritta due volte (almeno 8 caratteri), la casella, dove salvare. Alla fine dice dov'è il file,
  che cosa contiene e che cosa no. Regole e modelli con testi che su quel PC non si decifrano più restano fuori, e lo dice.
- **Importa**: il file si guarda prima di chiedere la password (non è una copia; è di un formato più nuovo, con "Aggiorna
  Missiva"; è incompleto, con quanti byte mancano). Password sbagliata: detta nella finestra, che resta aperta. File
  rovinato o modificato: "La password è giusta, ma il file è rovinato o è stato modificato dopo l'esportazione". Poi che
  cosa c'è nella copia e di quando è, e se Missiva ha già dati "Sostituire tutto con questa copia?" (Annulla è il
  predefinito): sostituisce tutto, non unisce.
- **Come riparte**: l'importazione non tocca nulla mentre Missiva gira. Lascia nella cartella dei dati
  `importazione.dat`, cifrato con DPAPI, e chiede di riavviare (Riavvia ora, oppure Più tardi: allora la pagina dice che
  un'importazione aspetta, e la lascia annullare). All'avvio, prima di leggere qualunque cosa, Missiva scrive i file
  nuovi accanto, mette da parte quelli di adesso con `prima-dell-importazione` e la data nel nome (impostazioni, la loro
  copia di riserva, contatti, rubrica, domini noti) e mette i nuovi al loro posto, cifrati per l'utente di quel PC. Poi
  un avviso: di quando era la copia, quali account chiedono l'accesso, che i dati di prima sono da parte.
- **Sul PC nuovo**: gli account ci sono tutti; sotto ognuno la barra con Accedi di nuovo (Microsoft, Google) o Aggiorna
  password (IMAP senza password nella copia), con "Su questo PC manca la password dell'account" o "manca l'accesso
  all'account". Nessuna finestra di Microsoft si apre da sola, e un account IMAP senza password non si collega nemmeno
  (prima avrebbe provato con una password vuota). La posta si riscarica.
- **Password e memoria**: la password va dalle caselle alla derivazione della chiave come vettore di caratteri, poi
  azzerato; non diventa un testo di .NET, non va nel registro né su disco. Chiave e vettori in chiaro azzerati appena
  usati. Il contenuto aperto resta in memoria come testo finché serve (fino al file in attesa).
- **Provato**: 18 controlli. Il formato (intestazione, due file diversi dallo stesso contenuto); andata e ritorno con
  ogni tipo di dato, confrontato per intero; che cosa manca apposta (token, accesso Microsoft, password, invii
  pianificati, valori DPAPI); niente in chiaro nel file né nel file in attesa; su un altro PC tutto si legge e viene
  cifrato di nuovo, e sul PC di partenza quei file non si leggerebbero; i tre account senza accesso con la loro barra e
  zero comandi al server finto; sopra dati che ci sono già (sostituiti, e quelli di prima da parte uguali byte per
  byte); sullo stesso PC gli accessi restano; con la casella la password c'è e l'account si collega al server finto, che
  accetta solo quella; password sbagliata; file troncato, non di Missiva, più lungo; un bit cambiato in otto punti;
  formato futuro; importazione annullata e file in attesa rovinato; testi illeggibili sul PC di partenza; la password in
  nessun file. A schermo, con dati finti: la pagina, Esporta (password diverse, password corta, casella accesa, avviso
  finale), Importa (password sbagliata, la domanda, Riavvia ora e Più tardi, la pagina con l'importazione in attesa e
  Annulla l'importazione), e il giro intero su una cartella vuota: link di Aggiungi un account, importazione, riavvio da
  solo, avviso, barra "Aggiorna password", password scritta e account che si collega, Preferiti tornati.

### Che cosa cambia per chi usa Missiva

- Impostazioni, Generali ha una pagina in più, Copia di sicurezza.
- La finestra Aggiungi un account, quando non c'è nessun account, ha una riga in più per importare una copia.
- Un account IMAP senza password salvata, o con una password che non si legge più, non apre più la connessione: mostra
  subito la barra. Un account Microsoft mai entrato su quel PC non apre la finestra di Microsoft da solo.
- Nella cartella dei dati possono comparire `importazione.dat` (finché si aspetta il riavvio) e i file
  `...-prima-dell-importazione-...`.

### Scelte fatte qui, da rivedere

- **L'importazione si applica al prossimo avvio**, non subito. È il modo che non lascia nulla a metà: nessuna parte di
  Missiva ha in memoria i dati vecchi quando arrivano i nuovi, e nessun salvataggio in corso li può sovrascrivere. Costa
  un file in attesa sul disco (cifrato con DPAPI, con le password IMAP se la copia le ha) fino al riavvio.
- **Il sigillo dell'intestazione.** Oltre a versione e parametri il file ha due tag GCM invece di uno: senza il primo non
  si può dire "password sbagliata" distinta da "file rovinato". Non rivela nulla del contenuto. Un byte cambiato
  nell'intestazione (sale, nonce, sigillo) viene però detto come password sbagliata: senza la chiave non si distingue.
- **Sullo stesso PC gli accessi restano.** "Sostituire tutto" non toglie l'accesso agli account che c'erano già con lo
  stesso identificativo, tipo e indirizzo. L'alternativa, più semplice da dire, era chiedere sempre di rientrare.
- **Gli invii pianificati non sono nella copia**, e quelli del PC restano dopo un'importazione. Una copia vecchia
  farebbe ripartire una bozza che non è più pianificata. Sul PC nuovo vanno ripianificati a mano, e Missiva non lo ricorda.
- **Il segreto dell'app Google è nella copia** (Google non lo considera riservato per le app desktop): senza, sul PC
  nuovo gli account Gmail non rientrerebbero finché non lo si riscrive. Anche gli ID delle app.
- **Regole e modelli illeggibili sul PC di partenza restano fuori** invece di passare con il segnaposto.
- **Il link nella finestra Aggiungi un account** non era chiesto: senza, sul PC nuovo bisogna chiudere quella finestra e
  cercare le Impostazioni.
- Password: almeno 8 caratteri, senza altri controlli e senza un indicatore di robustezza. 600.000 iterazioni su questo
  PC sono circa 70 ms: con una password debole la protezione è quella della password.
- Il contenuto si comprime (gzip) prima di cifrarlo: le firme con un logo sono 100-200 KB di RTF l'una.
- La casella delle password IMAP c'è solo se c'è un account IMAP.
- Le ricerche recenti e l'elenco dei messaggi già passati dalle regole sono nella copia, con il resto delle impostazioni.
- L'avvio con Windows segue la copia: importata su un PC nuovo con quella scelta accesa, Missiva si mette fra le app di avvio.
- I file messi da parte non si cancellano mai. La copia della posta e la coda delle azioni del PC restano dove sono,
  anche degli account che dopo l'importazione non ci sono più.
- Il nome proposto è "Missiva AAAA-MM-GG.missiva-copia". Nessuna copia automatica a scadenza.
- Parte A: nel server finto il testo si dichiara sempre text/html utf-8 senza codifica, anche in `BODY[1.MIME]`. Un
  server vero dà le intestazioni vere della parte, con la sua codifica.

### Non verificato

- **Un vero altro PC, o un altro utente di Windows.** "Un altro PC" nei controlli è un'entropia di prova per DPAPI, e a
  schermo una cartella di dati vuota sullo stesso PC e con lo stesso utente.
- **Accedi di nuovo dopo un'importazione**, per Microsoft e per Google: non c'erano account veri. Premuto solo Aggiorna
  password, con il server finto. Che un account Microsoft importato non apra la finestra da solo è visto nel codice e
  nel controllo (l'errore con il suo rimedio), non con la finestra account di Windows.
- **La Missiva installata** non è stata eseguita. La versione finale compila (0 errori) ma non è stata avviata: il
  riavvio con `--riavvio` è stato visto solo nella Debug, due volte.
- **Le finestre di Windows per scegliere il file** (Salva con nome, Apri): saltate con `MISSIVA_COPIA_FILE`. Non visti
  il nome proposto, il filtro, la domanda di sovrascrittura. "Apri la cartella" non premuto.
- Il riavvio annullato da una domanda di una scrittura aperta (Salvare la bozza? Annulla): letto nel codice, non provato.
- L'avvio nascosto (`--nascosta`) con un'importazione in attesa.
- A schermo, nella finestra Importa, solo la password sbagliata: il file rovinato ("Chiudi" al posto di Annulla), quello
  incompleto e quello di un formato più nuovo sono controllati come testo, non visti. L'avviso dell'esportazione con le
  cose lasciate fuori non è stato visto.
- `Backup.ApplyPending` che non riesce a metà (disco pieno, un file in uso): provato solo il file in attesa rovinato. Il
  caso di un file non sostituito dopo le impostazioni è scritto ma non provocato.
- Copie grandi: migliaia di contatti, firme con loghi. Tempi e dimensioni non misurati (la copia di prova è 2 KB).
- Assistente vocale sulle finestre nuove. Le caselle hanno il nome; non è stato ascoltato.
- Parte A: il percorso di Gmail (`GmailProvider.PrefetchAsync` usa la stessa `Prefetchable`, ma il controllo usa il
  server finto come IMAP generico), e un server vero.
- La suite completa è passata due volte su tre: una volta è fallito "ricerca che nessuno aspetta più", che dipende dai
  tempi ed è noto. L'ultimo giro, 757 controlli, è del codice del commit `305fc91`; il commit dopo ha solo i documenti.


## Blocco 8: velocità, secondo gruppo (interfaccia e disco)

Fatto il 4 ottobre 2026. "Prima" è il commit `2e1c6cc`. Sono i punti di gravità Media dei due rapporti di velocità
(`velocita.md`, qui v1, e `velocita-2.md`, qui v2) che riguardano il thread dell'interfaccia e il disco. Ogni punto è
stato misurato qui prima di toccarlo: tre sono stati lasciati come sono, perché su questo PC e con i dati di oggi il
costo è trascurabile. Due modi di misurare, come nel blocco 4: dove bastava, cartelle temporanee e dati finti nella suite
(`Speed2AuditTests`, 17 controlli e due righe di sole misure; da soli con `--prova-audit risultato.txt velocita2`); dove
serviva la finestra, la versione di prova (`strumenti\prova.ps1`, Debug su una copia dei dati, `MISSIVA_NO_READ`) pilotata
con `MISSIVA_KEYS`: otto avvii, uno senza rete; sulla casella vera solo letture e ricerche. I numeri sono di poche prove
ciascuno e dicono l'ordine di grandezza.

### Punti dei rapporti

- **v2 V-10 = v1 V-21** (`Store.Save` sul thread dell'interfaccia a ogni piccolo cambio). CONFERMATO SU WINDOWS, e più
  caro di quanto si pensava; CORRETTO (`33fa242`).
  - Misura. Le chiamate fuori dalle prove sono 85. Il costo di 2,6-3,2 ms scritto nel blocco 5 era con un file di prova
    da 1 KB. Con un file della misura di quello dell'autore (13 KB) una scrittura costa 12-19 ms in una cartella temporanea
    e 20-26 ms nella copia dei dati della versione di prova: è la copia di riserva a pesare (`File.Replace` con
    `impostazioni.bak`: 4 ms a 1 e 4 KB, 12-13 ms da 13 KB in su; senza copia di riserva 1,4-2,1 ms). Con impostazioni
    grandi (730 KB: 500 contrassegni, 2.000 messaggi visti dalle regole, un modello con un logo) 21-23 ms.
  - Correzione. `Store.SaveSoon()` (`SettingsSaver` in `Core/Store.cs`): 400 ms dopo l'ultima richiesta, al più 2 s
    dalla prima, l'istantanea si prende sul thread dell'interfaccia (0,2-0,5 ms con 13-19 KB, 3-4 ms con 730 KB) e si
    scrive su un thread del pool. Le istantanee sono numerate: sul file arriva sempre l'ultima, mai una più vecchia
    sopra una più nuova, anche con salvataggi subito da altri thread. Passano da `SaveSoon` tredici punti: gruppi degli
    account e delle sottocartelle aperti o chiusi, "Tutti gli account" e Preferiti (aperti, chiusi, aggiunti, spostati),
    zoom del riquadro, ricerche recenti, messaggi già visti dalle regole (`RulesSeen`), firme e modelli mentre si
    scrivono. `Store.Save()` resta subito per tutto il resto: account aggiunti, rimossi o cambiati, accessi e token,
    contrassegni, posposti, invii pianificati, regole, caselle delle Impostazioni, importazione. `Store.Flush()` porta
    sul disco quello che è in attesa: all'uscita (`OnExit`), alla fine della sessione di Windows, quando la finestra va
    nell'area di notifica, e comunque a `ProcessExit`.
  - Dopo. Venti cambi di fila: una scrittura sola, 0,01-0,4 ms a cambio sul thread di chi chiama. Nella versione di
    prova "account chiudi" (quattro account, quattro richieste) fa una riga "impostazioni scritte (13 KB, accorpate, in
    sottofondo) 20-26 ms"; prima sarebbero state quattro scritture sul thread dell'interfaccia (nell'app, prima della
    correzione, non misurato). Uscita subito dopo un cambio, nella versione di prova: "account chiudi" ed "esci" nello
    stesso istante, gli account chiusi sul disco passano da 2 a 4 ("impostazioni scritte (13 KB, subito) 24 ms").
  - Le garanzie del blocco 5 restano: stessa scrittura (`SafeFile.Write`, copia di riserva), impostazioni bloccate
    all'avvio mai sovrascritte nemmeno all'uscita, un errore non è un'eccezione e si dice una volta sola.
  - Controlli: "V-10, ..." (6): una scrittura per venti cambi e su un altro thread, uscita subito dopo un cambio,
    salvataggio non riuscito due volte e poi riuscito all'uscita, quattro thread mescolati e un'istantanea in ritardo,
    copia di riserva e impostazioni bloccate, impostazioni grandi.
  - Non fatti: `RulesSeen` come insieme invece che elenco (2.000 confronti di testo per messaggio arrivato: microsecondi),
    e "non salvare se l'RTF è uguale a quello di prima" per firme e modelli.
- **v2 V-20 = v1 V-11** (`BodyCache.Trim` dopo ogni corpo salvato, sotto il lucchetto di `Forget`). CONFERMATO SU
  WINDOWS, ma il costo non era dove lo metteva il rapporto; CORRETTO (`c778eb9`).
  - Misura, in una cartella temporanea con file finti. La pulizia da sola costa poco: 2,2 ms con 625 file (la copia più
    grande dell'autore oggi: 625 corpi, 54 MB), 4-5 con 2.000, 6-11 con 5.000. Il costo vero era il lucchetto: `Forget`,
    chiamato dal thread dell'interfaccia da Elimina, Sposta e Posponi, aspettava le scritture in sottofondo, ognuna con
    la sua pulizia. Con dieci corpi in scrittura (una cartella appena aperta): 26-39 ms in mediana e 46-50 al massimo
    con 625 e 2.000 file, 69 e 86 con 5.000.
  - Correzione. `BodyCache` tiene in memoria i byte di ogni account; la pulizia intera si fa alla prima scrittura della
    sessione, oltre il limite o dopo un'ora (e 30 s dopo l'avvio, come prima), con un lucchetto suo. `Forget` non prende
    più il lucchetto delle scritture. In `OfflineCopy.RunAsync` l'elenco dei file e i 500 "c'è già?" (15-18 ms) vanno su
    un thread del pool.
  - Dopo: `Forget` durante dieci scritture 1 ms in mediana, 2 al massimo; dieci corpi salvati in 10-14 ms (52-62 la
    prima volta nel processo) invece di 48-108; trenta corpi salvati, una pulizia invece di trenta.
  - Controlli: "V-20, ..." (4), con `BodyCache.TestRoot` in una cartella temporanea: una pulizia per trenta corpi, l'attesa
    di `Forget`, il limite per account che vale ancora (e i corpi tolti che si scontano), i corpi di oltre 30 giorni che
    escono alla prima scrittura dopo l'avvio. Il controllo dei 30 giorni di `OfflineTests` ora dimentica i conti come
    dopo un riavvio: prima si aspettava la pulizia a ogni scrittura.
- **v2 V-16 = v1 V-06** (Persone: a ogni lettera tutti i contatti con confronti della lingua). CONFERMATO SU WINDOWS con
  migliaia di contatti, CORRETTO in parte (`c18aa4e`).
  - Misura. Con 4.500 contatti finti `PeopleView.BuildEntries` costava 22-37 ms a lettera, quasi tutti nel confronto
    della lingua di Windows (ICU: circa un microsecondo a chiamata, un campo alla volta); l'ordine per nome 4-6 ms.
    Nella finestra, con `--persone --contatti-finti --contatti-molti` (6.010 contatti): "Persone, elenco riempito"
    40-78 ms a lettera in una prima serie, 17-48 in una seconda. Con i contatti dell'autore (la copia più grande è di 206 KB, qualche centinaio di persone: una stima
    dalla dimensione, non un conteggio) il costo era di pochi millisecondi.
  - Correzione. `CultureSearch.Contains`: fra due testi di soli caratteri ASCII stampabili, in italiano e in inglese, il
    confronto ordinale senza maiuscole dà lo stesso risultato di quello della lingua ed è cento volte più svelto; con
    una lettera accentata, nella ricerca o nel campo, resta il confronto della lingua (che trova anche "ò" scritta come
    "o" più l'accento). La via svelta si spegne da sola se la lingua di Windows non è italiano o inglese, o se il suo
    confronto ignora trattino e apostrofo.
  - Dopo: 3-9 ms a lettera con 4.500 contatti (in media 9,3 ms invece di 25,9 sulle 37 ricerche del controllo, nove
    delle quali con lettere accentate); nella finestra 5-23 ms e 6-7 nelle stesse due serie.
  - Controlli: "V-16, ..." (2): 37 ricerche su 4.503 contatti (accenti scritti in due modi, apostrofi, trattini, numeri,
    gruppi) danno le stesse persone nello stesso ordine di `Contact.MatchesByCulture`, il confronto di prima; e la via
    svelta dice lo stesso del confronto della lingua su 931.595 coppie di testi con ogni carattere stampabile.
  - Non fatti, perché dopo la correzione non servono: la pausa prima di filtrare (con pochi contatti ritarderebbe e
    basta) e gli elenchi tenuti già ordinati. Una ricerca con una lettera accentata costa come prima (22-25 ms con
    4.500 contatti).
- **v2 V-15 = v1 V-07** (suggerimenti dei destinatari: tutta la rubrica rifatta a ogni lettera). CONFERMATO SU WINDOWS,
  CORRETTO (`f6b3f22`).
  - Misura, con una rubrica finta di 4.545 voci e 4.502 contatti: `AddressBook.Suggest` 16,8 ms e 11,8 MB allocati a
    ogni lettera (v2, su Linux: 10-15 ms e 18 MB). Nella finestra con 6.010 contatti finti e la rubrica della copia di
    prova: "suggerimenti: pannello aggiornato" 12-47 ms a lettera.
  - Correzione. Un indice in memoria con nome, indirizzo e parole di ogni voce già in minuscolo, rifatto solo quando
    cambia la rubrica (una versione, che cresce in `Add`) o cambiano i contatti (un segno calcolato a ogni chiamata
    dagli oggetti: vale anche per un contatto cambiato sul posto e per le prove che sostituiscono `AddressBook.Contacts`).
  - Dopo: 1,6 ms e 212 KB a lettera; la prima lettera dopo un cambio rifà l'indice (21,8 ms). Nella finestra: 52 ms la
    prima lettera, poi 2-9 ms.
  - Controlli: "V-15, ..." (2): 1.464 ricerche confrontate con `AddressBook.SuggestOld` (il calcolo di prima, tenuto in
    Debug), le stesse persone nello stesso ordine, anche dopo una voce nuova, un messaggio inviato, un contatto cambiato
    sul posto, indirizzi cambiati, contatti tolti e aggiunti, gruppi riempiti e svuotati, e senza contatti; e i tempi.
- **v2 V-11 = v1 V-10** (avvio: una decifratura DPAPI per ogni testo protetto; contrassegni mai potati). MISURATO, NON
  CORRETTO: con i dati di oggi il costo non c'è. Nella copia delle impostazioni dell'autore c'è un solo testo cifrato (le
  ricerche recenti) e nessun contrassegno, posposto, regola o modello: "impostazioni lette (1 testi decifrati con DPAPI
  in 3,1-5,2 ms ...)", ed è la prima chiamata a DPAPI del processo. Una decifratura costa 173 microsecondi (500 in 87
  ms): comincerebbe a contare sopra il centinaio di testi. La riga resta nei tempi dell'avvio e lo dirà. La lettura
  intera delle impostazioni in Debug è 128-140 ms; la stessa fase negli avvii di prima di questo blocco era 116-124 ms
  (una volta 38), e tutto l'avvio varia di 100-200 ms da un avvio all'altro: la differenza non si distingue. La
  potatura dei contrassegni vecchi è una scelta da fare con l'autore, non una correzione di velocità.
- **v2 V-17 = v1 V-13** (il lavoro dei provider dopo un await sul thread dell'interfaccia). MISURATO, NON CORRETTO: sotto
  un fotogramma. Su questo PC, con una newsletter finta: corpo di 60 KB, JSON di Graph 0,2 ms e testo della parte MIME
  0,4-0,5 ms; 211 KB, 0,7-0,8 e 1,5-1,6 ms; 1 MB, 3,5 e 7,4-7,8 ms; un'immagine incorporata di 300 KB 1,4-1,5 ms.
  Spostarli chiederebbe di proteggere i campi dei provider che oggi cambiano solo sul thread dell'interfaccia: per un
  millisecondo non vale il rischio. Misurati i pezzi da soli, non dentro i provider veri.
- **v2 V-21, e v1 V-12 per il solo annullamento** (ricerca senza rete che non si ferma). CONFERMATO SU WINDOWS, CORRETTO
  (`8fa9b5a`). 300 corpi finti da 60 KB (29 MB): una ricerca 0,94 s; lanciandone un'altra 30 ms dopo, la seconda
  arrivava in 1,89 s (due scansioni intere insieme, ognuna su tutti i processori) e ora in 1,03 s, con la prima ferma
  dopo 28 file invece di 300. `BodyCache.Search` prende un token e si ferma fra un file e l'altro; la finestra lo annulla
  quando parte un'altra ricerca (in un account o in tutti) e quando la ricerca si chiude. Nella versione di prova con
  `--senza-rete`, due ricerche di fila sulla copia vera: la prima "fermata" dopo 0,2 s, la seconda in 1,4 s (una da sola
  1,3 s). Controllo: "V-21, ...". Il resto di v1 V-12 (ogni file decifrato e letto a ogni ricerca) non è di questo blocco.
- **v1 V-05 = v2 V-27** (Invio rifà la ricerca dei suggerimenti). CONFERMATO SU WINDOWS, CORRETTO (`41bfe30`). Nella
  versione di prova, sulla casella vera (solo ricerche), con un account Gmail: i suggerimenti cercano sul server in
  1,2-1,9 s, e Invio rifaceva la stessa ricerca in 1,0-1,6 s; premendo Invio mentre i suggerimenti cercavano ancora, 1,8
  s (la ricerca fermata teneva occupata la connessione per 1,3 s). Dopo: 1-2 ms con i suggerimenti finiti ("ricerca:
  riusata quella dei suggerimenti"), e 1,5 s con i suggerimenti in corso (si aspetta quella invece di fermarla e
  rifarla). `SuggestSearches` (`UI/MainWindow.Suggestions.cs`): la ricerca che parte tiene quella dei suggerimenti con lo
  stesso account, cartella e testo e ferma le altre; la riusa una volta sola, se è finita bene da meno di 30 secondi o
  è ancora in corso. Controllo: "V-05/V-27, ..." con un server finto che conta le richieste. La parte di v2 V-27
  sull'annullamento che non ferma i comandi IMAP non è stata toccata.
- **v2 V-29** (il processo dei PDF si avvia sul thread dell'interfaccia). CONFERMATO SU WINDOWS, CORRETTO (`b59ad54`).
  Nella versione di prova, anteprima di un PDF con il processo da avviare: "processo dei PDF avviato nel suo
  AppContainer" 42 e 56 ms "sul thread di chi chiede"; dopo, 54 ms "in sottofondo" (la riga ora lo dice).
  `PdfWorker.RunAsync` avvia il processo insieme alla richiesta su un thread del pool; anche la chiusura di un processo
  fermo o caduto non gira più sul thread di chi chiede. Controllo: "V-29, ..." (chi chiede riprende dopo 3 ms).
- **Lasciato dal blocco 7** (un messaggio che la copia dei 30 giorni non prende mai fa chiedere busta e struttura a ogni
  giro). GUARDATO, NON CORRETTO. Nei registri della versione di prova ci sono quattordici giri della copia sulla casella di
  L'autore, tutti con "0 da scaricare o troppo grandi": oggi il caso non c'è. Quando c'è, costa una richiesta piccola per
  messaggio ogni mezz'ora, sulla connessione in sottofondo. Per ricordarlo senza sbagliare bisogna sapere perché il corpo
  non è arrivato (troppo grande, o uno scaricamento non riuscito che va riprovato), e oggi `OfflineCopy` non lo sa: serve
  un metodo in più nei provider. Non vale la modifica.

### Trovato in più

- Il costo di un salvataggio delle impostazioni misurato nel blocco 5 (2,6-3,2 ms) valeva per un file di prova da 1 KB.
  Con il file vero sono 12-26 ms, per la copia di riserva. Le 72 chiamate di `Store.Save()` rimaste subito costano
  quindi tanto ciascuna sul thread dell'interfaccia: fra queste Posponi (una per messaggio, dentro il ciclo), i
  contrassegni, le caselle delle Impostazioni. NON CORRETTO: sono i dati che non si possono rischiare.
- In uno degli otto avvii della versione di prova i comandi di `MISSIVA_KEYS` non sono stati eseguiti per oltre 30 s
  dopo l'avvio, mentre il collegamento di un account Gmail impiegava 11 s e "cartelle e non letti" 19,6 s (la rete in
  quel momento era lenta). La finestra era arrivata al primo disegno e all'elenco come sempre. Rilanciata subito dopo,
  ha funzionato, e così negli altri avvii. Causa NON TROVATA: può essere il thread dell'interfaccia fermo in attesa
  della rete (codice di prima), il timer dei comandi rimandato da Windows, o qualcosa di questo blocco, che dal codice
  non risulta ma non è escluso.

### Che cosa cambia per chi usa Missiva

- Aprire e chiudere i gruppi nella barra laterale, cambiare lo zoom con Ctrl e rotellina, lanciare una ricerca e
  scrivere una firma o un modello non scrivono più il file delle impostazioni a ogni passo: si scrive una volta sola,
  meno di mezzo secondo dopo, senza fermare la finestra. All'uscita si salva tutto.
- Invio dopo che il pannello sotto la ricerca ha mostrato i suoi risultati: l'elenco compare subito, senza la seconda
  attesa di uno o due secondi.
- Eliminare o spostare un messaggio mentre Missiva sta copiando i corpi sul PC non ha più un'esitazione.
- Persone e i suggerimenti dei destinatari restano svelti anche con migliaia di contatti.
- Senza rete, una ricerca lanciata subito dopo un'altra non aspetta più la prima.
- La prima anteprima di un PDF non ferma la finestra per mezzo decimo di secondo.

### Scelte fatte qui, da rivedere

- **Quali impostazioni possono aspettare.** Se Missiva viene chiusa di forza (crash, corrente) entro 0,4-2 s da uno di
  quei cambi, quel cambio si perde: un gruppo aperto o chiuso, lo zoom, una ricerca recente, le ultime lettere di una
  firma o di un modello. Fra questi c'è l'elenco dei messaggi già visti dalle regole di Missiva: in quel caso al
  prossimo avvio una regola può ripassare sugli stessi messaggi appena arrivati.
- Tempi: 400 ms dopo l'ultima richiesta, 2 s al massimo dalla prima.
- La pulizia della copia dei corpi al più ogni ora: i corpi di oltre 30 giorni escono fino a un'ora più tardi, e il
  conto in memoria può sbagliare di qualche corpo fino alla pulizia dopo. Al limite dei 200 MB la pulizia torna a ogni
  scrittura, come prima, ma fuori dal lucchetto.
- La via svelta del confronto in Persone vale solo per l'italiano e l'inglese: con un'altra lingua di Windows resta
  tutto com'era.
- L'indice dei suggerimenti resta in memoria (qualche MB con migliaia di voci).
- Una ricerca riusata da Invio può avere fino a 30 secondi; un secondo Invio cerca di nuovo.
- Scegliere una riga del pannello non ferma più subito le ricerche in corso: le ferma la ricerca che parte un attimo dopo.

### Non verificato

- **La Missiva installata** non è stata eseguita. La versione finale compila (0 errori), non è stata avviata.
- V-10: la fine della sessione di Windows e la chiusura nell'area di notifica (provata solo l'uscita con "esci"); lo
  zoom con la rotellina vera, le firme e i modelli scritti a mano; il costo di prima nell'app, che è una stima da quello
  di una scrittura.
- V-20: Elimina e Sposta nella finestra mentre i corpi si copiano (solo nei controlli); la copia vera dell'autore al limite.
- V-16: con la tastiera vera e con i contatti veri dell'autore; quanti siano i suoi contatti è una stima dalla dimensione dei
  file. La via svelta è provata con la lingua di questo PC (it-IT) e con ICU: con un'altra lingua si spegne.
- V-15: il campo A della finestra di scrittura e la finestra di un evento (misurata la casella Cerca).
- V-21: la ricerca in tutti gli account senza rete, e la chiusura della ricerca mentre gira.
- V-05: gli account Microsoft e IMAP, "Tutti gli account", la scelta di una riga del pannello (messaggio o file), una
  ricerca con le pastiglie dei filtri. Provati tre Invio con un account Gmail.
- V-29: un processo dei PDF fermo o caduto con la finestra aperta (i controlli della suite lo provano senza finestra).
- V-17: misurati i pezzi da soli (JSON, base64, testo MIME), non i provider veri durante uno scorrimento.
- L'avvio senza risposta descritto sopra.
- I commit da `33fa242` a `b59ad54` sono stati compilati uno per uno. La suite completa è passata quattro volte su
  quattro: dopo i primi due (767 controlli), dopo i primi quattro (771), sul codice di `b59ad54` (774) e sul codice del
  commit dopo (774, "tutto bene"), che cambia solo due indirizzi finti nelle prove e i documenti.

## Blocco 9: velocità, terzo gruppo (rete e disegno), e due segnalazioni dell'autore

Fatto il 4 ottobre 2026. "Prima" è il commit `560b256`. Sono i punti di gravità Media dei due rapporti di velocità
(`velocita.md`, qui v1, e `velocita-2.md`, qui v2) che riguardano la rete e il disegno, due cose lasciate dai blocchi 4 e
8, e due difetti segnalati dall'autore mentre il blocco era in corso. Ogni punto è stato misurato prima di toccarlo: tre sono
stati lasciati come sono. Tre modi di misurare: i controlli della suite con server finti (`Speed3AuditTests`, 15
controlli; da soli con `--prova-audit risultato.txt velocita3`); la versione di prova (`strumenti\prova.ps1`, Debug su una
copia dei dati, `MISSIVA_NO_READ`: sulla casella vera solo letture) pilotata con `MISSIVA_KEYS`, undici avvii; e, per i
tempi di un arrivo vero, il registro della Missiva installata, solo letto. I numeri dal vivo sono di poche prove ciascuno,
della rete di casa dell'autore una domenica sera.

### Punti dei rapporti

- **v2 V-23 = v1 V-19, prima parte** (client HTTP senza compressione, in HTTP/1.1, con un gruppo di connessioni a parte
  per gli scaricamenti). CONFERMATO SU WINDOWS per la compressione, NON CONFERMATO per HTTP/2 con Graph; CORRETTO
  (`82c6c94`).
  - Misura, nella versione di prova, chiedendo la stessa risposta senza compressione, con gzip e con br: Graph comprime
    con gzip e non con br. La prima pagina della Posta in arrivo da 64,3 a 8,4 KB, quella della Posta inviata da 122,6 a
    14,7, un corpo da 20,3 a 9,1 e da 22,2 a 9,6, una conversazione da 1,5 a 0,8. Chiesta in HTTP/2, Graph risponde in
    HTTP/1.1: lì il guadagno delle richieste sulla stessa connessione non c'è. Le API di Google (calendario) rispondono
    in HTTP/2.
  - I tempi, su questa rete, cambiano poco: le richieste di Graph durano 60-130 ms quale che sia la misura della risposta
    (una pagina dell'elenco 0,5-0,8 s, quasi tutto tempo del server). Con gzip: 471 e 563 ms invece di 529 e 675 sulle
    due pagine dell'elenco, una prova l'una. Il primo clic dopo quattro minuti di pausa: corpo in 166 ms prima e 121
    dopo, "testo pronto" 231 e 181 ms, una prova l'una. Differenze così, su una prova, non si distinguono dal caso: sui
    tempi non c'è un guadagno dimostrato. Quello dimostrato è nei byte, e conta sulle reti lente, che qui non sono state
    provate.
  - Connessioni ferme: con il limite a cinque minuti restano aperte fino a circa quattro (due ancora aperte dopo 212 s,
    una sola dopo 263 s: le chiude il server); prima un minuto.
  - Correzione. `Network.ApiHandler` (decompressione automatica, connessioni ferme tenute cinque minuti) e
    `Network.ApiRequest` (HTTP/2 con ripiego su 1.1): un gestore solo per i due client di Graph, uno per le API di
    Google. I pezzi del caricamento degli allegati grandi restano in HTTP/1.1, com'erano. Nelle righe "tempi:" di Graph
    c'è la versione di HTTP, e c'è una riga "tempi: Google ...".
  - Controllo: "V-23, ..." con un server su 127.0.0.1 che parla solo HTTP/1.1 e comprime: la richiesta chiede gzip e lo
    legge (11,5 KB, sul filo 0,7), ripiega su 1.1, e uno scaricamento a blocchi di 3 MB compresso dal secondo client
    arriva intero sulla stessa connessione.
- **v2 V-24** (il limite di quattro richieste insieme per casella vale solo dentro ogni chiamata). CONFERMATO SU WINDOWS,
  raro; CORRETTO (`c1d2b54`).
  - Misura. Nei registri della versione di prova dal 30 settembre al 4 ottobre: 79 letture delle cartelle dell'account di
    Outlook (quasi tutte avvii), 1.165 richieste a Graph, due risposte 429, tutte e due su una delle sei cartelle speciali
    chieste insieme. Quella
    di cui il registro mostra la ripetizione è costata i 5 s del massimo dell'attesa (richiesta alle 15:17:49, ripetuta
    alle 15:17:54). Nessun 429 sui clic.
  - Correzione. `GraphProvider.MailboxGate`, uno per casella anche fra più `GraphProvider`: al più quattro richieste
    insieme, tre che non siano l'apertura di un messaggio (corpo, immagini del testo, invito, conversazione), due fra
    scaricamenti e pezzi dei caricamenti. Chi aspetta un posto lo dice nella riga "tempi:" ("in fila N ms").
  - Dopo, dal vivo: nessun 429 nei nove avvii fatti dopo la correzione; le richieste rimaste in fila, diciotto in tutto,
    hanno aspettato da 44 a 296 ms.
  - Controlli: "V-24, ..." (3): 36 richieste di sette chiamate insieme da due provider della stessa casella, mai più di
    4 in corso e 3 che non aprono un messaggio; con tre richieste ferme la conversazione del messaggio aperto parte lo
    stesso, e una richiesta annullata mentre aspetta non si tiene il posto; quattro allegati, due alla volta.
- **v2 V-38 = v1 V-19, seconda parte** (sottocartelle di Graph in fila, sei cartelle speciali a ogni avvio). CONFERMATO
  SU WINDOWS, CORRETTO (`11b1732`).
  - Misura, all'avvio della versione di prova: elenco 198 ms, sei cartelle speciali insieme 77-123 ms, quattro
    sottocartelle una dopo l'altra 64, 64, 100 e 69 ms: circa 620 ms e 11 richieste. Nessuno le aspetta: le cartelle si
    vedono dalla copia locale, e queste richieste aggiornano i non letti.
  - Correzione. Le figlie di cartelle diverse si chiedono insieme, dentro il limite per casella, e l'elenco esce nello
    stesso ordine di prima. Posta in arrivo, Bozze, Posta inviata, Posta eliminata e Posta indesiderata si prendono
    dalla copia locale delle cartelle, se sono ancora nell'elenco appena letto; l'Archivio si chiede sempre.
  - Dopo, un avvio: elenco 202 ms, poi Archivio e quattro sottocartelle in 150 ms: circa 350 ms e 6 richieste.
  - Controlli: "V-38, ..." (2), con un albero finto su tre livelli: lo stesso elenco nello stesso ordine, 12 richieste
    senza copia locale e 7 con; una cartella speciale della copia che non c'è più si richiede, e l'Archivio scelto
    altrove si vede.
- **v2 V-26** (la notifica aspetta il conteggio dei non letti e, una volta all'ora, la Posta inviata). CONFERMATO nel
  codice, CORRETTO (`e7cda67`).
  - Misura, dal registro della Missiva installata (solo letto): un controllo con un arrivo di Gmail dura 1,4 s in mediana
    (64 controlli: 1,2 s al decimo percentile, 2,0 al novantesimo, fino a 6,2); senza novità 0,9 s in mediana (90
    controlli); con Graph 0,2 s in mediana (14 controlli, uno a 3,5 s). Il conteggio dei non letti, prima della notifica,
    è due comandi sulla connessione in sottofondo per Gmail e una richiesta di 56-83 ms per Graph. Quanto pesasse da solo
    in un arrivo vero non è stato misurato.
  - Correzione. `App.ShowThenCountAsync`: prima le righe nell'elenco e la notifica, con i non letti stimati; poi il
    conteggio, che corregge il numero; la Posta inviata dopo il conteggio. La riga del controllo nel registro dice anche
    "notifica a N ms".
  - Controllo: "V-26, ...": con il server che non risponde al conteggio la notifica c'è già; un conteggio non riuscito
    lascia il numero stimato.
- **v2 V-25** (Graph: le immagini incorporate prima del testo; l'invito dopo, in fila). MISURATO, NON CORRETTO. Un
  messaggio della casella dell'autore con quattro immagini incorporate (78 KB in tutto), aperto tre volte: corpo in 83-121 ms,
  poi le quattro immagini insieme (la più lenta 85-198 ms), "testo pronto" a 145-181 ms; senza aspettarle sarebbe intorno
  a 100 ms (una stima, non una misura). E solo quando il corpo non è già in memoria: il messaggio dopo quello aperto è
  scaricato in anticipo. Mostrare prima il testo vuol dire disegnare la pagina due volte, come per la conversazione, per
  meno di un decimo di secondo su pochi messaggi. Il caso delle foto grandi incollate nel testo resta com'era: nella
  copia di prova non ce n'è uno.
- **v2 V-28** (risposte con Gmail e IMAP: ogni salvataggio della bozza rilegge l'originale e ricalcola la citazione).
  CONFERMATO SU WINDOWS sul server IMAP finto, CORRETTO (`d96a505`). Tre salvataggi di una risposta: tre letture di
  Message-Id e References prima, una dopo; il testo citato, che prima si calcolava a ogni salvataggio (letto nel codice),
  una volta (`ReplySources` per provider, con l'identificativo stabile nella chiave; `MimeOptions.QuoteText`). La bozza
  resta la stessa. Controlli: "V-28, ..." (2).
- **v2 V-36 = v1 V-20** (controllo dei nuovi messaggi di Gmail e IMAP: NOOP ripetuti, date richieste a ogni giro).
  CONFERMATO SU WINDOWS sul server IMAP finto, CORRETTO in parte (`aa71753`).
  - Misura. Con due non letti di oggi già annunciati: NOOP, SEARCH, FETCH delle date, NOOP, SEARCH del numero.
  - Correzione, senza toccare che cosa si annuncia: le date di arrivo si chiedono una volta per UID
    (`ImapProvider.ArrivalDates`, da capo con una UIDVALIDITY nuova), e il conteggio, chiesto subito dopo sulla stessa
    connessione, non ripete il NOOP fatto da meno di 2 s. La ricerca dei messaggi nuovi fa sempre il suo.
  - Dopo: NOOP, SEARCH, SEARCH. Con un arrivo la data si chiede solo del messaggio nuovo.
  - Controlli: "V-36, ..." (2): gli stessi messaggi nuovi e gli stessi non letti di prima, anche con un arrivo fra due
    controlli.
  - Non fatti: la ricerca per UID al posto di quella per data, e il solo conteggio per gli avvisi di soli segni.
- **Lasciato dal blocco 8** (`Store.Save()` a ogni messaggio in Posponi). CORRETTO (`0fd5daf`). In Posponi e nel ritorno
  dei posposti `SaveSoon` a ogni messaggio e un salvataggio subito alla fine, anche se un messaggio non riesce. Nel
  controllo, che rifà lo stesso schema su un `SettingsSaver` suo, dieci messaggi: da 10 scritture sul thread
  dell'interfaccia (145-223 ms in tutto) a 1 (19-20 ms). Gli altri cicli guardati: gli invii pianificati salvano dopo
  ogni invio e restano così (un salvataggio perso rimanderebbe il messaggio); la coda delle azioni salva una volta per
  gruppo.
- **v2 V-32 e v1 V-23, primo punto** (loghi letti e decodificati sul thread dell'interfaccia). MISURATO, NON CORRETTO.
  Sui 64 loghi veri della copia di prova (2,8 KB in mediana), fuori da Missiva, in PowerShell con le stesse classi di
  WPF: la decodifica, una per dominio per sessione, 0,69 ms in mediana e 1,39 al novantesimo percentile (una a 23 ms); la
  lettura dei byte per il riquadro 0,09 ms a messaggio. Meno di un decimo di fotogramma per dominio nuovo. Decodificarli
  in sottofondo vorrebbe dire righe che compaiono con le iniziali e poi cambiano.
- **v2 V-33 = v1 V-15** (la conversazione rifà la pagina; gli script vanno in fila). CORRETTO per gli script
  (`b6651ba`), la pagina no. Alternando i due modi sulle stesse dieci righe, 16 aperture l'uno: da "testo pronto" a
  "pagina mostrata" 17 ms in mediana con tre script in fila e 10 con uno (in media 20,1 e 12,3); dal clic 109 e 98 ms.
  Sette millisecondi ad apertura. La pagina rifatta all'arrivo della conversazione resta com'è.
- **v2 V-31 = v1 V-02** (riga dell'elenco: ogni testo formattato due o tre volte). MISURATO, NON FATTO. "costo righe"
  due volte: una riga nuova costa 2,72 e 2,08 ms in mediana, una `ListBox` banale 2,71 e 2,94. La stima dei rapporti è
  0,1-0,3 ms a riga: sta dentro la differenza fra due misure. Non c'è stato il tempo per provarlo con il confronto dei
  pixel.
- **Lasciato dal blocco 4** (dopo Canc o Ctrl+Z il messaggio successivo arriva in ritardo). CAUSA TROVATA, CORRETTO
  (`c59eb13`).
  - Succedeva ancora: in due sessioni, venti prove, "testo pronto" oltre 580 ms in dodici.
  - Causa, vista mettendo in fila le operazioni del Dispatcher per un secondo dopo il tasto: una riga tolta o aggiunta
    cambia l'altezza dell'elenco, e `StableScrollBar` assorbiva il cambio del cursore in 0,65 s disegnando a ogni
    fotogramma (79 fotogrammi). Finché WPF disegna così, le operazioni a priorità Input, cioè le riprese dopo un tasto,
    restano in coda: quelle messe in coda a 170 e 263 ms sono partite a 577, quando WPF stesso ha abbassato a Input la
    priorità dei disegni.
  - Correzione: da fermo, e fino a una schermata di righe, il cursore va subito al suo posto. Durante lo scorrimento
    resta com'era.
  - Dopo: nessun fotogramma animato in 12 prove su 12; "testo pronto" a 129-180 ms dopo Canc (380 la prima, su una
    newsletter da 211 KB) e 75-131 dopo Ctrl+Z.
  - Esclusi prima di trovarla: `HurryDelay`, `ShowDelay`, la barra di Annulla (non ha animazioni), un blocco del thread
    dell'interfaccia (nessuna riga "interfaccia lenta").

### Segnalazioni dell'autore

- **"Non c'è annulla iscrizione"** (blocco 6, F-11). CONFERMATO SU WINDOWS, CORRETTO (`e028c11`).
  - Causa. Gmail e IMAP aprono i messaggi dalla copia su disco, e le copie fatte prima del blocco 6 non hanno
    List-Unsubscribe. Un controllo scritto prima della correzione falliva ("comando: no; dal server: OneClick"), e nella
    versione di prova, sulla copia vera, i corpi venivano dalla copia senza le intestazioni.
  - Correzione. La copia dice se le ha lette (`SavedBody.ListHeadersRead`). Per quelle che non le hanno mai lette il
    riquadro chiede al server le sole intestazioni, in sottofondo con EXAMINE (`IMailProvider.GetListHeadersAsync`),
    aggiorna la copia e aggiunge la riga alla pagina già mostrata con uno script, senza rifarla. Una volta per messaggio.
  - Dal vivo, con Gmail: 155-180 ms per le intestazioni; quattro dei sette messaggi aperti dalla copia le avevano, e la
    riga è comparsa (vista in una foto e con UI Automation); alla seconda apertura nessuna richiesta. Su una newsletter
    da 211 KB le intestazioni arrivano prima della pagina: una pagina sola, la riga entra quando è mostrata.
  - Con Graph la copia in linea non si usa e le intestazioni arrivano con il corpo (61-83 per messaggio), ma nessuno dei
    dodici messaggi aperti nella casella di Outlook dell'autore ha List-Unsubscribe: lì la riga dal vero non si è potuta
    vedere. Resta il controllo del blocco 6 con il Graph finto.
  - Controlli: "annulla iscrizione, IMAP/Gmail, messaggio aperto da una copia su disco ..." (2).
- **Allegati grandi con Microsoft: "non c'è nessuna parte che indichi l'avanzamento"** (blocco 6, F-15). CONFERMATO nel
  codice, CORRETTO (`de727f6`).
  - Causa. Con Annulla invio la scrittura è nascosta: la barra "Invio in corso" spariva alla fine dell'attesa e
    l'avanzamento andava nella riga di stato della finestra nascosta.
  - Correzione. La barra resta finché l'invio non è finito e dice i MB caricati sul totale e il file, con i numeri per
    primi ("Invio in corso: 6,3 di 10 MB caricati (nome)"). Durante il caricamento Annulla lo ferma fra un pezzo e
    l'altro e riapre la scrittura con "Invio annullato". Lo stesso testo corto nella riga di stato della scrittura.
  - Visto con un caricamento finto (`MISSIVA_CARICAMENTO_FINTO=MB`, solo Debug) su un account IMAP finto: la barra in
    attesa e durante il caricamento, Annulla in più momenti, l'invio fino in fondo, la riga di stato a finestra aperta.
    La prima versione tagliava i MB dietro il nome del file, e dopo Annulla un avviso in ritardo poteva rimettere
    "Caricamento" nella riga di stato (visto una volta in una foto): corretti tutti e due prima del commit; dopo, quattro
    Annulla in momenti diversi, sempre "Invio annullato".
  - Controllo: "allegati grandi, barra degli invii ...": solo il testo della barra. Il resto è stato visto a schermo.

### Trovato in più

- Qualunque disegno a ogni fotogramma (`CompositionTarget.Rendering`: lo scorrimento con la rotella, il cursore della
  barra) può tenere in coda le operazioni a priorità Input fino a mezzo secondo: è quello che la traccia mostra per il
  cursore della barra. Qui è corretto il solo caso di Canc e Ctrl+Z. Un clic o un tasto mentre l'elenco scorre ancora
  non è stato misurato.
- Corretto, nelle sole prove (`64c800d`): nelle prove del blocco 2 il certificato fatto al volo e l'autorità che lo firma
  scadevano tutti e due "fra sei mesi", calcolati in due istanti; a cavallo di un secondo la suite si fermava con
  un'eccezione. Visto una volta.

### Che cosa cambia per chi usa Missiva

- "Annulla iscrizione" compare anche sui messaggi già scaricati prima dell'aggiornamento, un attimo dopo il testo la
  prima volta, subito le volte dopo.
- Inviando un allegato grande con un account Microsoft si vede quanto è stato caricato, e si può fermare.
- Dopo Canc o Ctrl+Z il messaggio successivo compare subito.
- La notifica di un messaggio nuovo non aspetta più il numero sull'icona.
- Con un account Microsoft la posta consuma meno traffico (le risposte arrivano compresse) e le cartelle si aggiornano
  prima all'avvio; l'attesa di cinque secondi che capitava ogni tanto all'avvio dovrebbe sparire.
- Posporre molti messaggi insieme fa una scrittura sola delle impostazioni invece di una per messaggio.

### Scelte fatte qui, da rivedere

- **Il limite per casella di Outlook**: quattro richieste insieme, tre che non aprono un messaggio, due scaricamenti.
  Anche la copia degli ultimi 30 giorni passa come apertura di un messaggio: può occupare tutti e quattro i posti, e un
  clic allora aspetta la fine di una richiesta (decimi di secondo).
- **Le cartelle speciali dalla copia locale**: se la copia fosse sbagliata non si correggerebbe da sola. Oggi nasce solo
  dalle risposte del server.
- **I non letti stimati** fra la notifica e il conteggio: per qualche decimo di secondo il numero può essere diverso da
  quello del server.
- **Il NOOP non ripetuto** entro 2 s prima del conteggio: un messaggio arrivato in quei 2 s entra nel numero al
  controllo dopo.
- **Posponi interrotto a metà** (crash, corrente): si può perdere l'ora di ritorno dei messaggi degli ultimi 2 secondi,
  che restano nei Posposti.
- **Il cursore della barra** va subito al suo posto quando l'elenco cambia da fermo fino a una schermata: in un elenco
  corto si vede cambiare di colpo, come in una barra qualsiasi.
- **Le intestazioni di "Annulla iscrizione"** si chiedono una volta per ogni messaggio aperto da una copia vecchia, anche
  per quelli che non le hanno: una richiesta leggera in più alla prima apertura.
- Annulla durante il caricamento c'è solo dopo il primo avviso di avanzamento: nel resto dell'invio non c'è.
- HTTP/2 è chiesto anche a Graph, che oggi risponde in 1.1: se Microsoft lo accendesse, Missiva lo userebbe senza che
  qui sia stato provato.

### Non verificato

- **La Missiva installata** non è stata eseguita, e la versione finale non è stata compilata.
- V-23: una rete lenta (telefono); uno scaricamento grande vero e un caricamento a pezzi vero con il gestore nuovo (solo
  sul server finto); Google Contatti (visto solo il calendario).
- V-24: i 429 erano 2 su 79 letture delle cartelle, e nove avvii senza non dicono che siano spariti. Nella prova gira anche la Missiva
  installata sulla stessa casella, che conta nel limite di Outlook.
- V-38: un account con molte sottocartelle; una casella condivisa; il primo avvio senza copia locale dal vivo.
- V-26, V-36: nessun arrivo vero è stato visto con il codice nuovo (nella prova non si può mandare posta): l'ordine e i
  comandi sono provati solo sui finti. Quanto prima arrivi la notifica lo dirà "notifica a N ms" nel registro.
- V-28: una risposta scritta nella finestra, con il salvataggio automatico.
- Posponi e i ritorni nella finestra: con `MISSIVA_NO_READ` la prova non li fa. Il controllo prova lo schema dei
  salvataggi, non le due funzioni della finestra.
- V-33: l'occhio. I tempi sono del registro; non è stato fatto un confronto dei fotogrammi con `lampi.ps1`.
- Canc e Ctrl+Z con la tastiera vera, e Archivia e Sposta. Lo scorrimento dopo la modifica: un solo "scorri 30" (294
  fotogrammi in 3 s), senza confronto con prima e senza guardare il cursore della barra.
- Annulla iscrizione: un clic sul comando comparso dopo (non è stato premuto); una conversazione con più messaggi
  aperti; le finestre dei messaggi.
- Allegati grandi: un caricamento vero verso Outlook e il suo Annulla (avrebbe scritto sulla casella); il salvataggio di
  una bozza con un allegato grande a schermo (nella prova le bozze vanno solo nel registro: la riga di stato è stata
  vista durante un invio a finestra aperta, che usa lo stesso avviso); più invii insieme.
- La suite completa è passata sul codice di `c1d2b54` (778 controlli), di `64c800d` (788) e di `c59eb13` (789, "tutto
  bene"). Due giri sono caduti prima: uno su "ricerca che nessuno aspetta più" (i tempi, già noto), uno sul certificato
  descritto sopra. Gli altri commit sono stati compilati e provati con i controlli del blocco.

## Blocco 10: sicurezza, punti di gravità Bassa

Fatto il 5 ottobre 2026. "Prima" è il commit `206e528`. Sono i punti di gravità Bassa di `sicurezza.md` rimasti dopo i blocchi
1-9 (gli Alti e i Medi sono tutti corretti). Ogni punto è stato riletto nel codice di oggi (le righe del rapporto si sono
spostate), provato con un controllo dove si poteva, corretto solo se confermato. Tre modi di provare: i controlli della suite
(`SecurityLowAuditTests`, 54, da soli con `--prova-audit risultato.txt sicurezza`: cartelle temporanee loro, un canale con
nome loro, un server HTTP finto, il vero processo dei PDF nel suo AppContainer); `Missiva.exe --prova-anteprima` a mano con la
Debug e con una versione finale di prova a un file solo (`dotnet publish -c Release -p:MissivaProva=true`, in una cartella
mia, poi tolta); la lettura dei sorgenti dove nessun controllo può creare una finestra. Nessuna richiesta di rete vera, a parte
quella del punto S-32; la Missiva installata non è stata toccata né avviata.

Dove il codice di prima si poteva rimettere (rimettendo da parte la correzione, `git stash`, e ricompilando) il controllo è
stato visto fallire: S-11, S-19, S-15 e, per la variabile d'ambiente, S-13. Per gli altri il "prima" è la lettura del codice,
e lo dico punto per punto.

### Punti del rapporto

- **S-11** (`Safety.Dangerous` senza .one e .onepkg). CONFERMATO SU WINDOWS (il controllo fallì sul codice di prima), CORRETTO
  (`5ab58e0`). Aggiunti `.one` e `.onepkg`, e anche `.psm1`: come `.psd1` e `.ps1xml`, che c'erano già, costa solo una
  domanda e un modulo di PowerShell non è un allegato che si spedisce spesso. I documenti con macro (`.docm`, `.xlsm`,
  `.pptm`) restano fuori, come dice il rapporto: se ne occupa Office con il marchio web.
- **S-28** (allegati temporanei in `%TEMP%\Missiva` per tutta la sessione). CONFERMATO leggendo il codice (`CleanTemp` era
  chiamata solo all'avvio e all'uscita), CORRETTO (`5ab58e0`). Il timer di ogni minuto che c'è già (`snoozeTimer`) la
  chiama per le cartelle più vecchie di un'ora, su un thread del pool. L'età non è solo quella della cartella: è l'ultimo
  momento fra la sua creazione e l'ultima modifica dei file dentro, così un documento aperto in Word, modificato e salvato,
  resta un'altra ora dopo l'ultimo salvataggio e non sparisce un minuto dopo aver chiuso Word. Un file in uso resta (la
  cancellazione fallisce e riprova al minuto dopo); all'uscita va via tutto, come prima. Controlli: 3, scritti dopo la
  correzione (cartelle di età diverse, una modificata da poco, una tenuta aperta).
- **S-10** (marchio web mancante su Salva .eml e Salva come PDF; l'errore del marchio ingoiato). CONFERMATO leggendo il
  codice, CORRETTO (`5ab58e0`). `MarkFromInternet` dice se ci è riuscita; Salva con nome .eml passa da
  `Safety.SaveDownloadedAsync` (scrive e marca) e `ReadingView.SavePdfAsync` marca il PDF; Apri, se il marchio non si
  scrive (cartella temporanea senza flussi alternativi), chiede conferma solo per i documenti di Office e i tipi pericolosi
  (`Safety.NeedsWebMark`, `Dialog.ConfirmOpenUnmarked`), gli altri tipi si aprono come prima; lo stesso per i documenti
  firmati (.p7m). Controlli: 3 (il flusso `Zone.Identifier` letto dopo il salvataggio, un marchio impossibile che si dice, i
  tipi che contano).
- **S-09** (nomi degli allegati con caratteri bidirezionali e invisibili, e nomi diversi per scheda e salvataggio). CONFERMATO
  SU WINDOWS (con i nomi `virus.ex?e` e `a.vbs:` scheda e Apri decidevano su due nomi diversi: la scheda sul grezzo, Apri su quello
  pulito), CORRETTO (`c1170a5`). Un solo nome, calcolato una volta alla nascita come `Address.Name` con `CleanName`:
  `Safety.CleanAttachmentName` passa da `Attachment.Name` e da `MessageSummary.AttachmentNames`, per ogni provider e per le
  copie locali (che si rileggono pulite). Toglie i caratteri invisibili (U+202E, U+200B, U+2066, controllo) e fa quello che
  faceva `SafeFileName` (percorsi, caratteri vietati, nomi riservati, 150 caratteri, la fine se più lungo); `SafeFileName`
  stesso ora toglie i caratteri invisibili (vale anche per i nomi proposti da Salva con nome). Un nome normale torna la stessa
  stringa senza allocare. Controlli: 5 (nove nomi travestiti, nomi normali, elenco, copie locali di prima).
- **S-19** (configurazione automatica: DTD, e l'ISPDB dall'MX senza conferma). CONFERMATO SU WINDOWS (due controlli falliti
  sul codice di prima: un XML con `<!DOCTYPE>` e un'entità dava il server `imap.entita-prova.it`, e la configurazione
  passata dall'MX non era `Foreign`), CORRETTO (`7c890d7`). L'XML si legge con `XmlReader` (`DtdProcessing.Prohibit`, nessun
  resolver); la configurazione dell'ISPDB scelta dal dominio dell'MX è `Foreign` se i server sono fuori dal dominio
  dell'indirizzo, e la finestra chiede conferma prima di mandare la password (il testo non parla più dei soli record SRV).
  Controlli: 3.
- **S-21** (la risposta dalla notifica parte senza finestra). CONFERMATO leggendo il codice, CORRETTO (`0611d1b`):
  `NotificationActionAsync` con `reply` e un testo apre sempre la scrittura con quel testo (`OpenQuickReplyWindow`), con la
  nota se `QuickReplyRisk` trova un rischio; `ReplyFromNotificationAsync`, che inviava, non c'è più. `QuickReply` e
  `QuickReplyBody` restano (le prove della risposta dei provider li usano) ma l'app non le chiama più. Controlli: 2 (il
  metodo non c'è più; il ramo letto dal sorgente non chiama nessun `SendAsync`). Nessuna prova crea la finestra principale,
  quindi l'apertura vera della scrittura da una notifica non è stata vista.
- **S-22** (canale con nome: lettura senza limite né scadenza, `AllowSetForegroundWindow(-1)`). CONFERMATO leggendo il codice,
  CORRETTO (`c6cfb90`). `App.ReadCommandAsync` legge una riga di al più 64 KB entro 3 secondi, altrimenti chiude il
  collegamento e se ne apre un altro. Il primo piano va al processo all'altro capo del canale
  (`GetNamedPipeServerProcessId`), non a tutti. Non fatto: riprovare `SendToRunning` quando il mailto: si perde perché la
  copia aperta non è ancora pronta, e controllare che il processo del server sia lo stesso eseguibile (il rapporto li
  propone; fuori dal compito di questo blocco). Controlli: 5, su un canale vero con un nome loro: una riga normale (anche con
  \r\n, senza a capo, accentata), un client fermo e uno con mezza riga (la lettura finisce alla scadenza: 414 e 401 ms con
  400 ms), 2 MB senza a capo rifiutati in 5 ms, 64 KB giusti passano e un byte in più no, e il processo del server è questo.
- **S-05** (host con il punto ideografico in `LinkTarget`). CONFERMATO SU WINDOWS (il controllo fallì: restavano fra i link
  almeno `missiva。example`, `missiva．example`, `missiva｡example` e le lettere a larghezza intera, che Chromium legge come
  `missiva.example`; la lista completa non è nel risultato, che la tagliava), CORRETTO (`f5af8a5`): si confronta
  `uri.IdnHost` (provati anche il trattino facoltativo, il punto finale, la porta), e un host che non si normalizza si toglie.
  Gli altri link restano com'erano (anche con un host non latino). Controlli: 2.
- **S-15** (immagini "di Missiva" dal solo suffisso del cid, e `Decode` solo per larghezza). CONFERMATO SU WINDOWS per
  l'altezza (sul codice di prima un PNG 1440 x 65535 arrivava intero, uno 100 x 30000 intatto, uno da 105 milioni di pixel
  si decodificava), CORRETTO per quella parte (`0d6f0b1`): `EditorImages.Decode` rimpicciolisce anche per altezza (8192
  pixel) e non decodifica oltre 100 milioni di pixel; le immagini normali escono come prima (4000 x 3000 a 1440 x 1080).
  **Il cid NON CORRETTO, per scelta**: un codice di controllo (HMAC con una chiave locale) lascerebbe senza immagini le bozze
  già salvate con i cid di oggi, e quelle aperte da un altro PC; l'elenco dei cid per bozza avrebbe lo stesso difetto. Il
  rischio rimasto è che Missiva decodifichi con i quattro decodificatori espliciti (dalla firma dei byte) un'immagine scelta
  dal mittente, come già fa per quelle che l'utente incolla, ora con i tetti. Controlli: 5.
- **S-12** (risposte del processo dei PDF non validate). CONFERMATO leggendo il codice, CORRETTO (`f6d9f7b`). Missiva controlla
  quello che riceve (`PdfWorker.ReadLoaded`, `ReadPng`, `ReadMessage`): da 1 a 5000 pagine e misure finite; un PNG con firma
  e IHDR al suo posto, larghezza fino a 4000, altezza fino a 16384 e 40 milioni di pixel (con qualche pixel di tolleranza),
  letti prima di darlo a `PngBitmapDecoder`, al massimo 100 MB (prima 200); un messaggio d'errore di al più 1024 caratteri
  senza a capo né caratteri di controllo. Una risposta fuori dal protocollo (`WorkerProtocolException`) chiude il processo e
  ne riparte uno alla richiesta dopo. Il processo rispetta gli stessi limiti: una pagina molto alta si disegna più stretta con
  le sue proporzioni (100 x 20000 punti, chiesta a 1000 pixel, arriva 112 x 16128), un'immagine ha il tetto su altezza e
  pixel (1440 x 65535 arriva 360 x 16384, non intera), oltre 150 milioni di pixel o 5000 pagine un errore che l'anteprima
  spiega. Controlli: 8, con il processo vero e con risposte costruite a mano (2^31-1 pagine, misure NaN o infinite, IHDR
  100000 x 100000, un PNG da 2 GB dichiarato, un errore da 100.000 caratteri). `PdfTestsAsync` (13) resta verde. Il picco di
  memoria del processo dopo le richieste più grandi: 92 MB impegnati, 115 MB in uso.
- **S-13** (AppContainer dei PDF). CONFERMATO SU WINDOWS per l'ambiente (la variabile finta di Missiva era "presente" nel
  processo), CORRETTO (`f6d9f7b`):
  - ambiente in lista bianca (`Sandbox.EnvironmentBlock`: SystemRoot, windir, SystemDrive, TEMP, TMP, PATHEXT, OS,
    PROCESSOR_ARCHITECTURE, NUMBER_OF_PROCESSORS, DOTNET_EnableDiagnostics=0 e, per l'eseguibile unico,
    DOTNET_BUNDLE_EXTRACT_BASE_DIR) più LOCALAPPDATA: senza, CreateProcess di un AppContainer dà l'errore 203 (provato una
    variabile alla volta; USERPROFILE e APPDATA non servono). Niente PATH, USERPROFILE, DOTNET_STARTUP_HOOKS;
  - job obbligatorio: se non si crea, se un limite non si imposta o se il processo non ci entra, il processo avviato
    sospeso si termina (`Sandbox.Abort`) e l'anteprima dice che non si è potuta avviare (provato con un guasto di prova,
    contando i processi di Missiva prima e dopo);
  - nel job, oltre a un solo processo e alla chiusura con Missiva, un limite di memoria di 3 GB (il picco misurato è 92 MB) e i
    limiti dell'interfaccia (handle USER, appunti, parametri di sistema, schermo, atomi, desktop, uscita da Windows), riletti da
    Windows nel controllo. Nell'AppContainer gli appunti erano già negati prima (errore 5) e restano negati; `GetDC(NULL)` il
    processo lo ottiene lo stesso anche con i limiti (non è un divieto che questo job dia: il processo non ha dove mandare lo
    schermo, niente rete, file o appunti), quindi non lo promette nessun controllo;
  - la lettura data al SID dell'AppContainer: con l'eseguibile unico va al solo file, senza ereditarietà, e non più a tutta la
    cartella da cui parte (Download, Scrivania). Nella compilazione di prova (con le librerie accanto) resta la cartella.
    Provato con la versione finale di prova a un file solo, due volte (con `Assembly.Location` e poi con il controllo di
    `Missiva.dll` accanto): il processo parte nell'AppContainer e nel job, disegna un PDF e un PNG, e `icacls` mostra la
    voce sul solo file. Non fatto: `disinstalla.ps1` non toglie la voce (la cartella dell'installazione si cancella con
    lui; quelle su cartelle di prima restano).
  Controlli: 5, più `PdfTestsAsync` e `--prova-anteprima` a mano con la Debug e con la versione finale di prova.
- **S-27** (nomi di file e registro che rivelano i corrispondenti). CONFERMATO le righe, CORRETTO la parte del registro
  (`4ae45ca`): `Log.Private` per il nome dell'allegato in anteprima, del file firmato, dell'immagine inserita nel testo e del
  calendario di Google non letto; `Log.Error` nella versione installata toglie dal testo dell'eccezione gli indirizzi
  (`Log.WithoutAddresses`: gli errori di SMTP e IMAP riportano la risposta del server, per esempio
  `550 5.1.1 <(indirizzo)>: Recipient address rejected`); nella versione di prova restano. I nomi dei file dei loghi e degli
  avatar delle notifiche restano com'erano (il blocco 2 li ha appena cambiati). Controlli: 3 (la pulizia degli errori, che
  `Log.Private` non lascia il nome nudo, le quattro righe lette dai sorgenti). Il registro della versione installata non è
  stato aperto: il ramo che toglie gli indirizzi è compilato nella versione finale di prova ma non eseguito.
- **S-26** (dati personali in chiaro in `impostazioni.json`). CONFERMATO leggendo il codice, CORRETTO in parte (`65d08e2`).
  Cifrati con DPAPI: le firme (`Signature` e `NamedSignature`: il testo RTF, con nome, telefono, a volte IBAN),
  `ImageSenders` e `HiddenCalendars` (per Google l'identificativo di un calendario è un indirizzo). `Signature.Rtf` resta la
  proprietà dell'app e vale vuoto se il cifrato non si apre, così il segnaposto non entra mai in un messaggio né nella scelta
  della firma; `StoredRtf` (nel file sempre "Rtf") è il valore del file, che al salvataggio torna com'era. Le impostazioni
  scritte in chiaro dalle versioni di prima si leggono e si cifrano al primo salvataggio; un elenco che non si decifra torna nel
  file com'era anche dopo `UpgradeImageSenders` all'avvio, e riparte se si sceglie di mostrare le immagini di un mittente (come
  le ricerche recenti); la copia di sicurezza (blocco 7) non porta i segnaposto e dice quante firme su questo PC non si
  leggono più, e firme, mittenti e calendari vanno e vengono in chiaro dentro il suo file cifrato come prima. L'avviso
  dell'avvio nomina anche firme, mittenti e calendari. **NON cifrate, per scelta**: le chiavi delle mappe di cartelle e
  categorie (`FolderOrder`, `FolderColors`, `Favorites`, `CollapsedFolders`, `LocalCategories`, `CategoryColors`, `Pinned`):
  servirebbe un convertitore che cifra la mappa intera con la stessa gestione dei segnaposto, della copia di sicurezza, di
  `ForgetAccount` e del modo in cui l'interfaccia le legge e le scrive a ogni clic; più rischio che protezione per un file
  che resta nella cartella dell'utente. Controlli: 8 (JSON senza testi in chiaro, andata e ritorno, file di prima, quattro
  cifrati illeggibili, copia di sicurezza con e senza). Un controllo del blocco 7 è stato adattato: sul "PC di partenza" la
  firma ora è cifrata e non si legge più, quindi resta fuori dalla copia come la regola e il modello (5 note invece di 4).
- **S-08** (profilo di WebView2 persistente): valutato, NON CAMBIATO. Il punto è vero (cache HTTP e cookie dei server delle
  immagini consentite restano in `%LOCALAPPDATA%\Missiva\WebView2` fra le sessioni; con le immagini consentite la richiesta passa
  da Chromium). Consiglio: se si vuole fare qualcosa, la cosa più piccola è togliere i cookie all'avvio del riquadro
  (`Profile.ClearBrowsingDataAsync(Cookies)`, in un `try` perché dipende dal runtime), perché l'indirizzo dell'immagine di un
  mittente fidato dice già chi ha aperto il messaggio e il cookie in più identifica il PC fra una sessione e l'altra; la cache
  su disco serve a non riscaricare i loghi. `IsInPrivateModeEnabled` lo sconsiglio senza misure: cambia il profilo del riquadro
  (memoria, avvio, immagini riscaricate a ogni sessione) e qui non c'è modo di provarlo senza finestra. Il livello di
  prevenzione del tracciamento "Strict" blocca in silenzio immagini di domini classificati: non lo consiglio. Non toccato perché
  non è banale provarlo (nessun controllo crea un WebView2) e il riquadro è la parte che l'autore ha reso veloce.
- **S-06** (gettone della pagina in attributi raggiungibili dal CSS dell'email): valutato, NON CAMBIATO. Il rapporto stesso lo
  misura non leggibile oggi (Chromium scarta `:has` dentro `:host-context`), e il gettone vale per la pagina mostrata e basta,
  per pulsanti che l'email non può premere senza JavaScript e che un link dell'email non raggiunge (`LinkTarget` toglie i link
  verso l'host della pagina, anche con il punto ideografico, S-05). Consiglio: lasciare com'è. Spostarlo fuori dal DOM vorrebbe dire
  riscrivere come i pulsanti dichiarano l'azione, o mettere l'email in un frame: troppo, per un rischio che dipende da una
  scelta del motore e che non dà potere a chi lo leggesse.
- **S-32** (installazione e NuGet). `dotnet list Missiva.csproj package --vulnerable --include-transitive` (5 ottobre 2026,
  origine api.nuget.org): "il progetto specificato `Missiva` non contiene pacchetti vulnerabili". Nessun pacchetto aggiornato. Resta
  l'avviso NU1510 su `System.Security.Cryptography.ProtectedData` (il pacchetto è già nel framework; non toccato).

### Trovato in più

- `CreateProcess` di un processo in un AppContainer, con un ambiente costruito a mano, non riesce senza `LOCALAPPDATA`
  (errore 203): chi stringe l'ambiente del processo dei PDF deve lasciarla.
- Dentro l'AppContainer del processo dei PDF `GetDC(NULL)` funziona, con e senza i limiti dell'interfaccia del job: un processo
  compromesso potrebbe vedere lo schermo, ma non ha come portarlo fuori (nessun canale oltre al tubo verso Missiva).
- Due righe di `GmailProvider` (`tempi: N messaggi di {cartella}` e `tempi: ricerca ... in {cartella}`) usano `Log.Write` e non
  `Log.Time`: finiscono anche nel registro della versione installata con il nome della cartella. Non toccate (fuori dal compito).
- `MessageSummary.AttachmentNames` e `Attachment.Name` ora passano da un punto solo: chi aggiunge un provider ha i nomi puliti
  senza fare niente.

### Che cosa cambia per chi usa Missiva

- Un allegato .one, .onepkg o .psm1 chiede conferma prima di aprirsi, come gli altri tipi che possono eseguire codice.
- Gli allegati aperti si cancellano da %TEMP% dopo un'ora dall'ultima modifica (non più solo all'uscita); un file ancora aperto
  in un programma resta.
- Il messaggio salvato come .eml e il PDF salvato dal riquadro hanno il marchio "scaricato da Internet": Windows può mostrare "Sblocca"
  nelle proprietà, e Office apre in Visualizzazione protetta.
- I nomi degli allegati si vedono senza caratteri invisibili e con i caratteri vietati tolti, come saranno salvati; un allegato
  inoltrato porta il nome pulito.
- Aggiungendo un account IMAP con un dominio proprio ospitato da un provider (l'MX porta a un altro dominio), la finestra chiede
  conferma dei server prima di mandare la password, come già faceva con gli SRV.
- La risposta di una notifica con un testo non parte mai da sola: si apre la scrittura (le notifiche di oggi non hanno la casella).
- I PDF con più di 5000 pagine, e le pagine altissime, hanno un'anteprima diversa (un errore detto, e una pagina più stretta).
- Firme, mittenti delle immagini e calendari nascosti stanno cifrati nel file: copiare `impostazioni.json` su un altro PC non basta
  più per averli (come per modelli e regole): serve la copia di sicurezza.

### Scelte fatte qui, da rivedere

- **Il nome dell'allegato è sempre quello pulito**, anche per gli allegati inoltrati (un nome con `:` o più lungo di 150
  caratteri non arriva identico al destinatario). In cambio scheda, pastiglia, domande e salvataggio non possono più dire cose diverse.
- **S-19**: i domini propri su un hosting (Aruba, Register...) chiedono una conferma in più alla prima aggiunta. È il prezzo di
  trattare l'MX come gli SRV: il rapporto lo chiede, e nessun altro modo distingue un provider vero da uno ricomprato.
- **Il cid `@missiva` non cambia** (S-15): vedi sopra. Se l'autore preferisce la difesa piena, il prezzo è perdere le immagini
  delle bozze già salvate.
- **Limiti dei PDF**: 5000 pagine, 4000 x 16384 e 40 milioni di pixel per pagina, 100 MB per risposta, 3 GB di memoria. Li ho
  scelti larghi (il picco misurato è 92 MB, una pagina A4 a 4000 pixel pesa 90 MB di bitmap): un PDF vero non li tocca, ma sono
  numeri miei.
- **Lo schermo (`GetDC(NULL)`) resta raggiungibile** dal processo dei PDF (S-13): un alternate desktop lo impedirebbe, ma cambia
  come il processo parte e non si può provare altrove che su questo PC.
- **Le mappe di cartelle e categorie restano in chiaro** (S-26), e i nomi dei file dei loghi (S-27).
- **`HiddenCalendars` cifrato** oltre a quanto chiesto: è una lista semplice e i suoi valori sono indirizzi.

### Non verificato

- **La Missiva installata** non è stata eseguita né toccata, e il registro installato non è stato letto.
- **Nessuna finestra**: nessun controllo crea la finestra principale o una finestra di scrittura, quindi non sono viste a schermo
  la domanda di S-10 (marchio non scritto), la scrittura aperta da una notifica (S-21), la conferma dei server fuori dominio
  (S-19), né il nome dell'allegato girato da U+202E come lo mostrava la scheda (S-09, provato sulla stringa).
- S-10: Salva come PDF (richiede WebView2 vero) non eseguito; il caso di una cartella su FAT o exFAT provato solo con una cartella
  che non esiste, non con un disco vero; il timer di S-28 non l'ho visto scattare nella finestra, e non ho provato Word che salva.
- S-19: DNS e ISPDB veri; solo risposte finte.
- S-22: due copie vere di Missiva (mailto:, "esci" di `installa.ps1`) e il primo piano a schermo; il canale di prova non è quello vero.
- S-05: che Chromium normalizzi quegli host come dice il rapporto non l'ho provato in un WebView2.
- S-12 e S-13: nessun processo dei PDF compromesso (le risposte fuori misura sono costruite a mano e passano dalle funzioni che
  Missiva usa, non da un processo finto); il limite di memoria è letto da Windows ma non è stato superato; la lettura al solo file
  provata con la versione finale di prova a un file solo ma non con quella installata da `installa.ps1` (con ReadyToRun, 245 MB);
  `--prova-anteprima` non ha provato l'anteprima in finestra.
- S-26: un `impostazioni.json` vero (solo costruiti a mano); l'avvio della Missiva installata con firme in chiaro di prima.
- S-08 e S-06: valutati sulla lettura del codice e del rapporto, nessuna misura nuova.
- La suite completa (`suite-C.ps1`) è passata sul codice finale di questo blocco: 843 controlli, "tutto bene". Un primo giro era
  caduto su un controllo mio (S-13: confrontava i nomi delle variabili d'ambiente rispettando le maiuscole, e nella suite
  Windows li dà in maiuscolo): corretto il controllo, non il codice. Prima del giro finale era passato anche l'insieme dei
  controlli dell'audit (blocchi 1-10) da solo, 270 controlli. "ricerca che nessuno aspetta più" non è caduta.

## Dopo l'installazione: due difetti segnalati dall'autore il 7 ottobre

Sulla versione installata il 5 ottobre: (1) "ogni tanto il counter delle mail da leggere rimane bloccato a 1 anche se sono
tutte lette"; (2) "le notifiche arrivano puntuali, ma l'elenco si aggiorna tardi". Missiva sta nell'area di notifica, con
quattro account (due Gmail, Outlook.com, IMAP). Dati: il registro della versione installata (solo letto, 5-7 ottobre) e la
versione di prova con due server IMAP finti (`loop\bug-I\prova-finta.ps1`). Commit `c3ceb8b`, `16e590e`, `9c8d0e3`, `d2e58da`.

### Che cosa diceva il registro, e che cosa no

- Gli arrivi: "IDLE: nuovo messaggio", un secondo dopo "notifica mostrata" e "numero sull'icona". Quando la riga entrava
  nell'elenco non era scritto, e nemmeno chi cambiava i non letti: solo il totale. Mancanza corretta (`c3ceb8b`): ora
  "non letti di Gmail 2: 2 -> 1 (letto nel riquadro)" ed "elenco: 1 nuovi di Gmail 2 in cima", senza indirizzi.
- 7 ottobre, 12:26:35: messaggio aperto dalla notifica, finestra principale nell'area di notifica. 12:26:36 "numero
  sull'icona: 1" e nello stesso secondo "2". 12:27:04 avviso dell'IDLE (l'eco della lettura: Gmail lo manda 20-30 secondi
  dopo), controllo, "numero sull'icona: 1".
- 6 ottobre, 18:14:53 e 5 ottobre, 17:11:49: il numero sale di uno senza nessun arrivo, all'ora del controllo di ogni due
  minuti, subito dopo delle letture. Il 5 ottobre resta poi a 1 fino al giorno dopo, con il PC sospeso in mezzo.
- 6 ottobre, 11:15:33: Elimina dalla notifica di un messaggio da leggere. Il numero scende alle 11:17:18, con il controllo.
- 6 ottobre, 10:45:12: "Operation timed out after 120000 milliseconds" sulla connessione principale di un account Gmail,
  dopo la ripresa dalla sospensione: per due minuti quella connessione non ha servito altro.

### Difetto 2, l'elenco in ritardo. Prima causa: la prima pagina riletta senza gli ultimi arrivati (`16e590e`)

- Causa. La prima pagina dell'elenco di Gmail e IMAP si legge sulla connessione principale, che resta ferma sulla cartella
  aperta e la conta com'era all'ultimo annuncio del server; la pagina va dal messaggio `Count - 50` a `Count`. I messaggi
  arrivati dopo non ci sono finché il server non li annuncia, e li annuncia con NOOP (è lo stesso comportamento del 27
  settembre, corretto allora solo per la connessione in sottofondo). Ogni account ha la sua connessione principale ferma
  sulla sua Posta in arrivo: passando dall'account A all'account B dopo una notifica di B, la pagina di B usciva com'era
  l'ultima volta. E un elenco riletto (cartella scelta di nuovo, avviso dell'IDLE per un messaggio letto o eliminato sul
  telefono) toglieva la riga che il controllo aveva appena messo in cima.
- Prova. Versione di prova, server finto: arrivo, "elenco: 1 nuovi di IMAP 1 in cima (5 righe)", notifica; poi la stessa
  Posta in arrivo scelta di nuovo: "4 messaggi di Posta in arrivo", elenco di 4 righe senza il nuovo, non letti 2.
- Questo è anche il difetto 1 come lo vede l'autore: il numero è giusto (lo conta la connessione in sottofondo, che il NOOP lo
  fa), ma il messaggio da leggere nell'elenco non c'è, e quelli che si vedono sono tutti letti.
- Correzione. `ImapProvider.OpenFreshAsync`, anche per Gmail: un NOOP prima della prima pagina se la cartella era già
  aperta. Appena aperta (SELECT) e per le pagine dopo no. Un comando in più per ogni elenco riletto.
- Controlli. Il server finto ora annuncia gli arrivi solo con NOOP (`AnnounceOnNoop`), come i server veri. Prima pagina,
  arrivo, prima pagina riletta: il nuovo in cima; comandi "SELECT, FETCH", poi "NOOP, FETCH", poi "FETCH" per la pagina
  dopo. Per IMAP e per Gmail. Senza la correzione: "NO" tutti e due (riletta: solo "FETCH", 50 messaggi senza il nuovo).

### Difetto 2, seconda causa: la copia locale ferma all'ultima apertura (`9c8d0e3`)

- Causa. Con aperta un'altra cartella le righe nuove non vanno da nessuna parte (giusto), e la copia locale della Posta in
  arrivo di quell'account restava quella dell'ultima apertura: aperta dopo la notifica, mostrava l'elenco vecchio finché non
  rispondeva il server (e con una connessione principale ferma da ore c'è anche il tempo di ricollegarsi).
- Prova. "elenco: 1 nuovi di IMAP 2 non aggiunti (aperta un'altra cartella)", poi la cartella aperta senza il messaggio
  fino alla risposta del server.
- Correzione. `MainWindow.KeepInSavedPage`: i nuovi entrano nella prima pagina in memoria se la cartella è aperta,
  altrimenti in cima alla copia locale (`MailCache.Prepend`). Dopo: "aperta la Posta in arrivo dell'account 1, subito 5
  righe, la prima 5". Vale anche per "Tutti gli account" e per il riavvio.
- Controllo. Nuovi in cima alla pagina salvata, senza doppioni, niente copia per una cartella mai aperta.
- Non per gli account con Evidenziata e Altra (Outlook.com dell'autore): il messaggio nuovo non dice in quale scheda sta. Il
  loro elenco arriva da una richiesta nuova a ogni apertura, quindi il ritardo lì è solo il tempo della risposta.

### Difetto 1, il numero che risale o resta alto (`d2e58da`)

- Causa A. Il numero dei non letti lo scrivono in assoluto il conteggio del server (alla fine di ogni controllo) e le
  cartelle rilette. Una lettura qui abbassa subito il numero e parte verso il server: un numero chiesto un attimo prima, o
  con la scrittura ancora in viaggio, tornava dopo e rimetteva quello vecchio, fino al conteggio seguente. È la sequenza
  1, 2 del 7 ottobre; quale dei due l'abbia scritto il registro non lo diceva (nessun controllo registrato in quel
  secondo: il controllo di ogni due minuti senza novità non scrive nulla).
- Causa B. Un messaggio da leggere eliminato, spostato, archiviato o posposto senza aprirlo non usciva dal conto: per Gmail
  e IMAP l'avviso dell'IDLE che ne torna è l'eco di un'azione di Missiva e si ignora, senza ricontare. Il numero restava
  alto fino al controllo di ogni due minuti. Nel registro: 105 secondi il 6 ottobre. Nella prova: Elimina dalla notifica
  alle 13:13:37, numero corretto alle 13:14:09.
- Correzione. `UnreadSync` (`Core/Idle.cs`): per account, le modifiche locali e le scritture di Missiva in corso. Chi conta
  prende un biglietto prima di chiedere; il numero si scrive solo se da allora qui non è cambiato nulla
  (`FolderItem.ApplyServerUnread`, `AccountItem.SetServerFolders`); con una scrittura in viaggio, o finita da meno di un
  secondo, non si chiede. Finita ogni scrittura `App.RecountSoon` riconta dal server, una richiesta per raffica. Dopo, nella
  prova: Elimina dalla notifica alle 13:32:08, "non letti di IMAP 1: 3 -> 2 (ricontati dal server)" alle 13:32:09.
- Controlli. Conteggio chiesto prima di una lettura e tornato dopo (non si scrive, resta 1); chiesto dopo (si scrive);
  scrittura in viaggio e appena finita; arrivo stimato; cartelle rilette; scrittura non riuscita; un avviso a fine scrittura.
  Con `Valid` che risponde sempre sì il controllo fallisce.

### Sospetti controllati

- Il NOOP prima del conteggio c'è ancora: `GetNewAsync` lo fa sempre, il conteggio lo salta solo se è di meno di due secondi
  prima (blocco 9). Non è la causa. Il NOOP che mancava è un altro, quello dell'elenco, e mancava da prima dell'audit.
- `UpdateBadge` accorpato (blocco 4): scrive il totale di quel momento, non un numero vecchio. Non è la causa.
- `PrependRows` (blocco 4, punto 7) con un arrivo vero dal server finto: riga in cima nello stesso secondo della notifica,
  con il gruppo Oggi già presente ("Oggi:4:5") e senza ("Oggi:1:5" in cima a "Settimana scorsa"), a finestra aperta,
  nascosta e poi riaperta, e in "Tutti gli account". Non è la causa.
- `ShowThenCountAsync` (blocco 9): la riga e la notifica partono insieme. Il conteggio che torna dopo è la causa A.

### Che cosa cambia per chi usa Missiva

- Dopo una notifica, la Posta in arrivo di quell'account ha il messaggio in cima appena la si apre, e non sparisce più se
  l'elenco si rilegge.
- Il numero dei non letti non risale dopo una lettura, e dopo Elimina, Archivia o Sposta di un messaggio da leggere è
  giusto in un secondo o due.
- Un comando in più (NOOP) a ogni elenco riletto di Gmail e IMAP, e una richiesta di conteggio dopo ogni raffica di azioni.

### Non verificato

- Nessuna prova con arrivi veri su Gmail, Outlook.com e la casella IMAP dell'autore: tutto con il server IMAP finto, che annuncia
  gli arrivi solo con NOOP perché così l'ho scritto. Che Gmail si comporti così sulla connessione principale lo dice solo
  la misura del 27 settembre sulla connessione in sottofondo.
- La sequenza 1, 2 del 7 ottobre non l'ho riprodotta: con il server finto, che risponde in pochi millisecondi, il numero non
  risale nemmeno con un controllo ogni 3 secondi. La causa A è dedotta da chi può scrivere quel numero, e provata solo dal
  controllo automatico. Se si ripete, il registro ora dice chi ("contati dal server", "cartelle rilette", "scartati").
- Il numero rimasto a 1 dal 5 al 6 ottobre: non so quale account né quale messaggio. Può essere la causa dell'elenco (un
  messaggio da leggere che l'elenco non mostrava), un messaggio da leggere nella scheda Altra di Outlook.com (il numero di
  Graph conta tutte e due le schede, non cambiato), o una lettura non arrivata al server. Non distinguibile dal registro.
- Una conversazione con due messaggi da leggere: aprendola diventa letto solo il più recente, e la riga chiusa sembra letta.
  Letto nel codice (`ReadingView.MarkRead`, `MessageItem.IsRead`), non provato né cambiato: è una scelta di comportamento.
- Il timeout di MailKit resta di 120 secondi: una connessione morta dopo la sospensione può tenere ferma la principale di
  quell'account fino a due minuti (una volta nel registro). Ora in quel tempo il numero non risale, ma l'elenco e l'apertura
  dei messaggi di quell'account aspettano. Non toccato.
- Un arrivo proprio mentre la prima pagina è in viaggio: la pagina chiesta prima dell'arrivo può togliere la riga nuova fino
  alla rilettura dopo. Non provato, non corretto.
- Outlook.com: `UnreadSync` per Graph non ha un controllo suo (la suite prova il meccanismo con IMAP), e il secondo di attesa
  dopo una scrittura è una stima, non una misura dei tempi di Graph.
- La versione finale compila; non è stata installata né provata sui dati veri.

## Audit del 7 ottobre, parte A: avvio

Fatto nella notte fra il 7 e l'8 ottobre 2026 sul rapporto `avvio-e-velocita.md`, parte A (A-01..A-22). "Prima" è il commit
`de77f2b`. Commit: `90d2ffc` (gruppo 1), `ea12c3a` (gruppo 2), più quello di questo esito. Il rapporto era scritto leggendo il
codice su Linux: qui ogni punto è stato misurato sul PC dell'autore prima di toccarlo, e nove su ventidue sono stati lasciati
come sono, con il numero.

Come si è misurato:

- La versione finale di prova (`prova.ps1 -Release`, copia dei dati dell'autore con quattro account) e le righe "tempi: avvio",
  con righe in più dove un tratto era grosso. `strumenti\avvio.ps1` avvia N volte e stampa per riga mediana, minimo,
  massimo e durata del tratto; con `-Compare` alterna due eseguibili (prima, dopo, prima, dopo).
- **Il PC era a batteria per tutta la notte** (dal 49 al 20%, schema "Bilanciato"). A batteria tutto costa di più: il
  codice di `de77f2b`, che il 5 ottobre dava il primo disegno a 766-790 ms, qui lo dà a 822 (786-1046), e nel minuto dopo
  una compilazione a 967-1167. Per questo due varianti si confrontano solo ad avvii alternati e per tratto (la differenza fra
  una riga e la precedente, che fra un avvio e l'altro cambia di pochi ms). I numeri assoluti di questa sezione non si
  confrontano con quelli del 5 ottobre.
- Un profilo a campioni del thread dell'interfaccia (`dotnet-trace`, senza diritti di amministratore, avviando l'eseguibile
  di prova) per sapere che cosa c'è dentro ogni tratto, e `MISSIVA_ASSEMBLY=1` per gli assembly caricati con thread e istante.
- Per le decine di avvii dei confronti, `MISSIVA_SENZA_RETE=1`: nessun collegamento (ogni avvio in rete apre una decina di
  connessioni a Gmail). Le tabelle finali sono in rete. Sulla casella vera solo letture.
- Per la rete in ritardo: i controlli con il server IMAP finto e gestori finti, e la versione di prova avviata con
  `--senza-rete` o `--errore-rete 10054` e pilotata con `MISSIVA_KEYS` ("rete tornata", "rete tornata in silenzio",
  "stato account"), che dopo il comando si collega davvero agli account.

### Tempi prima e dopo

Avvio a caldo con la finestra, in rete, 6 avvii alternati per variante, ms dall'avvio del processo: mediana (minimo-massimo).

| Riga | Prima (`de77f2b`) | Dopo |
|---|---|---|
| Main | 94 (88-131) | 96 (86-104) |
| tema Fluent | 284 (269-388) | 288 (270-321) |
| impostazioni lette | 332 (308-444) | 158 (144-171), lette intanto da un altro thread |
| XAML della finestra | 602 (562-767) | 497 (486-552) |
| finestra mostrata | 770 (713-960) | 654 (643-718) |
| **primo disegno** | **822 (786-1046)** | **692 (657-774)** |
| Posta in arrivo scelta | 867 (827-1100) | 735 (699-817) |
| **elenco nel disegno** | **978 (913-1218)** | **814 (799-901)** |
| timer di Posponi, invii pianificati, promemoria, copia dei 30 giorni | 2031-2195 (tre avvii a parte; 3486 con il PC occupato) | 738 (704-822) |
| icona nell'area di notifica | prima del primo disegno | 858 (831-939) |
| gestore delle notifiche registrato | prima della finestra | 885 (859-968) |
| WebView2 pronto | non scritto | 1634 (1327-2043) |

Altri avvii:

| Che cosa | Prima | Dopo |
|---|---|---|
| Primo avvio dopo una compilazione (un avvio per numero): primo disegno, elenco | 1175 e 1379; 971 e 1114 | 907 e 1038 |
| Nascosta (`--nascosta`), in rete, 3 avvii: icona | subito dopo "Posta in arrivo scelta", a 674-844 | 569-727 |
| Nascosta: Posta in arrivo scelta (l'elenco della copia locale pronto) | 674-844 | 610-779 |
| Nascosta: finestra disposta senza mostrarla (`LayOutHidden`, non toccata) | 117-125 | 89-112 |
| Riapertura dall'area di notifica ("apri" da una seconda copia): `Show`, nel disegno | 113-133, 119-143 | 119-124, 127-133 |
| Dopo la riapertura: WebView2 comincia a prepararsi, è pronto | non scritto | a +185-195 ms, a +544-550 |
| Primo messaggio dopo la riapertura (65 KB dalla copia su disco, scelto con UI Automation uno o due secondi dopo): testo pronto, pagina disegnata | 117-137, 140-280 (senza rete, PC più lento) | 71-100, 91-111 |

La riapertura non è cambiata (nessuna correzione la tocca). Una foto della finestra di prova (`foto.ps1`, PrintWindow) 4,5 s
dopo l'avvio, prima e dopo: nessun pixel diverso su 67.650 campionati. Il momento esatto del primo disegno non è
fotografato: PrintWindow non è lo schermo, e l'animazione di apertura di Windows (circa 250 ms) copre quei momenti.

### Dove va il tempo prima del primo disegno

Un avvio profilato del codice di prima con le righe in più (primo disegno a 835 ms), thread dell'interfaccia:

| Tratto | ms | Che cosa c'è dentro |
|---|---|---|
| dal processo a `Main` | 79-96 | l'avvio di .NET dal file unico: non è codice di Missiva |
| `Main` | 16-24 | 9 di `Process.GetCurrentProcess().SessionId`, che elenca tutti i processi del PC |
| costruttore di `App` | 170 | 39 il Dispatcher, 14 `Application`, 115 il tema Fluent (97 leggere il suo BAML) |
| `Store.Load` | 34-49 | 14 preparare i tipi del JSON, 12 leggerlo, 4-5 i due testi cifrati |
| gestore delle notifiche | 29-45 | la prima chiamata al Toolkit registra Missiva presso Windows: attivatore COM, registro, icona estratta dall'eseguibile e salvata su disco |
| costruttore di `MainWindow` | 195 | 84 le nove caselle del pannello dei filtri della ricerca create con la finestra (di cui 37 sono la prima inizializzazione del disegno di WPF, che qualcuno paga comunque), 102 `InitializeComponent` |
| `Show()` | 161 | 16 la finestra di Windows, 49 la prima misura, 8 la disposizione, 18 la cornice, 42 i messaggi di Windows (19 il fuoco) |
| icona nell'area di notifica | 14-20 | finestra nascosta, icona estratta, `Shell_NotifyIcon` |
| attesa di `ContentRendered` | 2-53 | il thread è fermo: WPF lo manda a priorità Input, che con il mouse sopra la finestra aspetta un timer |

Tre quarti del tempo sono WPF e .NET (tema, XAML, prima misura, avvio del runtime). Il codice di Missiva che si poteva
spostare valeva circa 110 ms, ed è quello spostato.

### Gruppo 1: percorso fino al primo disegno

- **A-01** (impostazioni lette dopo il tema). CONFERMATO SU WINDOWS: 34-49 ms sul thread dell'interfaccia. CORRETTO
  (`90d2ffc`): `App.LoadSettingsEarly` le legge su un altro thread da `Program.Main`, il costruttore di `App` le aspetta
  (arrivano prima: attesa 0 ms). Tratto da 40 a 0 ms. Il token di Microsoft e la connessione di Gmail partono a 162 ms
  invece che a 314-324, prima del tema. Controllo: impostazioni con un modello cifrato da 150 KB lette da un altro thread,
  uguali (20-22 ms tutta la lettura, in Debug).
- **A-02** (`ThemeMode` due volte). (a) NON CONFERMATO: la seconda assegnazione costa 4-5 ms con "Come Windows", con Scuro e
  con Chiaro a Windows scuro (4-9). Con A-01 il tema si assegna una volta sola: quei 5 ms tolti. (b) CONFERMATO: dei 262 ms
  fra "rubrica e temporanei" e "XAML della finestra" l'XAML è 91-113; il resto sono notifiche, caselle dei filtri e
  costruttore della finestra (tabella sopra).
- **A-03** (firme e modelli decifrati all'avvio). NON CONFERMATO sui dati dell'autore: 2 testi in 3,8-5,1 ms. Con A-01 la lettura
  non sta più sul thread dell'interfaccia. Non corretto.
- **A-04** (statici dei loghi e domini noti). NON CONFERMATO: gli statici meno di 1 ms; `domini.dat` (184 domini, 27 KB) 4-5
  ms, e ora si legge sul thread delle impostazioni. Non corretto. Resta la riga "tempi: loghi: domini noti letti".
- **A-05** (lavori di `OnStartup` prima del primo disegno). CONFERMATO per tre su sei, CORRETTO (`90d2ffc`):
  - gestore delle notifiche, 29-45 ms: con la finestra si registra dopo l'elenco (`App.ListenForToasts`, priorità
    Background); avviata da Windows per una notifica (`-ToastActivated`) o nascosta, subito come prima. Il registro confronta
    la riga di comando con `WasCurrentProcessToastActivated`: mai diversi in 48 avvii.
  - icona dell'area di notifica, 13-20 ms: con la finestra dopo l'elenco, insieme al gestore (un blocco di 43 ms a elenco
    disegnato); chiudendo la finestra prima, la mette `HideToTray`.
  - `Process.GetCurrentProcess().SessionId`, 9 ms: `ProcessIdToSessionId`. Tratto "Main" da 16 a 7 ms. Controllo: stessa sessione.
  - Lasciati: `CleanTemp` con rubrica e manutenzione 2 ms in tutto; `SystemEvents` 4-5; avvio automatico e canale mailto 3-4.
- **A-06** (secondo giro di layout). NON CONFERMATO: 5 giri prima del primo disegno, ma `ApplySettings` 2 ms, `ShowFolders`
  0 ms, e il layout che li segue 6 ms. Non corretto.
- **A-07** (elementi dell'XAML creati subito). NON CONFERMATO come guadagno. `InitializeComponent` 91-113 ms senza un
  elemento sopra i 4 ms nel profilo. Provato a creare le nove caselle dei filtri alla prima apertura del pannello: il
  profilo diceva 84 ms, il confronto alternato 3-9 ms netti (il resto è inizializzazione di WPF che passa all'XAML).
  Tolto. La bisezione dell'XAML per gruppi non è stata fatta.
- **A-08** (copia locale di tutti gli account prima della Posta in arrivo). NON CONFERMATO: 10-15 ms per quattro account,
  22-37 per la prima pagina. Non corretto.
- **A-09** (dalla finestra nascosta al primo messaggio). CONFERMATO l'ordine, numeri nella tabella. Non corretto: preparare
  WebView2 prima del disegno toglie circa 0,2 s solo a chi apre un messaggio entro mezzo secondo dalla riapertura, e mette
  i circa 46 ms di `Prepare` davanti a ogni riapertura.
- **A-10** (righe "tempi: avvio" scritte una per una). CONFERMATO: 0,48 ms a riga (58 righe 27,7 ms). CORRETTO: restano in
  memoria fino all'elenco nel disegno (`Log.StartupFlush`); `MISSIVA_REGISTRO_SUBITO=1` per il confronto.
- **A-12** (MSAL e MailKit sul thread dell'interfaccia). NON CONFERMATO: MSAL si carica su un thread del pool; MailKit sul
  thread dell'interfaccia dentro un tratto di 3 ms. Non corretto.
- **A-21** (regex di `BodyCache`). 2,9-3,1 ms (4,3-4,7 avviata nascosta), una volta. Non corretto.
- **A-22** (riscaldamento senza `SpacerName` e `CssEscapedFunction`). CONFERMATO: 10,7-15,9 e 1,8-2,7 ms la prima volta, 38
  ms se è la prima regex senza backtracking del processo. CORRETTO: `MailHtml.WarmUpSample`. Controllo sul messaggio finto.

### Gruppo 2: rete in ritardo all'accesso a Windows (`ea12c3a`)

- **A-13** (timer dopo le cartelle). CONFERMATO SU WINDOWS: con la rete buona partivano a 2031-2195 ms (elenco a 815-933),
  cioè dopo le cartelle di tutti gli account. CORRETTO: `StartAsync` non aspetta più `LoadAccountsAsync`; ora a 738 ms. Il
  caso della rete appesa (60-120 s) non è stato riprodotto. Nessun controllo automatico: serve una finestra.
- **A-15** (stato che non si ripulisce). CONFERMATO nel codice, CORRETTO, provato dal vivo:
  - avvio senza rete, a 12 s la rete torna senza che Windows lo dica: gli account con IDLE tornano a posto a 17 s, al loro
    tentativo ("di nuovo raggiungibile (IDLE collegato)"), quello di Outlook a 31 s con la query delta. Prima: al controllo
    di ogni due minuti.
  - avvio con `--errore-rete 10054`: "Non raggiungibile: Connessione in corso interrotta..." sotto i quattro account, e
    `KnownOffline` falso. A 11 s "rete tornata": a 29 s stato vuoto e cartelle rilette per tutti. Prima quello stato restava
    fino al riavvio, e il cambio della rete non faceva nulla per loro.
  - `Network.IsOffline` non è stato allargato a 10054, 10061 e tempi scaduti: sono errori che può dare anche il server.
- **A-16** (IDLE che non si sveglia). CONFERMATO, CORRETTO: `InboxIdle.RetryNow`. Dal vivo: a 26 s "la rete è tornata,
  riprovo subito invece che fra 20 s", IDLE in sessione un secondo dopo. Controllo: senza segnale aspetta, con il segnale
  riprova in 30 ms.
- **A-17** (tentativi unici). CONFERMATO nel codice, CORRETTO: promemoria del calendario a 1 o 5 minuti dopo una lettura
  non riuscita (dal vivo la riga "non in linea, si riprova fra un minuto"); contatti di Outlook dopo un controllo riuscito;
  Posponi con una riga per account e motivo, e nessuna richiesta con l'account non in linea.
- **A-18** (copia dei 30 giorni). (b) CORRETTO: dopo un errore di nuovo fra 5 minuti. (a), il primo giro a un minuto
  dall'accesso: NON VERIFICATO (serve una traccia dell'accesso a Windows), non toccato.
- **A-19** (lucchetti dei token). Token di Google: CONFERMATO e CORRETTO, l'errore di rete vale 10 s per chi è in fila
  (controllo: 4 richieste in fila in 2,0 s prima, una richiesta e 0,5 s dopo). Cambio della rete durante un controllo:
  CORRETTO (`checkAfterThis`). Che cosa risponde l'accesso di Microsoft senza rete (barra "Accedi di nuovo" falsa):
  NON VERIFICATO, non toccato.

### Gruppo 3: avvio nascosto e manutenzione

- **A-14** (a) icona dopo il lavoro sincrono: CONFERMATO, 124 ms dopo "finestra creata". CORRETTO: prima di `StartAsync`, 58
  ms dopo. (b) Posta in arrivo riletta a finestra nascosta: lasciata. È quello che tiene giusto l'elenco alla prima
  apertura, e i due difetti del 7 ottobre erano proprio elenchi non aggiornati. (c) `LayOutHidden`, 87-125 ms a
  interfaccia ferma: lasciato.
- **A-20** (manutenzione a +20 e +30 s). NON CONFERMATO come costo: i 52 `.json` della copia (0,6 MB) si leggono in 15 ms,
  10 leggendo solo il primo byte; 845 corpi (62 MB) elencati su un thread del pool. Corretto solo l'elenco degli account
  di `TrimAll`, che si leggeva 30 s dopo da un altro thread.

### Gruppo 4: il pacchetto (A-11)

CONFERMATO SU WINDOWS il contenuto: file unico di 266,7 MB, con dentro un composito ReadyToRun (`Missiva.r2r.dll`) di
155,6 MB. NON CONFERMATO che il pacchetto conti nell'avvio a caldo. Nessuna modifica a `Missiva.csproj` né a `installa.ps1`.

- La proiezione WinRT (`Microsoft.Windows.SDK.NET.dll`) non si carica prima del primo disegno: nell'elenco degli assembly
  caricati all'avvio non c'è, nemmeno quando il gestore delle notifiche si registrava prima della finestra.
- Due varianti pubblicate in cartelle di prova (`prova-pacchetto`), 5 avvii alternati con il file unico di oggi, senza rete:

| Variante | Misura | Primo disegno a caldo | Elenco nel disegno | Primo avvio dell'eseguibile |
|---|---|---|---|---|
| file unico di oggi | 266,7 MB, composito 155,6 | 645 (601-714) | 778 (750-852) | vedi sotto |
| proiezione WinRT fuori dal composito | 256,9 MB, composito 115,5, la proiezione compilata a parte 55,3 | 672 (622-705) | 805 (744-830) | 673, dieci minuti dopo la compilazione |
| file unico di oggi (secondo confronto) | | 683 (631-908) | 810 (757-1086) | |
| cartella di exe e DLL, ReadyToRun per assembly | 280 file, 236,2 MB | 830 (752-886) | 990 (906-1053) | 1293, dieci minuti dopo la compilazione |

- Fuori dal composito la proiezione non fa guadagnare nulla a caldo, e il file scende di 9,8 MB, non di 30: con
  `PublishReadyToRunCompositeExclusions` l'SDK la compila comunque, a parte (anche mettendo insieme
  `PublishReadyToRunExclude`). La variante con la proiezione lasciata in solo IL (30 MB in meno) non è stata ottenuta:
  NON VERIFICATA. Il suo costo sarebbe la compilazione alla prima notifica.
- La cartella è più lenta: circa 150 ms in più a caldo su ogni tratto (senza il composito ogni assembly si carica e si
  collega da sé) e 1293 ms al primo avvio. Non c'è una scelta da proporre: il file unico resta meglio.
- Il primo avvio dopo una compilazione è lento (971-1175 contro 822) soprattutto per lo stato del PC appena dopo due minuti
  di compilazione a batteria: un eseguibile nuovo mai avviato, dieci minuti dopo essere stato scritto, è partito a 673 ms
  come a caldo. Quanto pesi il file da leggere dal disco freddo resta da misurare (sotto, "Non verificato").

### Trovato in più

- PresentMon non vede il primo fotogramma di WPF: segna il primo `Present` 20-30 ms dopo "elenco nel disegno" anche tenendo
  ferma Missiva 1,5 s fra la cornice e l'elenco, mentre sullo schermo la cornice c'è. Per l'avvio non serve.
- L'attesa di `ContentRendered` (2-53 ms a thread fermo prima dell'elenco) è stata lasciata: senza, l'elenco arriva circa
  150 ms prima ma la finestra compare circa 110 ms dopo, con l'elenco già dentro.
- Il firewall blocca l'eseguibile di Debug (10013 vero) e lascia passare quelli in `prova-release` e `prova-release-prima`.

### Controlli e suite

- `StartupAuditTests`, 10 controlli (da soli con `--prova-audit risultato.txt avvio`): A-01, A-05, A-22, A-16, A-15 (due),
  A-17 (due), A-18, A-19. Quello di A-19 fa girare anche il comportamento di prima (memoria dell'errore a zero): 4 richieste
  in 2045 ms contro una in 516.
- Senza controllo automatico, perché serve una finestra: A-13, la parte di A-15 che sta in `MainWindow`, lo spostamento di
  icona e gestore delle notifiche. Provati dal vivo, come scritto sopra.
- Suite completa: 857 controlli, tutto bene, due volte (dopo il gruppo 2 e prima dell'ultimo commit). "ricerca che nessuno
  aspetta più" non è caduta.

### Che cosa cambia per chi usa Missiva

- La finestra compare circa 0,13 s prima e l'elenco circa 0,16 s prima (a batteria: 822 e 978 ms, ora 692 e 814). Stessa
  finestra, stesso elenco.
- L'icona nell'area di notifica compare un attimo dopo l'elenco invece che prima della finestra.
- Con la rete in ritardo all'accesso a Windows: i ritorni da Posponi, gli invii pianificati e i promemoria non aspettano
  più le cartelle; "Non in linea" e "Non raggiungibile" si tolgono da soli appena un server risponde, e le cartelle si
  rileggono; i nuovi messaggi tornano ad arrivare subito appena la rete c'è.
- Il registro non si riempie più di errori ripetuti quando manca la rete.

### Scelte fatte qui, da rivedere

- Il gestore delle notifiche si registra dopo l'elenco. Un clic su una notifica rimasta da prima, fatto nel primo secondo
  dopo aver aperto Missiva a mano, apre Missiva senza eseguire l'azione: prima valeva per circa 0,35 s, ora per circa 0,9.
- Posponi: con l'account non in linea il ritorno non si prova nemmeno; riparte quando l'account risponde.
- I tempi di riprova (1 e 5 minuti per i promemoria, 5 per la copia dei 30 giorni, 10 s di memoria dell'errore del token,
  30 minuti fra due tentativi dei contatti di Outlook) sono scelti, non misurati.

### Non verificato

- **L'avvio a freddo.** Senza diritti di amministratore la cache dei file di Windows non si svuota, e il PC non è stato
  riavviato. Misurato solo il primo avvio dopo una compilazione (tabella). Per misurarlo, da una PowerShell normale:
  1. `pwsh -File strumenti\prova.ps1 -Release -Build`, poi `pwsh -File strumenti\prova.ps1 -Stop`.
  2. Riavviare il PC, accedere, aspettare due minuti.
  3. `pwsh -File strumenti\avvio.ps1 -Runs 4 -Raw`: "avvio 0" è quello a freddo, gli altri tre a caldo.
  4. Per l'avvio insieme a Windows: dopo un altro riavvio, appena compare il desktop,
     `pwsh -File strumenti\avvio.ps1 -Runs 1 -Raw -Arguments "--nascosta"`.
  Con il PC alimentato, e scrivendo se lo era. Con i diritti di amministratore si evita il riavvio (`RAMMap64.exe -Et` prima
  di ogni avvio) e si vede dove va il tempo (`wpr -start GeneralProfile -start FileIO`, piano A.7 del rapporto).
- L'avvio vero con Windows (voce in Run, rete che arriva dopo): mai provato, la prova non scrive la chiave.
- Un clic vero su una notifica dopo lo spostamento del gestore: non provato (serve una mano).
- La rete appesa (né assente né presente): solo con i gestori finti della suite.
- La versione finale compila ed è stata provata come versione di prova; non è stata installata.

## Audit del 7 ottobre, parte B, primo gruppo: scorrimento, animazioni, ingresso

Fatto l'8 ottobre 2026 sul rapporto `avvio-e-velocita.md`, sezione B.0 e punti B-01..B-10. "Prima" è il commit `8f1c2dc`.
Commit: `e980a50` (B-01..B-04), `56d46da` (B-08), `6e1da15` (B-10), `ac6334d` (B-05), più quello di questo esito (strumenti
di misura di B-06, B-07 e B-09). Il rapporto era scritto leggendo il codice su Linux: qui ogni punto è stato misurato sul
PC dell'autore prima di toccarlo. Su dieci punti, cinque corretti, uno corretto in parte, quattro lasciati con il numero.

Come si è misurato:

- La versione di prova in Debug (`prova.ps1`, copia dei dati, `MISSIVA_NO_READ`), con il firewall che le blocca la rete:
  l'elenco è quello della copia locale (47-54 righe, più le righe finte di "aggiungi N"). Tutti i tempi sono di Debug.
- **Tasti e clic veri.** `strumenti\ingresso.ps1` manda con SendInput tacche della rotella, tasti (F15, che non fa nulla,
  e freccia giù) e clic sinistri alla finestra di prova, e scrive l'istante di ogni invio; `Core/InputProbe.cs` (solo Debug,
  con `MISSIVA_INGRESSO=1`) scrive l'istante in cui arrivano a `PreviewKeyDown` e `PreviewMouseDown` con lo stesso orologio
  (`QueryPerformanceCounter`), e dalle operazioni del Dispatcher quanto aspettano in coda quelle a priorità Input o inferiore e
  quelle sopra il disegno. Niente gestore di `Rendering` nella sonda: cambierebbe ciò che misura. Alle 01:10 di notte
  SendInput arrivava (CLAUDE.md dice che di notte non arriva: questa notte la sessione non era bloccata). Prima di ogni invio lo
  script controlla che la finestra in primo piano sia della versione di prova e che sotto il cursore ci sia lei; alla fine
  rimette primo piano e cursore.
- Il PC è andato in sospensione da solo alle 01:55 ed è ripartito alle 10:01: da lì in poi nessun ingresso vero, solo
  comandi di `MISSIVA_KEYS`.

### La domanda di partenza: i tasti e i clic veri ritardano? (B-04)

**No.** Ritardano le operazioni a priorità Input o inferiore, fra cui i comandi di `MISSIVA_KEYS` con cui erano state prese
le misure dei blocchi 4 e 9.

| Che cosa | Da fermo | Durante lo scorrimento (30-40 tacche vere, una ogni 25 ms) | 30-900 ms dopo l'ultima tacca |
|---|---|---|---|
| Tasto vero: dall'invio a `PreviewKeyDown` | 2,3-12,5 ms | 1,3-3,7 ms | 1,6-2,9 ms |
| Clic vero: dall'invio a `PreviewMouseDown` | 6,2-9,6 ms | | 0,9-8,7 ms |
| Operazioni sopra il disegno nel secondo dopo (le riprese dopo un `await`: `Send SynchronizationContextAwaitTaskContinuation`) | al più 2,5-11 ms | al più 5-33 ms | al più 0,5-5 ms |
| Operazioni a priorità Input (`HitTestInvalidatedAsyncCallback`, `ContinueProcessingStagingArea`, timer a Input) | al più 25-80 ms | **fino a 513-601 ms** | al più 31-262 ms |
| Operazioni a priorità Background | al più 81-84 ms | fino a 865-1343 ms | al più 49-741 ms |

- Il mezzo secondo è il tetto di WPF descritto dal rapporto (un disegno ogni 500 ms scende a priorità Input: la sonda ne
  conta uno "a priorità abbassata" per ogni mezzo secondo di scorrimento). Vale per ciò che sta a Input o sotto, non per
  l'ingresso: WPF consegna tasti e clic dentro `Dispatcher.Invoke` a priorità Send, e dopo ogni `await` il lavoro riprende
  a Send, sopra il disegno.
- La ripresa dopo un `await`, misurata sull'apertura di un messaggio durante lo scorrimento (riga "HTML pulito in
  sottofondo, da A a B": lavoro finito a B, ripreso al numero dopo):

| Freccia giù durante lo scorrimento | Ripresa dopo il lavoro | "clic, testo pronto" |
|---|---|---|
| Vera (SendInput), 4 prove | 0-6 ms | 51-105 ms (messaggi da 6-29 KB) |
| Da `MISSIVA_KEYS` a priorità Input (com'era), 4 prove | 41-612 ms | 195-892 ms (51-222 KB) |
| Da `MISSIVA_KEYS` a priorità Send (ora), 4 prove | 2-7 ms | 54-89 ms (6-29 KB) |

- Un clic vero 180 e 400 ms dopo l'ultima tacca apre il messaggio in 62-80 ms; da fermo, sulla stessa riga, 40-65.
- **CORRETTO lo strumento** (`e980a50`): i comandi di `MISSIVA_KEYS` girano dentro `Dispatcher.Invoke` a Send, come
  l'ingresso vero, e il loro timer è a priorità Normal. Era a Input "come i tasti veri" (blocco 4), ed era sbagliato.
- Che cosa cambia nella lettura degli esiti di prima. Blocco 9, "Trovato in più": "clic e tasti possono arrivare con fino a
  mezzo secondo di ritardo" riguarda i comandi di prova e le riprese interne a Input, non la tastiera e il mouse. Il
  ritardo del messaggio successivo dopo Canc e Ctrl+Z (blocchi 4 e 9, 0,35-0,8 s) era misurato con `MISSIVA_KEYS`: con un
  tasto vero la ripresa non passa da Input. La correzione di allora resta buona per un altro motivo (toglie 79 fotogrammi
  di disegno inutile). Un Canc vero non è stato mandato.

### Punti del rapporto

- **B.0, sottoscrizioni a `CompositionTarget.Rendering`.** CONFERMATO l'elenco (grep sul codice di oggi): `WheelScrolling`,
  `StableScrollBar`, un fotogramma in `ShowFromTray`, uno all'avvio nella versione di prova, e gli strumenti di Debug.
  CONFERMATO SU WINDOWS il meccanismo delle priorità, con i numeri della tabella sopra. La raccomandazione (un gestore unico
  che si spegne al primo tasto o clic) NON È STATA SEGUITA: curerebbe un ritardo dell'ingresso che non c'è.
- **B-01** (il cursore della barra continua a far disegnare WPF). CONFERMATO IN PARTE, CORRETTO IN PARTE (`e980a50`).
  - Dopo un gesto vero della rotella (0,9 s su righe mai misurate) lo scorrimento finisce 75-85 ms dopo l'ultima tacca e
    il cursore si ferma a 68-241 ms: il disegno continua 74-241 ms, non un secondo.
  - Il secondo c'è senza gesto: una pagina di 40 righe aggiunta in fondo, 1184-1419 ms (141-170 fotogrammi); un cambio
    di cartella, 758-1314 ms (90-158 fotogrammi), con il cursore che cambia grandezza. In quel tempo le operazioni a
    Input o sotto aspettano fino a 0,4-1,1 s.
  - Corretto il cambio di cartella, scheda e filtro: il cursore va subito al suo posto finché l'elenco nuovo non viene
    scorso (`StableScrollBar.Replaced`). Dopo: nessun fotogramma.
  - Lasciata la pagina aggiunta in fondo (dopo: 1471 ms, uguale): toglierla farebbe saltare il cursore di mezza pista
    mentre si scorre, cioè quello che l'autore aveva chiesto di togliere, e l'ingresso vero non ne soffre. Lasciate anche la
    soglia di 0,0004 (è già mezzo pixel dello schermo) e la chiusura della coda a fine gesto (un salto a elenco fermo).
  - Il cursore non salta: "scorri 30" su righe nuove dopo un cambio di cartella, scarto grezzo 40 px, mostrato al più
    3,2; "scorri 120", grezzo 67, mostrato al più 11,5 (CLAUDE.md: non oltre 16).
- **B-02** (la ripresa del hit test aspetta un timer a priorità Input). CONFERMATO SU WINDOWS, CORRETTO (`e980a50`).
  - Prima: ripresa 61-153 ms dopo l'ultimo fotogramma veloce (undici gesti; nelle intenzioni 60-85), cioè 124-218 ms
    dopo l'ultima tacca. Un clic vero 30 e 100 ms dopo l'ultima tacca andava alla cornice dell'elenco (`ScrollViewer`) e si
    perdeva: a 100 ms l'elenco era già fermo.
  - Correzione: il timer a priorità Normal; e un pulsante del mouse premuto quando la lista non corre più (nessun
    fotogramma a 3 px o più da 25 ms) riprende il hit test prima che WPF tratti il messaggio
    (`ComponentDispatcher.ThreadFilterMessage`, poi `Mouse.Synchronize`): il clic va alla riga sotto il cursore.
  - Dopo: ripresa a 67-95 ms dall'ultimo fotogramma veloce (sei gesti; il resto è la grana del timer di Windows). Clic
    veri a 30 e 60 ms dall'ultima tacca: ancora alla cornice, l'elenco corre (scelta di prima, non toccata). A 100 (due
    volte), 140 e 180 ms: alla riga, messaggio aperto in 34-63 ms.
- **B-03** (due sottoscrizioni e nessuna regola "si spegne al primo input"). CONFERMATO come struttura, NON CORRETTO: vedi B.0.
- **B-04**: sopra.
- **B-05** (dopo un cambio di tema i riferimenti allo `ScrollViewer` di prima). CONFERMATO SU WINDOWS, CORRETTO (`ac6334d`).
  - Con `--theme-flip 9` e il comando nuovo "scorrimento stato": dopo un cambio lo `ScrollViewer` dell'elenco è un altro,
    la scala della barra torna 0..2178 (quella di serie), "sospensione prova" risponde "nessuna guardia sulla lista".
    Fino al riavvio: cursore che salta, mouse fermo che partecipa allo scorrimento, e la posizione tenuta alle pagine
    successive presa dallo `ScrollViewer` di prima. La rotella animata continua (è un gestore di classe).
  - Correzione: `MainWindow.AttachListScrolling` riattacca barra e guardia al cambio di tema e al primo `ScrollChanged`
    di uno `ScrollViewer` nuovo; `ListScroller` controlla di essere ancora dentro l'elenco.
  - Dopo, a ogni cambio (tre di fila): lo stesso `ScrollViewer` tenuto dalla finestra, scala 0..1, guardia presente,
    "sospensione prova" come all'avvio, cursore mostrato al più 11,5 px con scarto grezzo 67.
- **B-06** (WebView2 lavora a ogni layout). MISURATO, NON CORRETTO. Comando "costo webview": un passo di scorrimento con il
  suo layout, sei blocchi alternati da 100 passi, con il gestore di `LayoutUpdated` di WebView2 0,32-0,39 ms, senza
  0,16-0,22: 0,17 ms a fotogramma, su 2,3-4 ms di lavoro e 8,3 di fotogramma. Toglierlo vuol dire toccare un campo privato
  di WPF.
- **B-07** (ingresso delle righe nuove). MISURATO, NON CORRETTO. Comando "arrivo misurato", tre arrivi: 16-19 disegni in
  600 ms, 25-33 ms di lavoro in tutto, al più 3,6 ms l'uno, nessuno oltre 8. Un arrivo durante "scorri 60": 2-3
  fotogrammi oltre 16,7 ms, come senza (3 e 3). Le due allocazioni per fotogramma di `WheelScrolling` non sono state
  toccate: stanno nel codice del movimento.
- **B-08** (Oggi e Ieri restano quelli dell'inserimento). CONFERMATO, CORRETTO (`56d46da`).
  - Con il giorno finto (`MessageItem.TestToday`): dopo mezzanotte l'elenco restava "Oggi: 1,2 | Ieri: 3", e un messaggio
    arrivato dopo finiva con quelli di ieri ("Oggi: 6,1,2") senza che i gruppi si rifacessero. Anche la data della riga
    restava "18:00" invece di "mer 18:00".
  - Correzione: `MainWindow.CheckDay` confronta il giorno ogni minuto (timer di Posponi), alla riapertura dall'area di
    notifica e quando la finestra torna attiva; se è cambiato, `MessageRows.NewDay` ricalcola gruppi, date e scadenze, e
    l'elenco si raggruppa tenendo la selezione e la prima riga che si vede.
  - Nella finestra, con "giorno N" di `MISSIVA_KEYS`: gruppi rifatti subito, dal timer entro il minuto (34 s), e a
    finestra nascosta prima di riaprirla. La prima riga visibile resta la stessa (prima di tenerla ferma si spostava di
    sette righe). Costo: un blocco di 54-122 ms, una volta al giorno.
- **B-09** (raggruppamento rifatto due volte). NON CONFERMATO come costo, NON CORRETTO. Comando "costo gruppi": togliere e
  rimettere il raggruppamento come fa `ApplySettings` costa 0,8-1,5 ms con 54 righe e 1,8-2,9 con 2.056; dentro
  `DeferRefresh` 1,1-1,7 e 1,8-3,3: uguale. Il costo vero è il layout dopo (7-19 ms), che è uno solo in tutti e due i
  modi. 2.000 righe aggiunte in 375 ms (0,19 ms l'una).
- **B-10** (il calendario torna alle 7:30). CONFERMATO SU WINDOWS, CORRETTO (`6e1da15`).
  - Con `--calendario --calendario-finto --calendario-lento` (gli account rispondono dopo 4, 6, 8 e 10 s): scorso a 510
    px, all'arrivo di ogni account le ore tornavano a 336 px (le 7:00).
  - Correzione: finché periodo e vista sono gli stessi, le ore toccate da chi guarda restano dove sono; se nessuno le ha
    toccate vanno dove dicono i dati (nel Giorno, il primo evento). "Oggi", le frecce e il cambio di vista ripartono dal
    mattino.
  - Dopo: scorso a 480 px, resta a 480 dopo due account; scorso a 120, resta a 120 dopo gli altri due.
  - La ricostruzione non è stata accorpata: 12-39 ms l'una (Settimana), 24 (Mese), in Debug, e arrivano in momenti diversi.

### Il movimento dello scorrimento è lo stesso

`WheelScrolling.Step`, `Retarget` e `Position` non sono toccati: in `WheelScrolling.cs` ci sono solo due segni della sonda
(`Conditional("DEBUG")`, nella versione installata non esistono). Misura: "rotella 6", "rotella 6 fine" e sette gesti veri
del 29 settembre rifatti con "replay", tre giri prima (`8f1c2dc`) e tre dopo, con `MISSIVA_RECORD=1`.

| Gesto | Prima: pixel, passi, passo medio, fermi | Dopo |
|---|---|---|
| rotella 6 | 350; 68-71; 4,9-5,1; 1. Passi regolari 3,7-5,7 px | 350; 71-75; 4,7-4,9; 1. Passi regolari 3,8-5,3 px |
| rotella 6 fine | 359; 53-56; 6,5-6,8; 0. Passi regolari 5,1-9,4 px | 359; 55-56; 6,5-6,6; 0. Passi regolari 4,9-9,1 px |
| 033456 (lento) | 305; 98-102; 3,0-3,1; 0 | 305-306; 98-104; 2,9-3,1; 0-1 |
| 033501 (lento) | 407-408; 85-86; 4,7-4,8; 0 | 407; 87-88; 4,6-4,7; 0 |
| 033504 (lento) | 472; 113-119; 4,0-4,2; 0-1 | 472-473; 119-121; 3,9-4,0; 0-1 |
| 033506 (a raffiche) | 501-512; 142-151; 3,7-3,8; 6-10 | 505-509; 145-147; 3,7; 6-7 |
| 033836 (flick, fino in fondo) | 2982-3009; 45-71; passo massimo 180-209 | 2982-3009; 51-64; passo massimo 189-217 |
| 033842 (flick) | 3009; 35-51; passo massimo 215-264 | 3009; 39-53; passo massimo 139-255 (due giri) |
| 033509 (flick) | 3009; 33-53; passo massimo 133-285 | 3008-3009; 37-50; passo massimo 184-255 |

Le differenze stanno dentro quelle fra un giro e l'altro dello stesso codice (i fotogrammi persi dal disegno cambiano il
numero dei passi, non i pixel).

### Trovato in più

- **Una cartella nuova si apriva già scorsa.** CORRETTO (`e980a50`). Con le righe della copia locale messe subito dopo aver
  tolto quelle di prima, lo `ScrollViewer` teneva la posizione dell'elenco di prima: da 1500 px, la Posta in arrivo di un
  altro account si apriva a 852 (visto in una foto: la prima riga era a metà elenco). Ora un'altra cartella o vista,
  un'altra scheda, un altro filtro e la ricerca tolta partono dall'alto (`ReloadAsync(fromTop: true)`); le riletture
  della stessa cartella (dopo un contrassegno, una bozza salvata) restano dove sono.
- Una riga di soli numeri in `MISSIVA_KEYS` apriva "Missiva ha avuto un errore imprevisto" (il numero letto come tasto):
  corretto, nei soli strumenti.
- Nella vista Giorno aperta da riga di comando il calendario si ricostruisce 10 volte invece di 5 (117 ms in tutto): non
  guardato.

### Controlli e suite

- `RunningAuditTests`, un controllo (da solo con `--prova-audit risultato.txt aperto`): B-08, con il giorno finto; scrive
  anche com'era prima.
- Senza controllo automatico, perché serve una finestra: B-01, B-02, B-04, B-05, B-10. Misurati nella finestra come
  scritto sopra, con strumenti che restano: `strumenti\ingresso.ps1`, `MISSIVA_INGRESSO=1`, e in `MISSIVA_KEYS`
  "scorrimento stato", "giorno N [subito]", "calendario stato", "calendario scorri N", "arrivo misurato", "costo gruppi",
  "costo webview", "punto elenco"; `--calendario-lento`.
- Suite completa: 858 controlli. Primo giro: caduto solo "ricerca che nessuno aspetta più" (quello che ogni tanto cade
  per i tempi: 6 SEARCH invece di 5); secondo giro, stesso codice: tutto bene.
- La configurazione Release compila (`dotnet build -c Release`); la versione finale di prova non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Un clic su un messaggio appena l'elenco si ferma lo apre: prima, per uno o due decimi di secondo dopo l'ultimo
  movimento della rotella, veniva ignorato.
- Dopo mezzanotte "Oggi" e "Ieri" sono giusti senza riavviare, anche riaprendo Missiva dall'area di notifica.
- Il calendario non torna al mattino mentre arrivano gli eventi degli altri account.
- Dopo un cambio di tema, anche automatico, la barra di scorrimento resta senza scatti e il mouse fermo resta fuori dallo
  scorrimento. Prima si perdevano fino al riavvio.
- Un'altra cartella si apre dall'inizio, e il cursore della barra è subito al suo posto.
- Lo scorrimento con la rotella e con il touchpad si muove come prima.

### Scelte fatte qui, da rivedere

- 25 ms senza un fotogramma veloce bastano perché un clic vada alla riga (`ScrollHitGuard.SlowMs`): scelto, non provato da
  L'autore. Mentre l'elenco corre il clic si perde come prima.
- Per una pagina aggiunta in fondo il cursore della barra continua a spostarsi piano per 1,2-1,5 s.
- Al cambio di giorno l'elenco si raggruppa da capo (54-122 ms a interfaccia ferma, una volta al giorno).
- "Oggi" nel calendario riporta le ore al mattino anche se la settimana è la stessa.
- I comandi di `MISSIVA_KEYS` ora girano a Send: i tempi "clic, testo pronto" presi durante un'animazione sono più corti
  di quelli scritti negli esiti di prima, e sono quelli veri.

### Non verificato

- **Il touchpad vero.** Tutto l'ingresso della misura è SendInput e rotella a tacche: come si sente lo scorrimento con le
  dita lo può dire solo l'autore. I passi per fotogramma sono gli stessi, ma è una misura, non una prova a mano.
- Il clic dopo lo scorrimento con il touchpad (l'inerzia di Windows manda eventi per circa un secondo dopo aver alzato le
  dita: finché l'elenco corre il clic si perde ancora) e con un mouse in mano.
- La versione finale: tutte le misure sono di Debug. Niente è stato installato.
- La mezzanotte vera e la ripresa del PC il giorno dopo: solo con il giorno finto.
- Il cambio di tema fatto da Windows: solo con `--theme-flip`, che passa dallo stesso gestore.
- Il calendario con gli account veri: solo eventi finti.
- B-09, i punti dal 4 al 6 del rapporto (Contrassegnati per scadenza con molte righe, conversazioni di centinaia di
  messaggi, pannelli annidati dei gruppi): non misurati.
- B-06 e B-07 nella versione finale, e un profilo di `WebView2.OnWindowPositionChanged`.
- Un Canc o un Ctrl+Z veri durante lo scorrimento.

## Audit del 7 ottobre, parte B, secondo gruppo: dati, rete, memoria

Fatto l'8 ottobre 2026 sul rapporto `avvio-e-velocita.md`, punti B-11..B-34 e sezioni B.5 e B.6. "Prima" è il commit
`2748d36`. Commit: `23c6014` (B-13), `1e22494` (B-11), `81e3eac` (B-14, con le misure di B-12 e B-15), `a7bfab1` (B-17, con
le misure di B-16 e B-18), `3c899b0` (B-19), `f2df7e7` (B-22), `f31d385` (B-26, B-27, B-30, con la misura di B-23), più
quello di questo esito. Il rapporto era scritto leggendo il codice su Linux: qui ogni punto è stato misurato o provato sul
PC dell'autore prima di toccarlo. Su ventiquattro punti: sei corretti (B-11, B-13, B-17, B-26, B-27, B-30), due corretti in
parte (B-14, B-19), otto lasciati con il numero (B-12, B-15, B-16, B-18, B-22, B-23, B-25, B-28), sei non misurati (B-21,
B-29, B-31, B-32, B-33, B-34), due da non toccare per il rapporto stesso (B-20, B-24).

Come si è misurato:

- Controlli e misure in `DataAuditTests` (`Core/ProvaBozze.Audit.cs`, da soli con `--prova-audit risultato.txt dati`), in
  Debug, su cartelle temporanee e dati finti. Le righe "misura" non sono controlli: scrivono solo il numero. I tempi
  cambiano fra un giro e l'altro (il PC faceva altro): dove sono scritti due numeri sono il più basso e il più alto visti.
- La pulizia dell'HTML (B-16) sui corpi veri della copia di prova (`prova-dati`, 849 corpi), con
  `MISSIVA_MISURA_CORPI=cartella`: solo tempi e misure, nessun testo.
- Nessuna prova a finestra e nessun avvio della versione di prova: tutto quello che qui chiede una finestra è in "Non
  verificato".

### Punti del rapporto

- **B-13** (coda delle azioni senza rete: durante la ripetizione le azioni non sono su disco). CONFERMATO, CORRETTO
  (`23c6014`).
  - Controllo prima: sei spostamenti in coda, la ripetizione fermata con il terzo in viaggio: in `coda.dat` 0 azioni, e 0
    "in attesa" sotto l'account. Una Missiva chiusa o caduta lì perdeva le quattro non ancora ripetute, senza avviso.
  - Correzione: la ripetizione segna le azioni come prese (`ActionQueue.running`, in memoria) invece di toglierle. Un
    secondo ritorno della rete non le prende, Annulla non le toglie più (come prima: "già partito"). Ogni gruppo esce
    dalla coda e dal file quando è riuscito, non è più possibile o è stato rifiutato; se la rete manca ancora restano
    dov'erano. La scrittura del file va su un thread del pool (la ripetizione parte dal thread dell'interfaccia).
  - Dopo: a metà ripetizione nel file ci sono le quattro non finite, nell'ordine; riavviata, Missiva le ritrova e le
    ripete (quella in viaggio un'altra volta, le due già fatte no); due ritorni insieme, una volta sola; la rete che
    cade di nuovo, due fatte e quattro in attesa. I controlli della coda della suite (`OfflineQueueAsync`, da soli con
    `--prova-audit risultato.txt coda`) passano uguali.
  - Costo: 300 azioni ripetute una per messaggio, 300 scritture del file in 1,1-1,2 s in tutto (3,6-4 ms l'una, in
    sottofondo; Gmail e IMAP ne fanno una per gruppo di azioni uguali).
  - "Con Graph una per volta": LASCIATO. Mandarne quattro insieme cambia che cosa succede con un 429 (oggi l'azione e le
    seguenti aspettano, e la suite lo controlla): non è semplice e sicuro. Conto: 60-95 ms a richiesta (CLAUDE.md),
    cento azioni 6-10 s in sottofondo; ora che nulla si perde è solo attesa.
- **B-11** (`IsLast` quadratico sui `<br>` fratelli). CONFERMATO, e la causa più grossa era un'altra. CORRETTO (`1e22494`).
  - Misura prima (`HtmlToFlow.Blocks`, N righe "riga i`<br>`" in un `<div>`): 1.000 a capo 0,9-1,2 s, 4.000 a capo
    2,8-3,4 s, 40.000 non finiva (processo fermato dopo nove minuti).
  - Il grosso non è `NextSibling`: `HtmlToFlow.Text` chiedeva `paragraph.Inlines.Count` a ogni pezzo di testo, e WPF conta
    gli elementi uno a uno. Sonda: 8.000 righe aggiunte a un paragrafo, 56 ms; chiedendo `Count` ogni volta, 11,1 s. Il
    rapporto, scritto su Linux senza WPF, non poteva vederlo.
  - `NextSibling` da solo (quello del rapporto): 20.000 a capo 0,6-0,7 s, 40.000 a capo 2,1-2,5 s. In linea con i numeri
    presi su Linux.
  - Correzione: `FirstInline == null` al posto di `Count == 0`; l'ultimo figlio che conta si trova una volta per
    elemento in `Children`.
  - Dopo: 4.000 a capo 67-88 ms, 20.000 a capo 0,24-0,33 s, 40.000 a capo 0,5-0,6 s.
  - L'albero è lo stesso: confrontato con quello di prima (`HtmlToFlow.TestOld`) su 35 bozze scritte a mano e 400
    generate (1.391 a capo), nessuna diversa.
- **B-12** (bozza ripresa con corpo grande, tutto l'albero di WPF sul thread dell'interfaccia). CONFERMATO, LASCIATO.
  - Una newsletter citata di 200 KB (4.057 elementi): analisi 31-35 ms, albero di WPF 200-270, ingresso nel documento
    38-79, prima impaginazione di un `RichTextBox` fuori schermo 0,6-1,1 s. Di 1 MB (20.157 elementi): 0,16-0,18 s, 1,0-1,2
    s, 0,26-0,37 s e 2,8-3,5 s.
  - Portare l'analisi su un altro thread toglierebbe 31-35 ms su un secondo. Albero e impaginazione vanno fatti sul
    thread dell'interfaccia. Un tetto più basso per l'editor cambierebbe l'aspetto delle citazioni: non è una modifica
    semplice.
- **B-14** (rubrica e contatti salvati sotto il lucchetto dell'interfaccia). CONFERMATO, CORRETTO per la rubrica
  (`81e3eac`), LASCIATO per i contatti.
  - Misura: con 364 voci (la rubrica di oggi, 68 KB) JSON 0,3-0,4 ms, DPAPI 1,1-6,9, file con `Flush` 2,4-4,4: 4-12 ms
    sotto `Gate`, 5 secondi dopo ogni arrivo. Con 5.000 voci 21-30 ms, con 20.000 circa 70 (un giro: 430).
  - Correzione: sotto `Gate` resta solo il JSON; cifratura e file fuori, con le copie numerate (sul file non torna una più
    vecchia). `AddressBook.Save` torna ancora a scrittura finita: l'uscita e la copia di sicurezza non cambiano.
  - Controllo: una scrittura tenuta ferma sul disco, e intanto `LookAlike`, una voce nuova e `Suggest` rispondono in
    19-29 ms; il salvataggio dell'uscita aspetta quella in corso e porta sul file anche la voce arrivata intanto. Il
    controllo non è stato eseguito sul codice di prima (il gancio non c'era): lì tutta la scrittura stava sotto `Gate`.
  - `contatti.dat`: 10 ms con 200 contatti, 30-46 con 4.500, sul clic di "Aggiungi ai contatti" o in sottofondo dopo una
    lettura dei contatti (`SetRemote`, al più ogni 12 ore). Lasciato.
  - Nessun tetto alla rubrica: lasciato. 364 voci dopo due settimane, 190 byte a voce.
- **B-15** (copia locale degli elenchi scritta sul thread dell'interfaccia). CONFERMATO, LASCIATO.
  - Un elenco di 50 righe: 2,7-8,1 ms a scrittura (due per cartella aperta); 200 righe 17-20 ms; 40 cartelle 1,4-2,7 ms.
  - Un arrivo con la cartella chiusa (`MailCache.Prepend`: rilegge, decifra e riscrive): 14-26 ms, una volta per arrivo.
  - Sotto un fotogramma o poco più, e spostarle su un altro thread chiede di coordinare due scrittori dello stesso file.
- **B-16** (corpi in anticipo non puliti prima del clic; logo riletto dal disco). CONFERMATO, LASCIATO.
  - La pulizia che il clic aspetta, su 849 corpi veri (in media 51 KB): tema chiaro mediana 3,6 ms, 75° percentile 7,6,
    90° 14,7, 99° 32, massimo 76; tema scuro 3,7, 8,1, 16,2, 42 e 87. In Debug.
  - Farla prima toglierebbe 4 ms a un clic da 65-80 ms, 15 a uno su dieci, e terrebbe in memoria un HTML pulito (in media
    108 KB) per ognuno dei 20 corpi che il provider tiene, anche mai aperti: circa 2 MB.
  - Il logo: 0,09 ms a lettura (blocco 9). Lasciato.
- **B-17** (riconteggio dei non letti dopo scritture che non li cambiano). CONFERMATO, CORRETTO (`a7bfab1`).
  - Su Gmail e IMAP `SetFlagAsync` e `SetCategoryAsync` non passano più da `UnreadSync.WriteAsync`
    (`InboxIdle.OwnAsync(..., affectsUnread: false)`): l'eco all'IDLE resta ignorato, ma alla fine non si riconta.
  - Controllo sul server IMAP finto: dieci contrassegni e una categoria, 0 riconteggi chiesti (prima uno per scrittura,
    letto nel codice: `WriteAsync` alza `Written` a ogni fine); un segno di letto, 1.
  - Letto, sposta, elimina, copia e Posponi come prima. Il riconteggio dopo ogni messaggio letto (uno al secondo al
    massimo) resta: è la correzione del 7 ottobre al numero che restava alto.
- **B-18** (l'indice dei suggerimenti si rifà a ogni contatore cambiato). CONFERMATO, LASCIATO.
  - Con 364 voci e 1.000 contatti una lettera nel campo A costa 0,5 ms con l'indice pronto e 4,3-5,3 ms dopo un arrivo
    da un mittente già noto. Con 4.500 e 4.500: 3,4 e 43-46 ms.
- **B-19** (`Store.Save()` sui percorsi di clic). CONFERMATO, CORRETTO in parte (`3c899b0`).
  - Misura: un salvataggio subito 11,7-16,7 ms sul thread di chi chiama (0,3-0,4 l'istantanea, il resto il file con la
    copia di riserva); `SaveSoon` 0,9-1,9 ms la prima volta.
  - Passati a `SaveSoon` i nove punti dove il file stava fra il clic e il ridisegno: "Mostra immagini", Fissa, colore di
    una cartella, ordine delle cartelle (due), cartella nascosta, le scelte di Visualizza (`SaveAndApply`), vista del
    calendario e calendari nascosti.
  - Lasciati subito, oltre a quelli che il rapporto diceva di tenere: l'ordine degli account, il tema, i menu
    personalizzati e tutte le caselle delle Impostazioni (lì 12 ms a clic non si vedono, e quello che si spunta è sul
    disco).
  - Controllo: ognuna di quelle scelte, poi `SaveSoon` e subito `Flush` (l'uscita, la fine della sessione, la chiusura
    nell'area di notifica): tutte sul disco, una scrittura sola.
- **B-20**: non toccato, come dice il rapporto.
- **B-21** (profilo di WebView2 senza controllo di dimensione). NON VERIFICATO: i dati della Missiva installata non sono
  stati letti. Il profilo della copia di prova è 65 MB, ma è quello delle prove.
- **B-22** (registro: file aperto a ogni riga; `WithoutAddresses` quadratico). NON CONFERMATO il quadratico, LASCIATO
  (`f2df7e7`).
  - Sei forme di testo da 80.000 caratteri ammessi (senza "@", con "@" e senza dominio, a pezzi divisi da "@", con un
    dominio senza punto, di punti e lettere): 0,1-1,4 ms l'una, nessuna riga persa. La regex di .NET 10 non ripassa sui
    caratteri già visti.
  - Una riscrittura a mano in una passata dava gli stessi risultati su 3.031 testi ma era più lenta (1,1-2,9 ms): non
    tenuta. Resta un controllo che lo misura a ogni giro.
  - Scrittore in sottofondo: non serve. 0,48 ms a riga (misura della parte A), due o tre righe per messaggio aperto.
- **B-23** (`SummaryCache`). MISURATO, LASCIATO. Una voce pesa 6,7 KB di memoria gestita (300 messaggi con testo, HTML e
  un allegato sul server IMAP finto): 13 MB per un account che riempie le 2.000 voci, cioè che scorre quaranta pagine in
  una sessione. L'esito del blocco 4 diceva "qualche MB al massimo".
- **B-24**: già misurato dal rapporto, nessuna proposta. Non toccato.
- **B-25** (loghi senza tetto). CONTATO, LASCIATO. Nella copia di prova 64 loghi e 75 domini senza logo, 497 KB su disco:
  al più 2,3 MB di immagini decodificate se in una sessione si vedono tutti.
- **B-26** (contenitori che crescono). CONFERMATO in parte, CORRETTO (`f31d385`).
  - `App.notified`: una voce per messaggio annunciato, senza tetto. Ora gli ultimi 2.000 (`RecentKeys`).
  - `upcoming`: cresceva meno di quanto stimato. Gli eventi di oggi escono a ogni lettura; restavano per sempre solo
    quelli il cui promemoria non era ancora passato quando il PC si ferma (quelli della sera). In trenta giorni simulati
    con due eventi a sera: 82 tenuti prima, 26 ora, e i 58 promemoria della sera si mostrano ancora al risveglio.
  - `Templates.plain`: riparte oltre 50 voci. `domini.dat`: scritto con `SafeFile.Write`.
  - Lasciati: i domini noti e i file `.none` (75 in due settimane, vuoti), gli avatar delle notifiche (2 file, 8 KB, nella
    copia di prova), `missiva-errori.log` (si scrive solo se il registro non si scrive, una riga per tipo di errore), i
    file messi da parte (tenuti per scelta). Il caso `UIDVALIDITY` di `notified`: non provato.
- **B-27** (buste PEC trattenute). CONFERMATO nel codice, CORRETTO (`f31d385`): chiudendo nell'area di notifica le buste
  già aperte (fino a 8, 64 MB) si lasciano andare (`Pec.ForgetOpened`), e il registro dice quante. Senza controllo
  automatico e senza una PEC vera.
- **B-28** ("Esegui ora" senza avanzamento né Annulla). LASCIATO, non misurato. Conto dai numeri di CLAUDE.md: 0,24 s a
  corpo con Gmail, 0,1 con Outlook.com; 500 corpi non ancora sul PC, due minuti e uno.
- **B-29** (connessione SMTP nuova a ogni invio). LASCIATO, non misurato: servirebbe inviare.
- **B-30** (timer da 1 Hz nella Posta in uscita). CONFERMATO nel codice, CORRETTO (`f31d385`): `PutInOutbox` ferma il
  timer, `LeaveOutbox` e l'errore d'invio lo riprendono se c'era qualcosa da salvare. Senza controllo automatico.
- **B-31** (nessuna compattazione nell'area di notifica). NON VERIFICATO: serve una misura a 24-48 ore.
- **B-32** (micro-allocazioni), **B-33** (la copia dei 30 giorni occupa i posti dell'apertura di Graph), **B-34** (minori).
  LASCIATI, non misurati.
- **B.5** (timer a riposo). Con B-30 corretto non resta nessuna sveglia al secondo.
- **B.6** (contenitori che crescono). Con un tetto da oggi: `App.notified`, `upcoming`, `Templates.plain`, `Pec.opened`
  nell'area di notifica. Senza tetto, con il numero sopra: rubrica, loghi, domini noti. Non guardato: cartella `WebView2`.

### Trovato in più

- `Inlines.Count` di WPF costa quanto gli elementi del paragrafo: è la causa vera del blocco di B-11.
- Le azioni in coda di un account rimosso a metà della ripetizione, se la rete cadeva, tornavano in `coda.dat` e ci
  restavano per sempre. Corretto con B-13 (controllo: 3 azioni tornate prima, 0 ora).
- Sotto l'account "N azioni in attesa" ora scende mentre la coda va (`ActionQueue.Changed` a ogni gruppo finito).
- `--prova-audit risultato.txt coda`: i controlli della coda e della Posta in uscita della suite, da soli.

### Controlli e suite

- `DataAuditTests`, 12 controlli e 14 righe di misura: B-13 (5), B-11 (2), B-14, B-17, B-19, B-22, B-26.
- Suite completa (`suite-C.ps1`, sul codice di `f31d385`): 870 controlli, tutto bene al primo giro.
- La configurazione Release compila (`dotnet build -c Release`); la versione finale di prova non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Le azioni fatte senza rete non si perdono se Missiva si chiude o cade mentre le ripete: ripartono all'avvio dopo.
- Riprendere una bozza con migliaia di righe non ferma più la finestra per secondi o minuti.
- "Mostra immagini", Fissa, il colore e l'ordine delle cartelle, Visualizza e la vista del calendario rispondono senza
  aspettare il file delle impostazioni.
- Contrassegnare un messaggio o dargli una categoria non fa più contare i non letti al server.
- Lasciata aperta per giorni nell'area di notifica, Missiva tiene meno memoria: niente buste PEC già lette, niente
  eventi passati, un tetto ai messaggi annunciati.

### Scelte fatte qui, da rivedere

- Chiusa a metà della ripetizione, al riavvio l'azione che era in viaggio si ripete: un segno due volte non cambia
  nulla, uno spostamento già fatto esce con l'avviso "Azioni non più possibili" anche se era riuscito.
- Le preferenze passate a `SaveSoon` si perdono se il processo viene terminato entro 0,4-2 secondi dal clic.
- Copia in un'altra cartella fa ancora ricontare i non letti (una copia nella Posta in arrivo li cambia).
- Dopo la chiusura nell'area di notifica una busta PEC già letta si riscarica alla prossima apertura.
- Il tetto di 2.000 ai messaggi annunciati e di 50 ai testi dei modelli: scelti, non misurati.

### Non verificato

- **Niente a finestra.** Nessun avvio della versione di prova: la coda ripetuta con un account vero, la bozza grande
  nella finestra di scrittura, "Mostra immagini" e Fissa col mouse, la Posta in uscita, la chiusura nell'area di
  notifica con una PEC aperta.
- La versione finale: tutte le misure sono di Debug. Niente è stato installato.
- B-13 con Graph vero e con un server IMAP vero, e la chiusura vera di Missiva a metà (qui: il file letto a metà e
  riletto da una coda nuova nello stesso processo).
- B-12: l'impaginazione è di un `RichTextBox` fuori schermo, non della finestra di scrittura.
- B-14: il controllo sul codice di prima. B-17: il conteggio sul codice di prima, e Gmail (stesso codice di IMAP, non
  eseguito). B-16 e B-18 nella versione finale.
- B-19: la perdita dopo una terminazione forzata (è ragionamento), e il timer di `SaveSoon` sotto disegno continuo.
- B-21, B-28, B-29, B-31, B-32, B-33, B-34: non misurati. Per B-21, sul PC dell'autore:
  `Get-ChildItem $env:LOCALAPPDATA\Missiva\WebView2 -Recurse | Measure-Object Length -Sum`.
- B-23 con Gmail e con messaggi veri (strutture più grandi di quelle di prova).
- B-26: `UIDVALIDITY` cambiata con un UID già annunciato.
- B-27 e B-30: senza controllo automatico; il codice è di poche righe ed è stato solo compilato.

## Audit dell'11 ottobre, sicurezza, primo gruppo: copia di sicurezza (T-14..T-22)

L'11 ottobre 2026 due sessioni nel cloud hanno letto il commit `2084922` e scritto due rapporti: `docs/audit/sicurezza-2.md`,
il secondo audit di sicurezza (36 punti, T-01..T-36), e `docs/audit/codice.md`, l'audit del codice (71 punti, C-01..C-71).
Questa sezione e le seguenti li verificano a blocchi sul PC dell'autore: ogni punto riprodotto prima di toccarlo, poi corretto,
lasciato con il motivo o dato per falso.

Fatto l'11 ottobre 2026 sui punti T-14..T-22 di `sicurezza-2.md`, tutti sulla copia di sicurezza del blocco 7 (F-29).
"Prima" è il commit `2084922`. Commit: `8a4506c` (T-14), `6f94363` (T-15), `ebbcd09` (T-16), `56f5e04` (T-22), `ba52e04`
(T-18), `ce0e5a4` (T-19), `4cceda7` (T-21), `b6a12c2` (T-17), `11c8cf1` (la misura di T-20), più quello di questo esito.
Su nove punti: sette corretti (T-14, T-15, T-16, T-18, T-19, T-21, T-22), uno corretto in parte con due scelte lasciate
all'autore (T-17), uno confermato e lasciato (T-20). Il modello di minaccia è quello del rapporto: un file `.missiva-copia`
preparato da altri, di cui chi importa conosce la password.

Come si è misurato:

- **Prima**: una sonda temporanea, mai entrata nel repository, eseguita sul codice di `2084922` con `MISSIVA_DATA` in una
  cartella temporanea fatta apposta. Per T-14 la cartella dei dati di prova e le cartelle "vittima" stavano tutte sotto
  quella cartella temporanea, e ogni identificativo ostile era scritto in modo da cadere lì dentro (controllato prima di
  usarlo). Scriveva solo fatti: le righe "prima" qui sotto sono le sue.
- **Dopo**: `Backup2AuditTests` (`Core/ProvaBozze.Copia2.cs`, da soli in 20 secondi con `--prova-audit risultato.txt
  copia2`), 24 controlli e due righe di sola misura. Come quelli del blocco 7: cartelle temporanee loro, dati finti,
  `Store.TestEntropy` per fare da altro utente di Windows, due server IMAP finti su 127.0.0.1. Le copie ostili si scrivono
  a mano: il JSON del contenuto, compresso e sigillato con `BackupFile.Seal` (un file `.missiva-copia`) o cifrato con DPAPI
  (un `importazione.dat`). Per T-14 il controllo dei percorsi è stato eseguito anche con la correzione dei nomi spenta a
  mano: cade, restando dentro la cartella temporanea.
- Nessuna prova a finestra e nessun avvio della versione di prova: quello che chiede una finestra è in "Non verificato".

### Punti del rapporto

- **T-14** (l'identificativo di un account diventa un percorso, fino a una cancellazione ricorsiva). CONFERMATO, CORRETTO
  (`8a4506c`).
  - Prima: una copia con identificativo `..\x`, `C:\x`, vuoto o `..\..\..\..\Documents` era accettata da `Backup.Read`.
    `MailCache.Delete` (Rimuovi account) con `..\..\vittima`: la cartella vittima accanto ai dati cancellata con i suoi due
    file; con un percorso intero al posto dell'identificativo, lo stesso. Con `..` il percorso è tutta la cartella dei dati
    (calcolato, non eseguito).
  - Non serve nemmeno rimuovere l'account: `BodyCache.Trim`, che l'avvio fa per ogni account, toglieva i file `.bin` di
    `vittima\corpi`; `Calendars.Save` e `ContactBook.SetRemote` scrivevano `x.json` e `y.dat` in una cartella fuori dai
    dati.
  - Lo stesso identificativo da altre strade: un `impostazioni.json` scritto a mano si leggeva senza avvisi. Guardati e a
    posto: `coda.dat`, `token-google.dat`, rubrica e contatti usano l'identificativo solo per confrontarlo; cartelle e
    messaggi della copia locale hanno nomi fatti con SHA-256; i loghi prendono il dominio da `SenderLogos.DomainOf` (solo
    lettere, cifre, punto e trattino, nessuna parte vuota) e le chiavi di `domini.dat` non diventano mai un percorso; gli
    allegati temporanei stanno in cartelle con un nome casuale e passano da `SafeFileName`; gli avatar delle notifiche sono
    colore e iniziali in esadecimale.
  - Correzione dove nasce: `MailAccount.ValidId` (lettere e cifre ASCII, trattino, trattino basso, da 1 a 64, non un nome
    di periferica di Windows). `Backup.Read` rifiuta una copia con un identificativo che non lo è, o con lo stesso due
    volte, e dice perché; un `importazione.dat` così non si applica e va da parte; `Store.Read` tratta un
    `impostazioni.json` così come un file rovinato (da parte con la data nel nome, si riparte da `impostazioni.bak`).
  - Correzione dove fa danno: `Store.CacheName` dà alla copia locale il nome dell'account, che per un identificativo non
    valido è la sua impronta (un nome solo, sempre dentro `cache`); lo usano `MailCache`, `BodyCache`, `Calendars` e
    `ContactBook`. E chi cancella una cartella intera (`MailCache.Delete`, `BodyCache.Delete`, Svuota) controlla prima che
    il percorso risolto stia dentro `cache` (`Store.IsInside`).
  - Dopo, la stessa sonda: le quattro copie rifiutate, le vittime restano, nessun file fuori dai dati,
    l'`impostazioni.json` a mano dà l'avviso "un account ha un identificativo che Missiva non usa". Sei controlli: la
    regola su 35 identificativi; copie e importazioni in attesa; impostazioni a mano con e senza copia di riserva; sette
    identificativi ostili usati come cartella e come pezzo di nome di file (elenchi, corpi, calendario e contatti si
    scrivono e si rileggono dentro `cache`, Rimuovi account toglie solo quelli, le tre vittime e la cartella dei dati
    restano); un account normale ha sul disco i nomi di sempre; `IsInside` su tredici percorsi.
  - Non `^[0-9a-f]{32}$` come proponeva il rapporto: le prove della suite usano identificativi come `imap-prova`, e la
    regola larga chiude lo stesso i percorsi. Nella copia di prova dei dati dell'autore i quattro account hanno tutti 32 cifre
    esadecimali (contata solo la forma).
- **T-15** (la password IMAP di questo PC parte verso il server scritto nella copia). CONFERMATO, CORRETTO (`6f94363`).
  - Prima, con due server IMAP finti: copia senza password, stesso identificativo e indirizzo, un altro server. Nessun
    account chiedeva l'accesso, l'account aveva la password di questo PC e la porta della copia, e al primo collegamento
    il server della copia riceveva `password-di-questo-pc`. Con un'altra app e un'altra directory per Microsoft e un altro
    client per Google, `HomeAccountId` e token restavano.
  - Correzione (`Backup.Merge`): la password di questo PC resta solo con gli stessi host, porte, sicurezza e utente di IMAP
    e SMTP (`SameServer`); l'accesso a Microsoft solo con la stessa app e la stessa directory (`MicrosoftApp`), il token di
    Google solo con lo stesso client. Altrimenti l'account arriva senza accesso e lo chiede con la sua barra. I tre campi
    degli accessi non si prendono mai dalla copia, nemmeno se qualcuno ce li ha scritti.
  - Dopo, cinque controlli: il server della copia non riceve nulla (0 comandi) e l'account chiede Aggiorna password; sei
    cose cambiate una alla volta (host IMAP, host SMTP, porta, le due sicurezze, utente) tolgono la password, tutto uguale
    la tiene; con la password nella copia vale quella, verso il server della copia; Microsoft e Google; accessi scritti a
    mano nella copia che non entrano.
  - "Che ottenga dei token" con un'app estranea, SOSPETTO nel rapporto: non verificato, serve Microsoft vero.
- **T-16** (un `importazione.dat` costruito apposta chiude Missiva a ogni avvio). CONFERMATO, e c'era di peggio. CORRETTO
  (`ebbcd09`).
  - Prima, nove contenuti. Password di un account null, account IMAP con identificativo null, `Accounts: [null]`:
    `Backup.Read` e `Describe` passavano, `ApplyPending` sollevava `ArgumentNullException` o `NullReferenceException` e
    `importazione.dat` restava lì. Impostazioni null: `ArgumentNullException` già da `Backup.Read` (non una
    `BackupException`), e poi all'avvio. Elenco delle password null: `NullReferenceException` in `Describe` e all'avvio.
    Una firma null: eccezione in `Describe`. Versione di Missiva null: eccezione nel testo della conferma. Una regola
    null nell'elenco passava dappertutto.
  - Il caso peggiore non era nel rapporto: con `ImageSenders: null` l'importazione riusciva, e poi era `Store.Load` a
    sollevare `ArgumentNullException` a ogni avvio. Senza più un file in attesa da togliere: per ripartire andava rimesso
    a mano il file messo da parte.
  - Correzione: `Backup.Parse` controlla il contenuto prima che qualcuno lo usi (`Validate`). Le proprietà scritte null
    valgono come assenti (`StripNulls`: resta il valore di base, nessun elenco null); una voce null in un elenco, un account
    senza identificativo o indirizzo, una password che non è di un account IMAP della copia fanno rifiutare la copia; i
    contatti si riscrivono come li scrive Missiva; ogni altra eccezione diventa "file che non si legge". `ApplyPending` non
    solleva più eccezioni: lettura, accessi e scrittura in memoria stanno nello stesso blocco, le impostazioni si rileggono
    come farà l'avvio prima di andare al loro posto, e qualunque cosa succeda il file in attesa va da parte e l'esito lo
    dice. `Describe` e l'apertura del file hanno il loro avviso.
  - Dopo, tre controlli. 26 casi scritti a mano: 15 non si applicano (file da parte, impostazioni di prima uguali byte per
    byte), 11 si applicano con i valori di base. Poi ogni valore di una copia (contenuto, impostazioni, contatti, rubrica)
    messo a null, cambiato di tipo o, negli elenchi, con una voce null, uno alla volta: 475 importazioni in 13-14 s, nessuna
    eccezione, nessun file in attesa rimasto, 250 rifiutate senza cambiare nulla e 225 applicate; per ognuna di queste
    l'avvio dopo legge le impostazioni e non c'è un null dove il tipo dice di no (cercati per riflessione; chi li cerca
    prova prima di trovarli). Infine un file nuovo che non si riesce a scrivere: l'importazione si ferma prima di toccare
    quelli di adesso.
- **T-17** (la conferma mostra indirizzi e numeri; passano regole, mittenti delle immagini e rubrica). CONFERMATO in
  parte, CORRETTO in parte (`b6a12c2`), due scelte lasciate all'autore.
  - Prima: il testo della conferma di una copia con un altro server, due regole che eliminano e immagini per tutti diceva
    "1 account (...), 2 regole di Missiva, e le impostazioni". Le due regole arrivavano accese, una senza condizioni.
  - (b) come scritto è FALSO: `"com"` cade alla prima lettura (`UpgradeImageSenders`: `ImageKey` di un nome senza punto è
    vuoto). Ma `"co.uk"` e un servizio intero come `"gmail.com"` entravano, e le immagini di chiunque scrivesse da lì si
    caricavano da sole: CONFERMATO con quei valori.
  - Corretto, la conferma (`BackupSummary`, `Backup.Describe` con le impostazioni di questo PC, `BackupUi.ConfirmText`).
    Per prima cosa "Da guardare prima di importare": le regole accese che valgono per tutti i messaggi, e gli account che
    su questo PC ci sono già ma nella copia hanno altri server (quelli di qui, quelli della copia, e che la password di
    questo PC non ci va), un'altra app o directory di Microsoft, un altro client di Google. Poi ogni account con il tipo e,
    per IMAP, server, porte e utente (e "notifiche spente"); le regole in parole (`Rules.Describe`) con il loro account,
    prima quelle per tutti i messaggi e quelle che eliminano, le spente dette, dodici righe e poi "e altre N"; i mittenti
    di cui mostrare sempre le immagini. Ogni testo della copia va su una riga sola, accorciato e senza caratteri che non
    si vedono o che girano il testo (`SenderCheck.CleanName`): non può fingere altre righe.
  - Corretto, i valori chiaramente ostili: all'importazione non entrano fra i mittenti delle immagini un servizio di posta
    personale intero e un suffisso nazionale da solo (`Settings.DropBroadImageSenders`: l'interfaccia non li scrive mai).
    L'avviso dopo il riavvio dice quante voci non sono entrate, e che la copia aveva regole accese.
  - (a) LASCIATO: importare le regole spente. Cambia ogni importazione vera (su un PC nuovo le regole andrebbero riaccese
    una per una): è una scelta, vedi "Scelte per l'autore". Ora le regole si leggono in parole prima di dire sì.
  - (c) CONFERMATO, LASCIATO: con una rubrica dai conteggi scritti da altri il primo suggerimento per "mario" è
    l'indirizzo di chi ha preparato la copia, e un messaggio di "Mario Rossi" da lì non accende la fascia dei sosia (riga
    di misura nella suite). Se la rubrica debba entrare, e come, è una scelta.
  - (d) le notifiche spente per un account sono dette sulla sua riga; l'avviso di allegato dimenticato spento no. (e) app e
    directory si vedono sulla riga dell'account di lavoro e negli avvisi; su un PC nuovo non c'è nulla con cui
    confrontarle.
  - Dopo, quattro controlli: la conferma della copia ostile riga per riga; i mittenti delle immagini dopo l'importazione
    e l'avviso; la conferma di una copia normale, senza "Da guardare"; app, directory, client, venti regole in dodici
    righe e testi con a capo, cambi di direzione e 5.000 caratteri.
- **T-18** (`domini.dat` importato salta la regola dei 14 giorni dei loghi). CONFERMATO, CORRETTO (`ba52e04`).
  - Prima: l'importazione scriveva il `domini.dat` della copia, con un dominio dato per noto da gennaio e autenticato.
  - Azzerare solo `Verified` non bastava: il dominio è di chi ha preparato la copia, e il suo primo messaggio supera DMARC.
    Ora la copia non contiene i domini noti e l'importazione non tocca quelli del PC; quelli scritti in una copia fatta
    prima si ignorano. Ogni PC li ricostruisce dalle date che i server danno ai messaggi (`SenderLogos.Note`).
  - Dopo, un controllo: copia nuova senza domini noti, `domini.dat` del PC uguale byte per byte dopo un'importazione che
    ne porta altri, su un PC nuovo non nasce. Due controlli del blocco 7 dicevano che i domini noti passavano: ora dicono
    che non passano.
- **T-19** (un file da 1 MB si espande a 1 GB; iterazioni fino a 20 milioni). CONFERMATO, CORRETTO (`ce0e5a4`).
  - Prima, misurato qui con contenuti più piccoli (il PC aveva 1,8 GB liberi): un file di 49 KB con dentro 50 MB rifiutato
    in 0,22 s con 566 MB di memoria chiesti in tutto; 99 KB con dentro 100 MB, 0,45 s e 1.132 MB. Fino al tetto di 1 GB
    cresce in proporzione. Password sbagliata con 600.000 iterazioni nell'intestazione: 77 ms; con 5 milioni 0,63 s; con
    20 milioni, il massimo accettato, 2,6 s (il rapporto, su Linux, 13-17 s).
  - Correzione: tetto di 64 MB al contenuto in chiaro (`Backup.MaxPlain`), in due passate: la prima conta soltanto e si
    ferma appena lo supera, la seconda riempie un vettore della misura giusta. Lo stesso tetto all'esportazione: dati più
    grandi non si scrivono, con il motivo. Iterazioni accettate fino a 5 milioni.
  - Non il rapporto massimo di 50 a 1 proposto: una copia vera con testi molto ripetuti lo supera (il contenuto di prova,
    4 MB di una sola lettera dentro una firma, sta in 5 KB), e non si importerebbe.
  - Dopo, tre controlli: 64 KB che si espandono a 65 MB rifiutati in 0,1 s con 157 KB chiesti; una firma da 4 MB va e
    torna, dati oltre il tetto non si esportano; 5.000.001 iterazioni o 20 milioni rifiutati senza derivare la chiave.
  - `DialogResult` su una finestra già chiusa, SOSPETTO nel rapporto: la finestra della password ora controlla se è stata
    chiusa durante l'attesa. Letto nel codice e compilato, non provato a finestra.
- **T-20** (file messi da parte con i token, mai tolti). CONFERMATO, LASCIATO (`11c8cf1`, solo la misura).
  - Dopo un'importazione sopra due account che la copia non ha, nei due file delle impostazioni messi da parte (il `.json`
    e il `.bak`) la password IMAP e il token di Google si decifrano ancora: quattro accessi.
  - Tenerli com'erano è una scelta del blocco 7 ("I file messi da parte non si cancellano mai", ripresa in B-26): servono a
    tornare indietro, e senza accessi tornare indietro vuol dire rientrare in ogni account. Restano cifrati per lo stesso
    utente di Windows, come `impostazioni.bak` dopo ogni salvataggio. Vedi "Scelte per l'autore".
- **T-21** (modelli con testi illeggibili: il segnaposto diventa l'oggetto). CONFERMATO, e anche il testo. CORRETTO
  (`4cceda7`).
  - Prima: un modello cifrato per un altro utente di Windows dava come oggetto "(testo cifrato non leggibile su questo PC,
    rif. ...)". Il rapporto dice che il corpo resta vuoto: qui no, `BlocksFromRtf` prende quel testo come testo semplice e
    lo inserisce (un blocco).
  - Correzione: `Templates.Subject` e `Templates.Blocks` danno vuoto per le parti che non si leggono, la ricerca nel
    pannello non le guarda, e `ComposeWindow.UseTemplate` lo dice con un avviso. Un controllo.
  - LASCIATO: spegnere quei modelli nel pannello Modelli. Restano nell'elenco con il loro nome, o con il testo che dice
    che non si leggono.
- **T-22** (un valore "dpapi:..." nella copia diventa testo vero). CONFERMATO, più grave di come detto. CORRETTO
  (`56f5e04`), non come proposto.
  - Prima: i testi singoli restavano com'erano (`MoveTo: "dpapi:AQAAAN..."`), ma gli elenchi scritti come un testo solo
    passavano da DPAPI di questo PC. Con un cifrato di questo utente messo nella copia, dopo l'importazione le categorie
    di una regola e le ricerche recenti erano il segreto in chiaro (`segreto-di-questo-pc`). Chi ha visto un
    `impostazioni.json` di chi importa ha quei cifrati: la password IMAP, il token di Google.
  - Correzione: in `Portable` nemmeno `ProtectedListConverter` tocca DPAPI; il valore resta il testo che è.
  - Non "copia rovinata" per ogni valore che comincia con "dpapi:", come proponeva il rapporto: un nome di modello o una
    ricerca possono cominciare davvero così, e passano uguali (nel controllo).

### Trovato in più

- Impostazioni importate che l'avvio non riusciva a leggere (`ImageSenders: null`): Missiva non partiva più, senza un
  file in attesa da togliere. Corretto con T-16.
- La pulizia dei corpi all'avvio (`BodyCache.Trim`) cancellava file `.bin` fuori dai dati senza che nessuno rimuovesse
  l'account. Corretto con T-14.
- Gli elenchi di una copia decifrati con DPAPI di questo PC (T-22): il rapporto lo dava per un difetto di coerenza.
- Il modello illeggibile metteva il segnaposto anche nel testo del messaggio (T-21).
- `"Settings": null` in una copia usciva da `Backup.Read` come `ArgumentNullException`: nella finestra della password
  sarebbe stato un errore imprevisto dell'interfaccia. Corretto con T-16.

### Controlli e suite

- `Backup2AuditTests`, 24 controlli: T-14 (6), T-15 (5), T-16 (3), T-17 (4), T-22, T-18, T-19 (3), T-21. Due righe di
  sola misura: la rubrica di T-17 e i file messi da parte di T-20.
- I 18 controlli del blocco 7 (`--prova-audit risultato.txt copia`) passano; due sono cambiati con T-18.
- Suite completa: 894 controlli, tutto bene, sul codice di `11c8cf1` (il commit dopo ha solo i documenti). Un giro intero prima, sul codice di `8a4506c`: 876, tutto bene.
- Compilazione Debug: 0 errori, nessun avviso nuovo (i 13 di prima, tutti su righe non toccate qui). La configurazione Release compila (`dotnet build -c Release`); la versione finale di prova non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- La conferma dell'importazione è più lunga: sotto "Contiene" ci sono gli account con i loro server, le regole in parole
  e i mittenti delle immagini, e sopra, quando serve, "Da guardare prima di importare". Se è lunga si scorre.
- Una copia con dentro qualcosa che Missiva non scrive si rifiuta, con il motivo: "Il contenuto della copia non è quello
  che Missiva scrive (...)".
- Rimettendo una copia sullo stesso PC, un account IMAP a cui nel frattempo sono stati cambiati server, porte o utente
  chiede di nuovo la password; lo stesso per Microsoft e Google con un'altra app o un altro client.
- Dopo il riavvio l'avviso dice anche che la copia aveva regole accese, e quante voci delle immagini non sono entrate.
- La copia non porta più i domini noti dei loghi: su un PC nuovo i loghi tornano man mano che Missiva rilegge la posta.
- Dati oltre 64 MB non si esportano, e lo si dice.
- Un modello che su questo PC non si decifra non inserisce nulla e apre un avviso.
- Per gli account di sempre nulla cambia sul disco: la copia locale ha gli stessi nomi.

### Scelte fatte qui, da rivedere

- **La regola dell'identificativo** è larga (lettere, cifre, trattino, trattino basso, fino a 64), non le 32 cifre
  esadecimali che Missiva genera.
- **Un `impostazioni.json` con un identificativo non valido è un file rovinato**: va da parte e si riparte dalla copia di
  riserva. L'alternativa era togliere solo quell'account.
- **Null**: una proprietà scritta null vale come assente e la copia entra; una voce null in un elenco fa rifiutare la
  copia. I contatti di una copia si riscrivono come li scrive questa Missiva: un campo che questa versione non conosce
  non passa.
- **64 MB** e nessun rapporto massimo di compressione; **5 milioni** di iterazioni.
- **I domini noti fuori dalla copia**, invece di importarli senza il segno "autenticato".
- **"dpapi:..." in una copia resta testo**, non fa rifiutare la copia.
- **Gli avvisi confrontano con questo PC** solo gli account con lo stesso identificativo e lo stesso indirizzo. Una copia
  che cambia anche l'identificativo non riceve nessun accesso di questo PC, e non ha avvisi.
- **Dodici righe di regole** nella conferma, poi "e altre N".
- **Le voci larghe delle immagini si tolgono solo all'importazione**, non a ogni avvio: un `impostazioni.json` scritto a
  mano con `"co.uk"` vale ancora.
- **L'avviso del modello illeggibile** è una finestra in più.
- Una copia rimessa sullo stesso PC non tocca `domini.dat`: resta quello che il PC ha.

### Scelte per l'autore

- **Regole di una copia** (T-17): devono arrivare spente, da riaccendere a mano, invece che accese con l'avviso?
  Proposta: no, restano accese; ora si leggono in parole prima di importare, e quelle per tutti i messaggi sono in cima.
- **Rubrica dei destinatari** (T-17): deve entrare nella copia? Proposta: sì, ma con i conteggi dei messaggi scritti
  azzerati, così un nome noto da un indirizzo nuovo accende ancora la fascia dei sosia; i suggerimenti si rifanno con
  l'uso.
- **File messi da parte** (T-20): devono perdere gli accessi degli account, o serve un pulsante "Elimina i dati di prima"
  nell'avviso dopo l'importazione? Proposta: il pulsante, che lascia la scelta a chi importa.
- **Modelli illeggibili** (T-21): vanno nascosti dal pannello Modelli della scrittura? Proposta: sì, restano solo in
  Impostazioni, Posta, Modelli, dove si eliminano.
- **Mittenti delle immagini** (T-17): togliere le voci per interi servizi anche a ogni avvio, non solo all'importazione?
  Proposta: sì, se in `impostazioni.json` dell'autore non ce ne sono (non guardato: sono i dati veri).

### Non verificato

- **Niente a finestra.** La conferma lunga nella finestra vera (va a capo? scorre oltre 420 DIP?), l'avviso del modello
  illeggibile, la finestra della password chiusa durante l'attesa, l'avviso di `Describe`, l'avviso dopo il riavvio con
  le righe nuove. Passi per l'autore, dopo l'installazione: Impostazioni, Generali, Copia di sicurezza, Esporta; poi Importa
  lo stesso file. La conferma deve mostrare "Account:" con i server, le regole in parole e i mittenti delle immagini,
  senza "Da guardare"; dopo "Sostituisci tutto" e il riavvio gli account non chiedono nulla e l'avviso ricorda le regole
  accese.
- **La Missiva installata e la versione finale** non sono state eseguite. I dati veri non sono stati letti: della copia
  di prova solo la forma degli identificativi.
- **T-14**: Rimuovi account dalla finestra (qui le stesse chiamate di `RemoveAccountAsync`, una per una); un
  identificativo che è un nome di periferica (`nul`) usato come percorso (la regola lo rifiuta, `IsInside` su quello non
  è provato: dipende dalla versione di Windows); collegamenti simbolici dentro `cache`.
- **T-15**: Microsoft e Google veri. Che con un'app estranea "Accedi di nuovo" mostri il nome di quell'app è di
  Microsoft; qui solo che l'accesso di questo PC non passa.
- **T-16**: `ApplyPending` che si ferma a metà dello spostamento dei file (un file in uso): scritto, non provocato. Il
  ramo "errore che Missiva non si aspettava" non ha un controllo: nessun contenuto provato ci arriva.
- **T-17**: una copia vera con decine di regole; la lettura delle righe con Assistente vocale.
- **T-18**: i loghi che tornano su un PC nuovo (dal codice: `Note` usa la data del server di ogni messaggio elencato). Una
  copia fatta davvero dalla versione installata oggi, che i domini noti li contiene: provata solo una copia scritta a mano
  con quel campo.
- **T-19**: una firma vera con un'immagine (il contenuto di prova è una lettera ripetuta); la memoria al tetto (un
  contenuto valido di 64 MB); il tempo dei 20 milioni di iterazioni su un PC più lento.
- **T-21**: `UseTemplate` nella finestra di scrittura (qui `Templates.Subject` e `Templates.Blocks`).
- **T-22**: se il segreto decifrato arrivasse davvero a un server (come categoria di una regola): ragionamento, non
  provato. Chiuso prima.
- Dove vadano i 13-14 s del controllo "ogni valore di una copia" (475 importazioni, 30 ms l'una): non misurato.

## Audit dell'11 ottobre, sicurezza, secondo gruppo: immagini, mittenti, allegati (T-01..T-10)

Fatto l'11 ottobre 2026 sui punti T-01..T-10 di `sicurezza-2.md`, la parte A ("le correzioni reggono?"). "Prima" è il
commit `d42cab4`. Commit: `9fb69c3` (T-01), `27ea8d3` (T-02), `2216c62` (T-03, T-04, T-05 e la misura di T-06), `d7560dc`
(T-07), `1382c57` (T-08), `66d7696` (T-09, T-10), più quello di questo esito. Su dieci punti: cinque corretti (T-01, T-02,
T-03, T-04, T-07), tre corretti in parte con il resto lasciato (T-05, T-08, T-10), uno corretto ma senza effetto su
questo Windows (T-09), uno confermato e lasciato (T-06).

Come si è misurato:

- **Il motore vero, senza finestra.** Su questo PC Edge non c'è più: resta il Runtime di WebView2 (154.0.4258.62), quindi
  niente "Edge senza finestra" come nei blocchi 1 e 10. La prova nuova (`Sec2Engine` in `Core/ProvaBozze.Sicurezza2.cs`)
  crea un WebView2 figlio di `HWND_MESSAGE` (nessuna finestra), con i dati in `WebView2-prove` dentro la cartella dei dati
  di prova, lo stesso confino del riquadro (`ReadingView.Confine`) e la stessa regola per le richieste
  (`ReadingView.FateOf`): le due funzioni sono state tolte dai metodi della finestra apposta, e il riquadro usa quelle.
  La pagina è quella vera di `ReadingPage.Build`, servita da `https://missiva.example/`. Ogni richiesta che il motore fa
  passa da `WebResourceRequested` e si registra con quello che il riquadro ne farebbe; quelle che lascerebbe andare
  partono davvero solo verso un ascoltatore su 127.0.0.1, e `--host-resolver-rules` non lascia risolvere nessun nome.
- **Il controllo che la prova veda.** Nella stessa conversazione il messaggio del mittente consentito ha un'immagine
  scritta nel modo di sempre per ogni strada (`img`, sfondo di una cella, `style`, immagine di un disegno SVG): devono
  arrivare tutte e quattro all'ascoltatore, altrimenti il motore senza finestra non sta provando quella strada.
- **Prima**: gli stessi controlli lanciati sul codice di prima, con la sola estrazione di `Confine` e `FateOf`. Le righe
  "prima" qui sotto sono le loro. Per gli altri punti ogni controllo è stato visto cadere prima della correzione (T-02,
  T-03, T-04, T-05, T-07, T-08 per il riquadro, T-09, T-10).
- **Le forme a migliaia**: 82.824 indirizzi fatti di quello che il motore toglie o ignora davanti (14 modi), uno schema
  (29), le barre (17), il resto (12), letti dal motore con `new URL(forma, document.baseURI)` dentro la pagina; un
  campione di 2.239 anche come attributo `src` di un'immagine vera.
- **Uscita uguale e tempi**: il banco del blocco 1 copiato in una cartella temporanea, con `MailHtml.cs` di `d42cab4`
  accanto a quello nuovo.
- Nessuna finestra e nessun avvio della versione di prova. I dati veri e la copia di prova dei dati dell'autore non sono stati
  letti.

### Punti del rapporto

- **T-01** (immagini esterne scritte come le legge Chromium). CONFERMATO nel motore vero, e c'era di più. CORRETTO
  (`9fb69c3`).
  - Prima, conversazione con un messaggio espanso di un mittente consentito e uno di un mittente non consentito con 53
    forme: 31 partivano (richieste di tipo immagine lasciate andare dal riquadro; quelle in http arrivate all'ascoltatore
    con il loro percorso, quelle in https come connessioni che non cominciano con una richiesta HTTP).
  - Le forme del rapporto, come le legge il motore: `http:\\host`, `http:/host`, `http:host` e la tabulazione nello
    schema sono `http://host`; `/\host` e `\/host` sono `https://host`; `https:\\host` è `https://host`. Partivano da
    `img`, dallo sfondo di una cella e di una tabella, da `<image>` scritto al posto di `<img>`, dentro `foreignObject`.
  - Una cosa diversa dal rapporto: `\\host` su Windows il motore lo legge come `file://host` e non lo chiede nemmeno (sul
    Linux del rapporto partiva). E `https:host`, con lo schema della pagina e senza barre, resta sulla pagina.
  - Forme che il rapporto non aveva. Nel CSS una `url(` mai chiusa, anche con l'indirizzo di sempre:
    `style="background-image:url(http://host/a.png"` senza la parentesi si caricava, perché per il CSS la fine
    dell'attributo o del foglio chiude da sola parentesi e virgolette; lo stesso `image-set(` mai chiusa. Il rapporto
    dava `url()` nel CSS per a posto: lo è solo quando è chiusa. Negli attributi dei disegni SVG, che il motore legge
    come CSS: `mask`, `cursor` e `fill` con `url(http:\\host)`, `url( 'http:/host')`, `image-set('http://host' 1x)`,
    `u\72l(http://host)` (anche `fill` verso un file esterno si chiede come immagine). Nelle animazioni SVG: `set` su
    `href` con `to` scritto come sopra, `animate` con `values="#a;http://host/a.png"`, dove passava anche l'indirizzo di
    sempre perché non sta in testa al valore.
  - Non partivano nemmeno prima, 22 forme su 53: `\\host`; quelle già riconosciute (`//host`, anche con un a capo
    davanti, e negli attributi dei disegni e nelle animazioni l'indirizzo di sempre in testa al valore); le `url()` chiuse
    negli stili e lo sfondo di `body` (lì tutto quello che non si riconosce diventa `none`); `href` di `image`, `feImage`
    e `use` (lista bianca); `srcset`; `file://`; `src('...')` e `image('...')`, che il motore non carica; `filter` con il
    nome scritto `u\72l(`; `set` su `xlink:href` e su `fill`; uno spazio unificatore davanti (per il motore è un percorso
    della pagina).
  - Da solo, lo stesso messaggio non fa partire nulla, prima e dopo: lo ferma la CSP della pagina, come dice il rapporto.
  - Correzione, la regola. `MailHtml.KindOf` legge un indirizzo come il motore dalla pagina del riquadro: via spazi e
    caratteri di controllo ai bordi, via tabulazioni e a capo ovunque; lo schema è lettere, cifre, `+`, `-`, `.` fino ai
    due punti; `\` vale `/`; dopo `http:` le barre non servono; `https:` senza due barre, come un indirizzo senza
    schema, resta sulla pagina. Dice pagina, `#id`, sito esterno, `data:`, `cid:`, altro schema. Lo usano `src`,
    `background` e `poster`, i riferimenti dei disegni e ogni altro attributo. Non è una lista di forme, e non è
    `Uri.TryCreate` come proponeva il rapporto: .NET non legge gli indirizzi come Chromium.
  - Correzione, il resto. Uno schema che non è né un sito né un'immagine dell'email (`file:`, `blob:`, `javascript:`,
    un percorso di Windows) si toglie sempre. Nel CSS, dopo la pulizia, ogni `url(` rimasta deve essere di quelle tenute
    (`data:`, o servita da Missiva) e non resta nessun `image-set(`: le altre diventano `missiva-bloccato(`
    (`WithoutUnclosedLoads`). Negli attributi dei disegni resta solo `url(#id)` e `url(data:)`, e si toglie l'attributo
    che ha una parentesi insieme a una barra rovesciata o a `image-set(` (`LoadsFromCss`). Le animazioni di `href` si
    tolgono per intero, anche al mittente consentito (`AnimatesAddress`); `values` con dentro un indirizzo esterno si
    toglie. Quello che si toglie si conta fra le immagini bloccate: prima no, e non compariva nemmeno "Mostra immagini".
  - Dopo, nel motore: delle 53 forme nessuna parte, le quattro del mittente consentito arrivano all'ascoltatore.
  - Dopo, a migliaia: delle 82.824 forme il motore ne porta 6.399 fuori dalla pagina, e `KindOf` le dà tutte per esterne.
    Con la regola di prima ne sfuggivano 5.881. Altre 4.725 sono date per esterne e per il motore non lo sono
    (`\\host`, che su Windows è `file:`; `http://missiva.example`; indirizzi non validi): si bloccano per prudenza. Le
    2.239 lette come immagini della pagina danno lo stesso sito; passate dalla pulizia e rilette dal motore, nessuna
    immagine e nessuno sfondo porta fuori.
  - Uscita uguale a prima: 12 messaggi normali generati, 440 testi di prova presi dai sorgenti e 299 pagine HTML della
    documentazione di Git installata, 5.257 confronti (tema chiaro e scuro, immagini bloccate e consentite, testo),
    nessuno diverso. Su 2.976 varianti a caso 34 confronti diversi su 20.832, tutti dove il ritocco aveva scritto una
    `url(` mai chiusa, una funzione o una barra rovesciata in un attributo, o un altro schema. Dei messaggi patologici
    del blocco 1 cambiano due: "url(" ripetuto 10.000 volte (ora ogni `url(` è `missiva-bloccato(`) e l'attributo con
    `url(` e 30.000 spazi (ora tolto).
  - Tempi, vecchia e nuova alternate, mediana di 21 giri, due volte, tema chiaro: newsletter da 50 KB 2,2 e 2,4 ms, poi
    2,2 e 2,6; da 200 KB 8,4 e 9,5, poi 8,5 e 7,6; da 1 MB 63 e 58, poi 65 e 60. Sulla newsletter da 50 KB anche
    `ToText`, che non è cambiato, va da 1,2 a 1,35 e da 1,3 a 1,5: la differenza sta nel banco, non nella pulizia.
    Messaggi costruiti per far lavorare le regole nuove, nella suite: 250.000 `url(` mai chiuse (1 MB) 141-195 ms, un
    attributo con 150.000 `url(#a` 28-33 ms, 300.000 voci in `values` 43-51 ms, 1 MB di tabulazioni davanti a un
    indirizzo 22-27 ms, un `data:` di 2 MB con un a capo ogni 76 caratteri 49-67 ms.
  - Il dettaglio di `OnResourceRequested` (giudica con la pagina appena costruita, non con quella servita): CONFERMATO
    nel codice, LASCIATO. Le richieste in ritardo della pagina di prima sono già passate dalla sua CSP: se quella pagina
    non lasciava le immagini, il motore non le chiede (misurato: il messaggio da solo, zero richieste fuori dalla
    pagina anche con tutte le forme dentro). Nella finestra non si può provare qui.
  - Controlli: 11. Sei senza il motore (quattro sulla pulizia, uno sui tempi, uno sulla regola delle richieste), cinque
    nel motore (le immagini di controllo, la conversazione, le 82.824 forme, le 2.239 passate dalla pulizia, il messaggio
    da solo).
- **T-02** (le immagini dei mittenti consentiti si caricano con la fascia rossa, negli .eml e nelle PEC false).
  CONFERMATO, CORRETTO (`27ea8d3`), con una parte lasciata all'autore.
  - Prima, quattro controlli caduti: (a) mittente consentito con DMARC fallito secondo Gmail, fascia rossa e immagini
    caricate da sole; (b) busta che sembra posta certificata senza la firma di un gestore, e una firmata con un
    certificato per i siti web, con dentro il From di un mittente consentito: caricate; (d) "Mostra immagini" in quei
    casi ricordava il mittente: due domini in `ImageSenders`.
  - Logo e "Annulla iscrizione" in quei casi Missiva li toglie già: le immagini erano un buco nella stessa regola.
  - Correzione: `ReadingView.ImagesOnce` dice quando "Mostra immagini" vale solo per quella volta e non ricorda nessuno:
    Posta indesiderata (com'era, F-12), fascia rossa del mittente (`SenderCheck.Failed` sulle intestazioni del server
    della casella), fascia rossa della posta certificata o della firma (`Pec.Band`). `LoadsImages` e `RememberImages`
    decidono da lì; il suggerimento della fascia delle immagini bloccate dice il motivo.
  - (c) Un .eml allegato a un messaggio della Posta indesiderata si apriva come posta normale. Ora si apre come se ci
    stesse anche lui (`ReadingView.EmlJunk`, `EmlRequested` porta l'informazione): immagini, "Annulla iscrizione" e
    rubrica come lì. Gli .eml allegati a un messaggio normale restano com'erano: vedi "Scelte per l'autore".
  - `Pec.cs` non è cambiato (`ImagesFrom` resta il From di postacert.eml): la regola sta in un posto solo, e vale anche
    per i messaggi firmati che non sono posta certificata.
  - Controlli: 5.
- **T-03** (account IMAP: una Authentication-Results scritta dal mittente vale come quella del server). CONFERMATO sul
  server IMAP finto, CORRETTO (`2216c62`).
  - Prima: un messaggio con in cima `mail.studio-prova.example; dkim=pass ...; dmarc=pass header.from=...` (il dominio
    della casella) scritto da chi manda: il dominio del From diventava autenticato (`SenderLogos.IsAuthenticated`, il
    segno che va in `domini.dat` e dà il logo), e "Annulla iscrizione" era con un clic. Il server finto non scrive la sua
    intestazione e non toglie quelle che arrivano; quanti server veri facciano così non è stato misurato.
  - La regola dichiarata era "del server della propria casella, mai una aggiunta dal mittente": su IMAP non c'è modo di
    saperlo dalla sola intestazione. Ora `SenderCheck.Own` segna quel risultato `Unproven`: vale se dice che qualcosa non
    va (la fascia rossa resta), non autentica nessuno. Per i loghi un `Unproven` che direbbe "tutto bene" è come se non
    ci fosse: né prova né dubbio.
  - Non la seconda proposta del rapporto (accettarla solo sotto un `Received` del server): chi manda può scrivere anche
    quel `Received`, e nei server con più passaggi interni l'intestazione vera sta sotto altri `Received`. Per farlo bene
    serve provare server veri.
  - `Verified` che scade o cade al primo DMARC fallito: non fatto. Con `Unproven` la strada del rapporto è chiusa, e "il
    segno non scade" è una scelta del blocco 2 ancora da rivedere.
  - Due controlli del blocco 6 e uno del 4 ottobre dicevano il clic singolo su IMAP con il nome del server: ora dicono
    il messaggio da inviare.
- **T-04** (Gmail: si cerca la sua intestazione fra tutte). CONFERMATO nel codice, CORRETTO (`2216c62`), non come
  proposto.
  - Prima: con sopra l'intestazione di un altro server, quella con `mx.google.com` più sotto autenticava il mittente.
  - Ora quella di Gmail trovata non in cima è `Unproven`: se dice DMARC fallito la fascia rossa resta, se dice superato
    non prova nulla. Il rapporto proponeva di guardare solo la prima: se in qualche messaggio vero quella di Gmail non
    fosse la prima, si perderebbe la fascia rossa. Così no.
  - Che esistano messaggi di Gmail senza la sua intestazione resta SOSPETTO, come nel rapporto.
- **T-05** (Microsoft: `EndsWith` senza il punto, e authserv-id assente). CONFERMATO in parte, CORRETTO in parte
  (`2216c62`).
  - Il suffisso: CONFERMATO (`xoutlook.com`, `posta-outlook.com`, `finto-microsoft.com` passavano), CORRETTO: il nome
    deve essere `outlook.com` o `microsoft.com` o starci sotto (`MailAutoconfig.InDomain`).
  - "Un authserv-id assente non basta per il logo": LASCIATO. È la forma in cui Exchange la scrive (quella vera nei
    controlli del blocco 3 non ha nome): senza, i messaggi di Outlook scritti così non autenticherebbero più nessun
    dominio.
- **T-06** (allineamento a due etichette, non la lista dei suffissi pubblici). CONFERMATO, LASCIATO.
  - Misura nella suite: un messaggio da `cliente1.piattaforma-prova.example` firmato da
    `cliente2.piattaforma-prova.example`, senza DMARC, vale come autenticato per `piattaforma-prova.example`.
  - La proposta (senza DMARC solo il dominio del From o un suo genitore) toglie anche il caso più comune e onesto: la
    firma di `em.negozio-prova.example` per un From di `negozio-prova.example` oggi vale (stessa misura). È dentro la
    scelta "autenticato anche senza DMARC", in attesa dell'autore dal blocco 2.
- **T-07** (il disegno dei loghi fermato: se `ControlledExecution` non ferma il thread, nessuno lo sa). Il meccanismo
  CONFERMATO, CORRETTO (`d7560dc`). L'SVG che lo provochi non è stato cercato.
  - Prima, con un blocco `finally` al posto del codice nativo (né l'uno né l'altro si interrompono): `OnStaAsync` smetteva
    di aspettare dopo 2.010 ms (limite di prova 1 s), il thread restava a girare, nel registro non c'era nulla finché non
    finiva, e il disegno dopo partiva lo stesso.
  - Ora il disegno non fermato si conta finché il suo thread gira (`SenderLogos.DrawingSuspended`), con una riga nel
    registro: intanto non partono altri disegni e non si cercano altri loghi. Un logo non disegnato per questo non
    diventa "nessun logo" per 14 giorni; quello che si è piantato sì. Quando il thread finisce i loghi riprendono.
  - Non "niente più disegni nella sessione", come proponeva il rapporto: basta un disegno fermo alla volta.
- **T-08** (Posta indesiderata non riconosciuta). CONFERMATO in parte. CORRETTO il riquadro (`1382c57`), il resto
  misurato e lasciato.
  - Riquadro: quattro decisioni (copia su disco del corpo, dominio autenticato, rubrica, logo) guardavano la sola
    cartella. Ora chiedono `InJunk`, che conta anche l'etichetta di Gmail nei risultati di una ricerca. Il controllo
    legge il sorgente: quelle decisioni stanno nei metodi della finestra.
  - IMAP, CONFERMATO, LASCIATO: con una cartella segnata dal server (`\Junk`) e una seconda "Spam", la seconda è una
    cartella qualunque. Vedi "Scelte per l'autore".
  - Graph, TRASCURABILE: con la copia locale delle cartelle la Posta indesiderata si riconosce senza nemmeno chiederla
    (0 richieste); senza copia e con la richiesta non riuscita è una cartella qualunque solo fino alla lettura dopo.
    Serve la prima lettura di un account nuovo andata male.
  - `JunkOf` null "se manca la cartella": è il caso sopra, o un account che la cartella non l'ha.
- **T-09** (`SafeFileName`: nomi riservati incompleti). CONFERMATO che restavano com'erano, CORRETTO (`66d7696`).
  L'effetto su questo Windows: nessuno.
  - `COM0`, `LPT0`, quelli con la cifra in apice, `CONIN$`, `CONOUT$` e un nome riservato seguito da spazi (`CON .txt`)
    ora escono con un `_` davanti, anche dalla strada dei nomi normali (`IsPlainName`).
  - Misurato su questo PC (Windows 10.0.26300), aprendo in scrittura senza scriverci: `CONIN$`, `CONOUT$`, `COM¹`,
    `COM0`, `LPT0`, `CON .txt`, `NUL.txt` e perfino `CON` diventano file normali nella cartella; solo `NUL` da solo non
    crea un file, e quello era già riservato. Su un Windows più vecchio non è stato provato.
- **T-10** (le pagine HTML e SVG allegate si aprono senza domande). CONFERMATO. CORRETTO in parte (`66d7696`), il resto
  lasciato all'autore.
  - `.appcontent-ms`, `.udl` e `.diagpkg` ora chiedono conferma, come `.settingcontent-ms` e `.diagcab`. Che siano
    nell'elenco di Outlook non è stato controllato (niente rete): entrano per quello che fanno.
  - `.htm`, `.html`, `.shtml`, `.xhtml`, `.svg`, `.mht`, `.mhtml`: nessuna conferma, e Apri li passa al browser (riga di
    misura). Una conferma per le pagine web è una regola nuova: vedi "Scelte per l'autore".

### Trovato in più

- Una `url(` mai chiusa nel CSS, con l'indirizzo scritto nel modo di sempre, passava la pulizia senza essere contata:
  la forma più semplice di tutte. Corretto con T-01.
- Gli attributi dei disegni SVG si leggono come CSS e il motore chiede come immagine quello che c'è in `mask`, `cursor`
  e `fill`. Corretto con T-01.
- Le animazioni SVG cambiano `href` a un'immagine dopo la pulizia. Corretto con T-01.
- La pagina del riquadro si dava a chiunque la chiedesse, anche come immagine: un'immagine dell'email con l'indirizzo
  `#a` è la pagina stessa. Con un'animazione di 0,2 secondi, 37 richieste fra il caricamento e i 600 ms di attesa della
  prova, ognuna con tutta la pagina in risposta. Ora `FateOf` la dà solo al documento.
- Su Windows il motore legge `\\host` come `file://host`, non come un sito.
- Su questo Windows i nomi riservati del rapporto sono file normali.
- Il controllo "S-01, un disegno che non finisce" del blocco 2 è caduto una volta su cinque giri dell'insieme
  dell'audit (266 ms di processore del processo nei 400 ms dopo, soglia 200) ed è passato negli altri quattro e nelle
  due suite complete; il codice che prova non era cambiato dal giro prima, passato. Conta il processore di tutto il
  processo, non del solo thread fermato. Non toccato.

### Controlli e suite

- `Security2AuditTests` (`Core/ProvaBozze.Sicurezza2.cs`, da soli in 10 secondi con `--prova-audit risultato.txt
  sicurezza2`), 26 controlli: T-01 (11), T-02 (5), T-03, T-04, T-05, T-07, T-08 (3), T-09 (2), T-10. Sette righe di sola
  misura: le richieste del motore, la forma che per il motore resta sulla pagina, come legge le forme del rapporto, la
  tabella di pulizia e motore sulle 82.824 forme, T-06, la seconda cartella di IMAP, le pagine web allegate. Con
  `MISSIVA_PROVA_MOTORE=file` ogni richiesta del motore finisce nel file.
- Tre controlli di prima dicono una cosa nuova (il clic singolo su IMAP, T-03) e uno una cosa più stretta (l'indirizzo
  con la h scritta `\68` ora si conta fra le immagini bloccate).
- Suite completa: 920 controlli, tutto bene, sul codice di `66d7696` (il commit dopo ha i documenti e un commento in
  `Safety.cs`), in 191 secondi. Prima di cominciare, sul codice di `d42cab4`: 894, tutto bene, in 185. L'insieme dell'audit da solo
  (`--prova-audit` senza nome) è girato cinque volte durante il lavoro: la prima con il controllo del blocco 1 sulla h
  scritta `\68` ancora da aggiornare, poi 332, 340 e 347 controlli tutti passati, e una volta 336 con il solo "S-01, un
  disegno che non finisce" caduto (vedi "Trovato in più").
- Compilazione Debug: 0 errori, nessun avviso nuovo (gli stessi 11 più NU1510, su righe non toccate qui). La
  configurazione Release compila (`dotnet build -c Release`); la versione finale di prova non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Un messaggio di un mittente consentito con la fascia rossa non carica più le immagini da solo: compare "Mostra
  immagini", che vale per quella volta. Lo stesso in una busta che sembra posta certificata ma non è firmata da un
  gestore, e in un .eml aperto da un messaggio della Posta indesiderata.
- In un'email con immagini scritte in modo insolito, o con un CSS mai chiuso, quelle immagini sono bloccate e contate
  anche per il mittente non consentito che sta in una conversazione con uno consentito.
- Un'immagine con un indirizzo che non è né un sito né un'immagine dell'email (`file:`, `blob:`, un percorso di Windows)
  perde l'indirizzo. Come si vede poi nel riquadro non è stato guardato.
- Le animazioni dei disegni SVG che cambiano immagine non girano più.
- Account IMAP: "Annulla iscrizione" non parte più con un clic (resta il messaggio da inviare, o la pagina), e un
  dominio non diventa autenticato per la sola posta arrivata lì. I loghi dei domini già autenticati restano.
- Un allegato `.appcontent-ms`, `.udl` o `.diagpkg` chiede conferma. Un allegato che si chiama `COM0.zip` o `CON .txt` si
  salva con un `_` davanti.
- Se il disegno di un logo si pianta, per il resto della sessione (o finché non si sblocca) non compaiono loghi nuovi.

### Scelte fatte qui, da rivedere

- **`\\host` è dato per esterno** anche se su Windows il motore lo legge come `file:`: su un altro sistema è un sito.
- **Gli altri schemi si tolgono sempre**, anche al mittente consentito.
- **Le animazioni di `href` si tolgono a tutti**, e ogni attributo con una parentesi e una barra rovesciata insieme.
- **`missiva-bloccato(`** al posto di una `url(` mai chiusa: se sta dentro un commento o un testo del CSS si vede scritto
  così.
- **La pagina solo al documento**: non provato nella finestra (stampa, Salva come PDF).
- **IMAP `Unproven` sempre**, anche dove il server scrive davvero la sua intestazione.
- **Gmail non in cima `Unproven`**, invece di ignorarla.
- **Un disegno fermo sospende i loghi finché gira**, non per tutta la sessione.
- **Il motivo nel suggerimento** della fascia delle immagini ("Il mittente di questo messaggio non è verificato", "La
  posta certificata o la firma di questo messaggio ha un avviso rosso").
- **Un WebView2 dentro la suite**: è la prima prova che lo avvia. Sei giri di seguito senza una caduta (4,4-4,5 secondi
  l'uno, quando il gruppo aveva solo i controlli di T-01), e nessuna caduta negli altri giri di questo blocco. Se il
  Runtime manca o non parte, la prova cade con una riga che lo dice.
- Due funzioni in più nel riquadro (`Confine`, `FateOf`) e un argomento in più nella Debug (`--eml-indesiderata`).

### Scelte per l'autore

- **.eml allegati a un messaggio normale** (T-02): le immagini di un mittente consentito scritto nel loro From devono
  caricarsi da sole? Proposta: no, come nella Posta indesiderata; il From di un .eml non lo verifica nessuno. Costa un
  clic su "Mostra immagini" quando apri un messaggio inoltrato come allegato.
- **Account IMAP** (T-03): vuoi il lavoro più lungo per riavere il dominio autenticato e il clic singolo sui server che
  scrivono davvero la loro intestazione? Proposta: solo se usi un account IMAP normale; serve provarlo su quel server.
- **Allineamento senza DMARC** (T-06): insieme alla scelta in attesa dal blocco 2. Proposta: chiedere DMARC superato
  chiude tutti e due.
- **Seconda cartella di indesiderata su IMAP** (T-08): una cartella "Spam" o "Junk" in cima, oltre a quella segnata dal
  server, va trattata come Posta indesiderata? Proposta: sì per immagini, loghi e rubrica, senza cambiare Svuota, che
  lì cancella per sempre e la cartella può essere tua.
- **Pagine web allegate** (T-10): `.html`, `.htm`, `.svg` e `.mht` devono chiedere conferma prima di aprirsi nel
  browser? Proposta: sì, con un testo suo ("è una pagina web arrivata per posta: non scriverci password"), senza il
  rosso dei tipi che avviano programmi.

### Non verificato

- **Niente a finestra.** Passi per l'autore, dopo l'installazione: aprire una newsletter di un mittente consentito (le
  immagini si vedono come prima); aprire un messaggio con la fascia rossa di un mittente consentito, se ce n'è uno (le
  immagini sono bloccate, e il suggerimento di "Mostra immagini" dice che vale solo per questa volta); stampare un
  messaggio e salvarlo come PDF (la pagina ora si dà solo al documento); aprire un .eml allegato a un messaggio della
  Posta indesiderata.
- **La Missiva installata e la versione finale** non sono state eseguite.
- **T-01**: la posta vera non è stata riletta con la pulizia nuova (il blocco 1 l'aveva fatto sulla copia di prova). Il
  confronto è su messaggi generati, testi di prova e pagine di documentazione.
- **T-01**: il motore è il Runtime 154 di questo PC, senza finestra. Che una finestra vera faccia le stesse richieste
  non è provato; le quattro strade di controllo dicono che il motore senza finestra carica immagini, sfondi, stili e
  disegni. Altre strade del CSS che caricano immagini e che qui non sono venute in mente restano possibili: la CSP della
  pagina le ferma quando il messaggio è da solo, non in una conversazione con un mittente consentito.
- **T-01**: quando il Runtime si aggiorna, il confronto con il motore gira di nuovo nella suite; qui vale per la 154.
- **T-02**: `ShowImages` nella finestra (qui `RememberImages`, la funzione che chiama), e la finestra dell'.eml.
- **T-03, T-04, T-05**: nessun server vero. Quanti server IMAP lascino passare l'intestazione, se Gmail scriva sempre la
  sua per prima, in che ordine Graph dia le intestazioni: come nel rapporto, SOSPETTO.
- **T-07**: un SVG vero che tenga SharpVectors in codice nativo oltre il tempo. E `FetchAsync` con i disegni sospesi:
  letto, non provocato.
- **T-08**: le quattro decisioni del riquadro sono lette dal sorgente, non provocate.
- **T-09**: Windows 10, dove quei nomi dovrebbero essere dispositivi.
- **T-10**: l'elenco di Outlook per i tre tipi aggiunti.

## Audit dell'11 ottobre, sicurezza, terzo gruppo: annulla iscrizione, Graph, canale, ganci di prova, editor (T-11..T-13, T-23..T-36)

Fatto l'11 ottobre 2026 sui punti T-11, T-12, T-13 e T-23..T-36 di `sicurezza-2.md`: quello che restava della parte B
("codice nuovo") e la parte C ("mai guardato"). "Prima" è il commit `3f18d58`. Commit: `919ac06` (T-28), `7569784` (T-31,
T-32), `6f2b588` (T-11, T-12, T-13), `1b7c25b` (T-23, T-24), `fee95f9` (T-25, T-27), `0fc2081` (T-29), `a6d58ac` (T-33,
T-35, T-36 e la misura di T-34), più quello di questo esito. Su diciassette punti: dieci corretti (T-11, T-24, T-27,
T-28, T-29, T-31, T-32, T-33, T-35, T-36), quattro corretti in parte con il resto lasciato (T-12, T-13, T-23, T-25), due
confermati e lasciati (T-26, T-34), uno falso su questo PC (T-30). In fondo c'è il riepilogo di tutti i 36 punti.

Come si è misurato:

- **Dopo**: `Security3AuditTests` (`Core/ProvaBozze.Sicurezza3.cs`, da soli in 7 secondi con `--prova-audit risultato.txt
  sicurezza3`), 25 controlli e tre righe di sola misura. Solo dati finti, cartelle temporanee loro, server finti e
  ascoltatori su indirizzi di loopback.
- **Prima**: dove si poteva, lo stesso controllo lanciato con la sola correzione spenta a mano, e visto cadere: T-23 (la
  pagina che rimanda altrove), T-24, T-31 (tre controlli), T-33, T-35, T-36. Per T-28 e T-29 una compilazione vera del
  codice di prima. Per T-27 e T-30 una prova fuori da Missiva. T-11, T-12, T-13, T-25 e T-26 letti nel codice; dove il
  controllo lo permette dice anche che cosa valeva prima (per T-12 che `Authenticated` è vero con il solo SPF, per T-13
  che l'indirizzo mostrato prima aveva la lettera non latina).
- **La versione finale senza avviarla** (T-28): `dotnet publish -c Release -p:MissivaProva=true
  -p:DefineConstants=DEBUG%3BTRACE`, come dice CLAUDE.md, verso una cartella del lavoro e non verso `prova-release`. Poi
  uno script che trova `Missiva.dll` dentro l'eseguibile a un file solo e ne legge i metadati (System.Reflection.Metadata):
  le stringhe che `Program.Main` carica, e dopo la correzione il campo `Program.Build`. Nessun eseguibile è mai stato
  avviato senza `MISSIVA_DATA`. I due eseguibili pubblicati così sono stati avviati solo con `MISSIVA_DATA` verso la
  cartella dei dati di prova e solo per un gruppo dell'audit (`--prova-audit`), per T-29.
- **Che cosa parte, nel motore di WPF** (T-31, T-32): un `RichTextBox` senza finestra, misurato e disposto come lo
  disporrebbe la finestra di scrittura, e ascoltatori che segnano la prima riga di ogni richiesta. Per gli indirizzi http
  un ascoltatore su una porta libera di 127.0.0.1. Per i percorsi di rete (`\\server\cartella`) un ascoltatore sulla
  porta 80 di un indirizzo di loopback scelto a caso (127.x.y.z): quando la condivisione non c'è Windows prova lo stesso
  percorso via WebDAV, e la richiesta arriva lì. Su questo PC il servizio WebClient era acceso. Windows ricorda per un
  minuto i server WebDAV che non hanno risposto: per questo ogni prova ha il suo indirizzo. Il tentativo SMB verso lo
  stesso indirizzo finisce al servizio di condivisione di questo PC e non si vede: quello che si vede è che Windows va
  in rete al percorso scritto nel nome del carattere.
- **Il controllo che la prova veda** (T-31): un `Run` con il carattere-indirizzo messo a mano deve far arrivare una
  richiesta all'ascoltatore, altrimenti la riga dice "non misurato" e il resto non si giudica. Arriva in 211-309 ms
  (nei quattro giri guardati).
- **Incolla senza gli appunti** (T-32): gli appunti veri non sono stati toccati. La scelta del formato si prova con
  oggetti dati fatti a mano e con l'evento che WPF solleva prima di incollare e di lasciar cadere
  (`DataObjectPastingEventArgs`), su un editor vero. Che cosa farebbero XAML e RTF si misura con `TextRange.Load`, che usa
  gli stessi convertitori di Incolla. Una sonda in PowerShell 7.6.6 (il WPF di .NET 10.0.12), mai entrata nel repository,
  ha provato tredici contenuti: i suoi risultati sono sotto, a T-32.
- **Un protocollo di prova** (T-27): `missiva-prova-t27:` registrato in `HKCU\Software\Classes` dalla shell di lavoro, che
  vede una copia privata del registro (quello vero non è stato toccato), con un gestore che scrive i suoi argomenti in un
  file. Tolto alla fine.
- **Un albero finto** (T-30): cartelle e giunzioni create dentro una cartella del lavoro, cancellate con Windows
  PowerShell 5.1.26100.9549 e con PowerShell 7.6.6. Mai `installa.ps1` né `disinstalla.ps1`.
- Nessuna finestra, nessun avvio della versione di prova, nessuna rete fuori dal PC. I dati veri e il registro vero non
  sono stati letti.

### Punti del rapporto

- **T-11** (Annulla iscrizione con un messaggio parte dall'indirizzo principale, non dall'alias). CONFERMATO nel codice,
  CORRETTO (`6f2b588`).
  - Prima: `OnMailto` apriva un messaggio nuovo senza originale, quindi dall'indirizzo principale; la conferma del clic
    singolo nominava l'indirizzo principale.
  - Ora `Unsubscribe.Recipient` dà l'indirizzo a cui il messaggio era scritto, con la stessa regola di Rispondi
    (`SendIdentity.For`: un altro indirizzo dell'account fra i destinatari, e non il principale). La conferma nomina
    quello; `MailtoRequested` porta l'identità e `ComposeWindow` la riceve (`sendAs`), con la sua firma.
  - Controlli: 2. Il secondo legge il sorgente: nessuna prova crea le finestre.
- **T-12** (clic singolo senza controllare che la firma DKIM copra le due intestazioni). CONFERMATO. CORRETTO in parte
  (`6f2b588`), il resto lasciato.
  - Prima: con `dmarc=pass` e il solo SPF (nessuna firma DKIM), `SenderCheck.Authenticated` è vero e il comando era la
    POST di un clic.
  - Ora serve una firma DKIM superata del dominio del mittente, o di un suo sottodominio (`SenderCheck.Signed`): con il
    solo SPF, con la firma di un altro dominio o con la firma non superata resta il messaggio da inviare, o la pagina.
    È l'unica prova che la RFC 8058 accetta.
  - LASCIATO: guardare che il `h=` della DKIM-Signature contenga List-Unsubscribe e List-Unsubscribe-Post. Servirebbe
    leggere le DKIM-Signature nei tre provider e tenerle nelle copie su disco (quelle di prima non le hanno): non è
    semplice, e l'abuso pratico è SOSPETTO anche nel rapporto.
- **T-13** (la conferma mostra l'host in due forme, e non dice che l'indirizzo risulta attivo). La prima parte
  CONFERMATA, CORRETTA (`6f2b588`); la frase lasciata all'autore.
  - Prima: l'indirizzo intero era `Uri.AbsoluteUri`, con la lettera cirillica al suo posto, accanto al nome del sito in
    punycode.
  - Ora `UnsubscribeAction.Shown` scrive l'indirizzo con il nome in punycode (`https://liste.xn--ntizie-prova-4fk.example/...`
    nel controllo), al più 300 caratteri; lo usa anche l'avviso dopo una richiesta non riuscita.
  - La frase "il mittente saprà che questo indirizzo è attivo": non aggiunta. Vedi "Scelte per l'autore".
- **T-23** (Graph: un indirizzo assoluto della risposta riceve il token; `uploadUrl` senza controllo dell'host).
  CONFERMATO con il gestore finto. CORRETTO in parte (`1b7c25b`).
  - Prima: una pagina con `@odata.nextLink` verso `altrove-prova.example`, e la richiesta dopo ci andava con
    `Authorization`: 99 volte, una per pagina, finché la lettura non aveva i suoi cento messaggi.
  - Ora `GraphProvider.Target`: un indirizzo relativo sta sotto `Base`; uno intero vale solo se è https, con lo stesso
    nome di `Base`, sulla sua porta e senza nome utente. Altrimenti la lettura si ferma con "Microsoft Graph ha risposto
    con un indirizzo che non è il suo". Otto indirizzi di prova rifiutati (un altro sito, il nome di Graph come prefisso
    o come nome utente, un'altra porta).
  - I pezzi degli allegati grandi: un client loro (`UploadHandler`), senza reindirizzamenti e senza cookie. Misurato con
    due ascoltatori su 127.0.0.1: un 307 del primo, e con il gestore che i pezzi usavano prima il PUT arrivava al
    secondo ("PUT /altrove"); con quello nuovo la risposta resta 307 e al secondo non arriva nulla.
  - LASCIATO: limitare `uploadUrl` ai server di caricamento di Outlook. La funzione (F-15) è provata solo sul Graph
    finto: il nome vero di quel server qui non è mai stato visto, e un elenco scritto a memoria potrebbe fermare gli
    allegati grandi. Il registro ora dice a quale server sono andati i pezzi ("allegato a pezzi caricato su ..."): con
    quel nome l'elenco si può fare. Le PUT non portano il token, come dice il rapporto.
- **T-24** (allegati grandi: la misura letta prima di aprire il file). CONFERMATO, CORRETTO (`1b7c25b`).
  - Prima, con un file che cresce di 5.000 byte mentre Graph riceve il testo della bozza: partiva in 3 pezzi, 3.150.049
    byte dei 3.155.049, con i Content-Range coerenti e nessun errore.
  - Ora la misura presa all'inizio si confronta con quella del file appena aperto per il caricamento: se è cambiata,
    "... è cambiato mentre il messaggio veniva preparato: l'allegato non è stato aggiunto", senza nessun pezzo inviato.
    Da lì il file è aperto senza lasciar scrivere agli altri programmi, come prima. I file piccoli si misurano e si
    leggono dallo stesso file aperto: non possono più entrare in memoria più grandi di come sono stati contati.
  - Non il `ReadByte() == -1` dopo l'ultimo pezzo, proposto dal rapporto: con il file aperto in sola lettura per gli
    altri non può crescere, e la misura è già confrontata.
- **T-25** (canale con nome: un altro processo può occuparne il nome e spegnere mailto:, "apri" ed "esci"). CONFERMATO
  nel codice e su un canale di prova. CORRETTO in parte (`fee95f9`).
  - Il comportamento di .NET, SOSPETTO nel rapporto: con il nome già preso `NamedPipeServerStream` non si apre
    (`IOException`), anche per lo stesso utente.
  - Prima: cinque errori qualsiasi, mai azzerati, e il ciclo finiva per tutta la sessione.
  - Ora `App.ServeCommandsAsync` riprova per sempre, sempre più di rado (1, 2, 4... fino a 60 secondi), dice l'errore
    nel registro la prima volta e poi ogni venti; un collegamento riuscito azzera il conto; e il canale si chiude prima
    di eseguire il comando, così mentre un mailto: apre la sua finestra (o un avviso) il collegamento dopo trova già
    l'istanza nuova. Nel controllo: 12-13 tentativi con il nome occupato, poi mailto:, "apri" ed "esci" ricevuti in
    fila.
  - LASCIATO: l'impronta dell'utente nel nome. Il SID non è un segreto, quindi chi vuole occupare il nome lo calcola; e
    cambiare nome toglie "esci" al primo aggiornamento della versione installata, che ascolta ancora su quello di prima.
  - LASCIATA: la seconda istanza sempre in attesa. In .NET più istanze vogliono dire rinunciare al controllo "prima
    istanza", e chi ha preso il nome potrebbe ricevere una parte dei comandi.
- **T-26** (un'attivazione con argomenti scelti agisce su qualunque messaggio della copia locale). CONFERMATO nel codice,
  LASCIATO.
  - `OnToast` passa account, cartella e messaggio degli argomenti a `NotificationActionAsync`, che cerca il messaggio
    anche nella copia locale di quella cartella (`FindNotified`) ed elimina, contrassegna o pospone senza chiedere.
  - La proposta semplice (solo i messaggi in `notifiedMessages`) toglie una cosa chiesta dall'autore: i pulsanti di una
    notifica rimasta dopo un riavvio, e quelli con Missiva chiusa che Windows avvia nascosta, perché quell'elenco sta
    in memoria. La proposta migliore (un segreto negli argomenti) vuole un segreto salvato, e un programma dello stesso
    utente che sa attivare il gestore delle notifiche sa anche leggerlo. Vedi "Scelte per l'autore".
  - Restringere alle sole cartelle Posta in arrivo: non fatto. Su Graph non è stato visto dal vero se la cartella
    scritta nella notifica è sempre uguale a quella che Missiva chiama Posta in arrivo.
- **T-27** (il comando del link mailto: è `"exe" "%1"`). SOSPETTO nel rapporto, CONFERMATO qui per la parte di Windows,
  CORRETTO (`fee95f9`).
  - Misurato con il protocollo di prova: Windows mette il link al posto di `%1` senza toccare le virgolette.
    `missiva-prova-t27:a@b.example" --nascosta "x` arriva al programma come tre argomenti (il link, `--nascosta`, `x`);
    scritto con `%22` resta un argomento solo; un link con uno spazio resta un argomento solo.
  - Ora `installa.ps1` registra `"Missiva.exe" --mailto "%1"`, e dopo `--mailto` `Program.Arguments` tiene solo il primo
    argomento che è un mailto: (in `Main` e in `App.OnStartup`). Senza `--mailto`, cioè con il comando registrato da
    un'installazione di prima, tutto com'era.
  - Che cosa passino i browser e i lettori di PDF: non provato.
- **T-28** (la versione finale con gli strumenti di prova perde PROVA). CONFERMATO con la compilazione vera, CORRETTO
  (`919ac06`).
  - Prima: MSBuild dava al compilatore `DEBUG;TRACE`, e nel `Program.Main` dell'eseguibile pubblicato c'era il ramo che
    senza `MISSIVA_DATA` esce solo con `-ToastActivated`.
  - L'eseguibile che sta oggi in `prova-release` (8 ottobre) ha PROVA: era stato compilato senza quella proprietà. Non è
    stato toccato.
  - Correzione alla radice: `Missiva.csproj` ha `TreatAsLocalProperty="DefineConstants"`. Il valore della riga di comando
    diventa quello di partenza e il progetto ci aggiunge PROVA: `DEBUG;TRACE;TRACE;PROVA;RELEASE`. Le altre compilazioni
    hanno le costanti di prima (`TRACE;PROVA;RELEASE`, `TRACE;RELEASE`, `TRACE;DEBUG`).
  - La guardia: `Program.ClosesWithoutData`. La PROVA si chiude sempre; la Debug quando la avvia una notifica, com'era, e
    ora anche quando non sta in `bin\Debug`: copre una versione finale compilata con DEBUG e senza
    `-p:MissivaProva=true`.
  - `Program.Build` scrive nel file le costanti di compilazione. Nell'eseguibile ripubblicato: "DEBUG PROVA".
  - Controlli: 2. Il secondo chiede a MSBuild le costanti delle tre righe di comando, senza compilare (1,3-1,8 s).
- **T-29** (ganci rimasti nella versione installata). CONFERMATO, CORRETTO (`0fc2081`).
  - `MISSIVA_RIGA_VECCHIA`, `MISSIVA_SENZA_SOSPENSIONE`, `MISSIVA_SENZA_STALL`: ora si leggono solo nelle versioni di
    prova (DEBUG o PROVA). Un controllo legge i 128 sorgenti con le loro direttive `#if`: nel codice che entra nella
    versione installata non resta nessuna variabile `MISSIVA_`. Il modello di riga di prima resta nell'XAML.
  - `DOTNET_STARTUP_HOOKS`, misurato: un assembly di prova che scrive un file, e l'eseguibile a un file solo pubblicato
    dal codice di prima lo caricava prima di `Main`. Ora fuori dalla Debug il progetto ha `StartupHookSupport=false`, e
    lo stesso eseguibile ripubblicato non lo carica.
  - La tabella "Ganci di prova" del rapporto: riletta, nessun altro punto. Gli argomenti della versione installata
    restano `--pdf-worker`, `--riavvio`, `--nascosta`, `-ToastActivated`, il link, e ora `--mailto`.
- **T-30** (`disinstalla.ps1` con Windows PowerShell 5.1 e le giunzioni; `LOCALAPPDATA` vuota). FALSO su questo PC.
  - `Remove-Item -Recurse -Force` su una cartella con dentro una giunzione verso un'altra cartella, con la giunzione due
    livelli più sotto, e con la cartella che è essa stessa una giunzione: la giunzione si toglie e i file della cartella
    a cui punta restano tutti. Lo stesso con la forma di `installa.ps1` (`Get-ChildItem | Remove-Item -Recurse`). Con
    5.1.26100.9549 e con 7.6.6.
  - `LOCALAPPDATA` vuota: `Join-Path` rifiuta l'argomento e, con `$ErrorActionPreference = "Stop"`, lo script si ferma
    lì. Il percorso non diventa relativo.
  - Gli script non sono cambiati.
- **T-31** (il `font-family` di una bozza ripresa arriva a `new FontFamily` senza filtro). SOSPETTO nel rapporto,
  CONFERMATO nel motore di WPF, e più largo. CORRETTO (`7569784`).
  - Per WPF "indirizzo#nome" è un carattere da cercare a quell'indirizzo. Un `Run` con il carattere
    `\\127.x.y.z\caratteri\#Prova`, disposto, fa arrivare `OPTIONS /caratteri HTTP/1.1` all'ascoltatore, e il disegno
    solleva un'eccezione ("Impossibile raggiungere la destinazione del percorso di rete"). Lo stesso con
    `file://127.x.y.z/caratteri/#Prova`. Con un nome che non esiste (`.invalid`) nessuna eccezione e niente da vedere.
  - Prima: la bozza di prova, con quei caratteri in un `div`, in due `span` e in un `b` dentro una tabella, li portava
    tutti nell'editor; disposta, la richiesta arrivava e il disegno sollevava l'eccezione.
  - Ora `HtmlToFlow.FontName` lascia passare solo un nome: lettere, cifre, spazi e trattini, al più 64 caratteri, come lo
    spedisce `FlowToHtml`; mai `#`, barre o due punti (`IsFontLocation`). Vale per bozze e risposte automatiche.
  - La stessa strada in altri tre posti, non nel rapporto. Il carattere dei messaggi delle impostazioni
    (`Settings.ComposeFont`), che arriva anche da una copia di sicurezza: un indirizzo diventa "Segoe UI". L'RTF di firme
    e modelli (`RichEditor.BlocksFromRtf`): i caratteri-indirizzo si tolgono prima che qualcuno li disegni
    (`ClearFontLocations`). L'RTF degli appunti: vedi T-32.
  - Controlli: 4. Verso un server vero Windows proverebbe anche SMB, con le credenziali dell'utente: è il motivo della
    gravità Media nel rapporto, e qui non si è visto (niente rete vera).
- **T-32** (Incolla nell'editor: RTF e XAML degli appunti lasciati a WPF). SOSPETTO nel rapporto, CONFERMATO in parte.
  CORRETTO (`7569784`).
  - La sonda, tredici contenuti. RTF con `INCLUDEPICTURE` (http e percorso di rete) e con un oggetto incorporato: nulla.
    RTF con `HYPERLINK`: solo un link. RTF con un carattere-indirizzo: richiesta ed eccezione, come T-31. XAML con
    un'immagine http: `GET` appena letto, prima di ogni disegno. XAML con un'immagine da un percorso di rete: richiesta.
    XAML con un `Frame`: `GET` della pagina. XAML con un carattere-indirizzo: richiesta ed eccezione. `MediaElement`:
    nulla in 1,2 secondi senza finestra. `ObjectDataProvider`: caricato senza effetti visibili, non guardato oltre.
  - Lo XAML negli appunti lo mette solo un programma fatto con WPF: una pagina web non può. L'RTF sì, da un documento
    scritto da altri e copiato da Word.
  - Ora, in ogni editor di Missiva (`RichEditor.PasteFormat`, anche firme, modelli e risposte automatiche, che prima non
    avevano nessun controllo): lo XAML si incolla solo se viene da un editor di questa Missiva, riconosciuto da un
    formato in più che la copia aggiunge (`OwnCopy`, con un nome che vale per il processo); quello di un altro programma
    mai, al suo posto l'RTF o il testo; l'RTF con un carattere-indirizzo si incolla come testo semplice. Vale anche per
    il trascinamento.
  - Non "mai lo XAML", come proponeva il rapporto. Misura nella suite: un testo di Missiva con carattere, colori,
    elenco, link, tabella e immagine, passato dall'RTF invece che dallo XAML, guadagna `text-align:justify` in ogni
    paragrafo e `color:#000000` in ogni pezzo (2.482 caratteri di HTML invece di 1.715). Nel tema scuro sarebbe testo
    nero.
  - Il costo: un RTF che contiene un cancelletto si legge due volte. 141 KB con 2.000 paragrafi: 370-543 ms in più.
    Senza cancelletto, nulla.
  - Controlli: 3, più due righe di misura. Il formato in più passa a Windows come gli altri (chiesto all'oggetto dati
    per la strada di OLE: un blocco di 1 byte).
- **T-33** (inviti: il link della riunione riconosciuto con `EndsWith` senza il punto). CONFERMATO, CORRETTO (`a6d58ac`).
  - Prima: cinque indirizzi di prova con il nome di un servizio in coda al nome del sito (`malzoom.us`,
    `xteams.microsoft.com`, `notwebex.com`, `riunioni-whereby.com`, `finto-meet.google.com`) erano il link della
    riunione.
  - Ora `Invitation.IsMeetingSite`: il nome del servizio, o un nome che ci sta sotto con il punto, come già in
    `CalendarView`; il confronto è sul nome in punycode. Otto indirizzi rifiutati, sei veri accettati, e lo stesso da
    un .ics.
- **T-34** (regole di Outlook che inoltrano fuori: segnalate solo in Impostazioni). CONFERMATO, LASCIATO.
  - Le regole di Outlook si leggono solo quando si apre Impostazioni, Posta, Regole. Un avviso alla comparsa vuol dire
    leggerle ogni tanto in sottofondo (una richiesta in più a Graph, per gli account che hanno dato quel consenso),
    tenere un'impronta delle regole viste e una notifica nuova: è una funzione. Vedi "Scelte per l'autore".
  - Il confronto dei domini è a posto, come dice il rapporto (misura nella suite): di sei inoltri, fuori sono un dominio
    che comincia come quello dell'account, uno che finisce allo stesso modo senza il punto e un altro dominio; il
    collega, un altro indirizzo dell'account e un sottodominio no.
- **T-35** (sito dei contatti verso la rete interna). CONFERMATO, CORRETTO (`a6d58ac`).
  - Prima: dieci indirizzi numerici su tredici provati diventavano il collegamento "Apri il sito", anche `0x7f.0.0.1`,
    `2130706433` (che .NET legge come 127.0.0.1) e `[::ffff:192.168.1.1]`. Gli altri tre, IPv6 senza punti, erano già
    rifiutati.
  - Ora un indirizzo numerico vale solo se è di Internet (`SenderLogos.IsPublic`, lo stesso dei loghi). I nomi restano
    come prima: un nome che porta alla rete di casa non si riconosce senza chiederlo al DNS.
- **T-36** (registro: a capo dei testi di terzi non tolti, nomi delle cartelle in chiaro). CONFERMATO, CORRETTO
  (`a6d58ac`), non come proposto.
  - Prima: `Log.Write` scriveva il testo com'era. Il nome di una cartella con dentro un a capo e una riga con la data
    diventava una voce in più del registro.
  - Ora `Log.OneEntry`: una voce comincia in prima colonna con la data, le righe che continuano sono rientrate di
    quattro spazi, gli altri caratteri di controllo (e i separatori di riga di Unicode) diventano uno spazio.
  - Non "ogni a capo diventa uno spazio", come proponeva il rapporto: la traccia di un errore finirebbe su una riga
    sola. Così resta leggibile, rientrata.
  - I due nomi di cartelle IMAP in chiaro (`ricerca in ...`, `ripresa della copia in ...`) passano da `Log.Private`. Un
    controllo legge `ImapProvider.cs`: non ne restano.
  - Che un server vero ammetta a capo nei nomi delle cartelle resta SOSPETTO, come nel rapporto.

### Trovato in più

- Il carattere-indirizzo non è solo una connessione: il disegno del testo solleva un'eccezione. Che cosa ne faccia la
  finestra di scrittura (un avviso, o Missiva che si chiude) non è stato provato. Chiuso con T-31.
- Tre strade oltre la bozza per lo stesso difetto: il carattere dei messaggi nelle impostazioni, l'RTF di firme e
  modelli (tutti e due anche da una copia di sicurezza preparata da altri), l'RTF degli appunti. Chiuse con T-31 e T-32.
- Lo XAML degli appunti può portare un `Frame`, che apre una pagina dentro l'editor. Chiuso con T-32.
- Gli editor di firme, modelli e risposte automatiche non avevano nessun controllo su Incolla: ora hanno quello della
  scrittura.
- Il ciclo del canale con nome aspettava la fine del comando prima di riaprire il canale: con un avviso aperto da un
  mailto: (nessun account) "esci" di `installa.ps1` non arrivava. Corretto con T-25.
- Windows non decodifica `%22` nei link che passa ai programmi, e non tocca le virgolette vere (T-27).
- Quando il registro non si riesce a scrivere, `Log.Append` annota il motivo in `missiva-errori.log` nella cartella vera
  dei dati, anche nelle versioni di prova che lavorano altrove. Letto, non toccato.
- Il controllo "S-01, un disegno che non finisce" del blocco 2, dato per instabile dal gruppo precedente: nelle due
  suite complete di questo blocco non è caduto.

### Controlli e suite

- `Security3AuditTests`, 25 controlli: T-11 (2), T-12, T-13, T-23 (3), T-24, T-25, T-27, T-28 (2), T-29 (2), T-31 (4),
  T-32 (3), T-33, T-35, T-36 (2). Tre righe di sola misura: lo XAML con un'immagine, il copia e incolla via RTF, T-34.
  Se la porta 80 di loopback non si può prendere o il servizio WebClient è spento, il controllo di T-31 nel motore dice
  "non misurato" e non cade. I controlli di T-28 e T-29 chiamano `dotnet msbuild` per leggere due proprietà: senza
  `dotnet` nel percorso lo dicono e non cadono.
- Nessun controllo di prima è cambiato.
- Suite completa: 945 controlli, tutto bene, sul codice di questo esito (dopo `a6d58ac` cambiano solo un controllo di
  T-32, che chiede il formato in più per la strada di OLE, e un indirizzo nella misura di T-34), in 159 secondi. Due
  giri prima: sul codice di `a6d58ac` 945, tutto bene, in 184 secondi; con il controllo in più di T-32 945, tutto bene,
  in 327 secondi (il più lento dei tre: il motivo non è stato cercato). Prima di
  cominciare, sul codice di `3f18d58`, la suite non è stata rilanciata: il numero di partenza, 920, è quello del gruppo
  precedente.
- Il gruppo da solo è girato anche dentro la versione finale con gli strumenti di prova pubblicata nella cartella del
  lavoro (un file solo, `Program.Build` "DEBUG PROVA"): tutto bene, con i controlli che leggono i sorgenti saltati.
- Compilazione Debug: 0 errori, nessun avviso nuovo (gli stessi 11 più NU1510; cinque si sono spostati di riga). La
  configurazione Release compila (`dotnet build -c Release`).
  `prova-release` non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Annulla iscrizione di una newsletter arrivata a un altro indirizzo dell'account: la conferma nomina quell'indirizzo,
  e il messaggio di annullamento parte da lì.
- Annulla iscrizione con un clic solo per i messaggi firmati dal dominio del mittente: qualche newsletter che prima si
  annullava con un clic ora prepara un messaggio o apre la pagina.
- Nella conferma l'indirizzo intero di un sito con lettere non latine è scritto in punycode.
- Incolla in un editor: da un altro programma fatto con WPF arriva l'RTF o il testo, non lo XAML; un testo RTF con un
  carattere che non è un nome si incolla senza formato. Copia e incolla dentro Missiva, e da Word, come prima.
- In una bozza ripresa, in una firma o in un modello un carattere che non è un nome non vale: quel testo prende il
  carattere di base. Un nome con punti o trattini bassi li perde (com'era già in uscita).
- Un allegato grande che cambia mentre il messaggio si prepara dà un errore, invece di partire troncato.
- Il sito di un contatto scritto come indirizzo numerico del PC o della rete di casa non è più un collegamento.
- In un invito "Partecipa alla riunione online" compare solo per i siti dei servizi veri.
- Nel registro le righe che continuano una voce sono rientrate; due righe non dicono più il nome della cartella.
- Dopo la prossima installazione il comando dei link mailto: è `--mailto "%1"`. Fino ad allora vale quello di prima.
- Le tre variabili d'ambiente di prova e `DOTNET_STARTUP_HOOKS` non hanno più effetto sulla versione installata.
- Per chi prova: una Debug copiata fuori da `bin\Debug` e avviata senza `MISSIVA_DATA` si chiude subito.

### Scelte fatte qui, da rivedere

- **`TreatAsLocalProperty`** nel progetto, invece di ritoccare `prova.ps1`: vale per ogni riga di comando. Con quella
  proprietà anche le costanti che mette l'SDK si aggiungono a quelle date a mano (`TRACE` due volte, `RELEASE` accanto a
  `DEBUG`): nessun sorgente guarda `RELEASE`.
- **La Debug fuori da `bin\Debug` si chiude** senza `MISSIVA_DATA`: chi la copia altrove per provarla deve dargliela.
- **Il nome di un carattere**: lettere, cifre, spazi e trattini, al più 64. Un carattere dal nome insolito si perde in
  entrata, non più solo in uscita.
- **`ComposeFont` si corregge da solo** quando si leggono le impostazioni: un valore che non è un nome diventa "Segoe
  UI", in silenzio.
- **Il segno delle copie di Missiva** è un formato degli appunti con un nome nuovo a ogni avvio. Un programma dello
  stesso utente può leggerlo dagli appunti e imitarlo. Fra due Missiva (la prova e l'installata) lo XAML non passa: si
  incolla l'RTF.
- **L'RTF con un carattere-indirizzo perde tutto il formato**, non solo quel carattere.
- **`--mailto` vale solo come primo argomento.**
- **Il canale riprova per sempre**, e i comandi non aspettano più la fine di quello prima: due mailto: di fila aprono
  due finestre insieme.
- **Un indirizzo di Graph che non è di Graph ferma la lettura** con un errore. Dal vero non è mai stato visto un
  `nextLink` verso un altro nome; se esistesse, quella lettura ora fallirebbe.
- **I pezzi degli allegati grandi** hanno un client senza compressione delle risposte e senza HTTP/2, com'erano già.
- **Il sito di un contatto sulla rete di casa** non si apre più da Missiva, nemmeno se lo ha scritto chi usa Missiva.
- **Le tracce degli errori nel registro** sono rientrate di quattro spazi in più.
- **Le tre variabili restano nelle versioni di prova**, dove servono ai confronti; `StartupHookSupport` resta acceso
  nella Debug.

### Scelte per l'autore

- **Conferma di Annulla iscrizione** (T-13): aggiungere "il mittente saprà che questo indirizzo è attivo"? Proposta: no.
  Dove confermare l'indirizzo è un rischio (Posta indesiderata, fascia rossa) il comando non c'è già, e Outlook e Gmail
  non lo dicono.
- **Azioni delle notifiche** (T-26): devono valere solo per i messaggi notificati in questa sessione? Proposta: no. I
  pulsanti di una notifica rimasta da ieri, o con Missiva chiusa, aprirebbero il messaggio invece di agire; e chi può
  attivare il gestore delle notifiche è un programma del tuo stesso utente, che ha strade più corte.
- **Regole che inoltrano fuori** (T-34): vuoi un avviso quando ne compare una? Proposta: sì, come lavoro a parte: le
  regole di Outlook lette all'avvio e una volta al giorno per gli account che hanno già dato quel consenso, e una
  notifica la prima volta che una regola manda la posta fuori.
- **La Debug senza `MISSIVA_DATA`** (T-28): nella sua cartella lavora ancora sui dati veri, accanto alla Missiva
  installata. Deve chiudersi sempre, come la PROVA? Proposta: sì, se non la avvii mai a mano senza `prova.ps1`.
- **Il server dei pezzi degli allegati grandi** (T-23): dopo un allegato da più di 3 MB mandato con un account
  Microsoft, il registro dice "allegato a pezzi caricato su ...". Con quel nome si può limitare il caricamento a quel
  server. Proposta: farlo al prossimo giro.
- **La firma DKIM che copre le due intestazioni** (T-12): vuoi il lavoro più lungo (le DKIM-Signature lette nei tre
  provider e tenute nelle copie su disco)? Proposta: no.

### Non verificato

- **Niente a finestra.** Passi per l'autore, dopo l'installazione:
  1. In una pagina web clicca un link mailto: (con Missiva scelta come app di posta): si apre Nuovo messaggio, come
     prima. Poi con Missiva chiusa: lo stesso.
  2. Nella scrittura copia un pezzo di testo con del grassetto e una tabella e incollalo più sotto: uguale all'originale.
     Copia un testo formattato da Word e incollalo: formato come prima. Trascina un pezzo di testo dentro l'editor.
  3. Apri una bozza in Modifica, una firma e un modello: si vedono come prima.
  4. Se hai una newsletter arrivata a un altro indirizzo dell'account: Annulla iscrizione, e la conferma (o il Da della
     scrittura) deve dire quell'indirizzo.
  5. Un allegato da più di 3 MB con un account Microsoft: parte, e nel registro c'è "allegato a pezzi caricato su ...".
  6. `installa.ps1` chiude la Missiva aperta con "esci" entro pochi secondi, anche nascosta nell'area di notifica.
- **La Missiva installata** non è stata avviata, chiusa né letta. La versione che `installa.ps1` compila non è stata
  eseguita: ha le stesse proprietà di compilazione di quella provata (lette da MSBuild).
- **T-11**: la finestra di scrittura che parte dall'alias (il passaggio è letto dal sorgente).
- **T-12, T-13**: nessuna newsletter vera. Quante perdano il clic singolo perché senza firma DKIM allineata: non contato.
- **T-23**: Graph vero. Che i suoi `nextLink` e `deltaLink` abbiano sempre il nome di `Base`, e il nome del server dei
  pezzi.
- **T-24**: un file davvero in scrittura da un altro programma (qui il file cresce fra due richieste, da questo stesso
  processo).
- **T-25**: un secondo account di Windows che occupa il nome (qui lo occupa lo stesso processo); due copie vere di
  Missiva; `installa.ps1` con il ciclo nuovo.
- **T-26**: che un programma riesca davvero ad attivare il gestore delle notifiche con argomenti suoi (SOSPETTO anche
  nel rapporto).
- **T-27**: il registro vero (la prova è nella copia privata della shell), e che cosa passano browser e lettori di PDF.
- **T-28**: nessun eseguibile avviato senza `MISSIVA_DATA`: che si chiuda è letto dal file (`Program.Build`, e il
  codice di `ClosesWithoutData`), non visto.
- **T-29**: la riga vecchia e la sospensione nella versione installata; `DOTNET_STARTUP_HOOKS` sulla versione di
  `installa.ps1`.
- **T-30**: un Windows PowerShell 5.1 più vecchio (Windows 10), e i collegamenti simbolici, che qui non si possono
  creare senza permessi in più.
- **T-31**: un server vero (SMB, con le credenziali); la finestra di scrittura davanti all'eccezione del disegno; un PC
  con il servizio WebClient spento, dove l'ascoltatore non vedrebbe nulla e il controllo direbbe "non misurato".
- **T-32**: gli appunti veri, con Ctrl+V e con il trascinamento: il formato in più dopo che Windows ha preso in carico
  gli appunti, e il copia e incolla fra due finestre di Missiva. Il formato `XamlPackage` di un altro programma (misurato
  il solo `Xaml`; la scelta li tratta allo stesso modo).
- **T-33**: un invito vero di ognuno dei servizi.
- **T-35**: Persone a finestra (qui `ContactLinks.WebUrl` e `IsAllowed`).
- **T-36**: un server IMAP vero con a capo nei nomi; gli strumenti in `loop\` che leggono il registro, con le tracce ora
  rientrate.

### Riepilogo del secondo audit di sicurezza (T-01..T-36)

I tre gruppi insieme. Su 36 punti: 23 corretti, 8 corretti in parte, 4 confermati e lasciati, 1 falso. Il dettaglio è
nelle tre sezioni qui sopra.

| Punto | In breve | Esito | Commit |
|---|---|---|---|
| T-01 | Immagini esterne scritte come le legge Chromium | CONFERMATO, CORRETTO | `9fb69c3` |
| T-02 | Immagini automatiche con la fascia rossa, negli .eml e nelle PEC false | CONFERMATO, CORRETTO; gli .eml allegati a un messaggio normale lasciati all'autore | `27ea8d3` |
| T-03 | IMAP: Authentication-Results del mittente credute | CONFERMATO, CORRETTO | `2216c62` |
| T-04 | Gmail: la sua intestazione cercata fra tutte | CONFERMATO, CORRETTO | `2216c62` |
| T-05 | Microsoft: `EndsWith` senza punto, authserv-id assente | CONFERMATO in parte, CORRETTO in parte | `2216c62` |
| T-06 | Allineamento a due etichette | CONFERMATO, LASCIATO (misura) | `2216c62` |
| T-07 | Disegno dei loghi non fermato | Il meccanismo CONFERMATO, CORRETTO | `d7560dc` |
| T-08 | Posta indesiderata non riconosciuta | CONFERMATO in parte, CORRETTO il riquadro | `1382c57` |
| T-09 | Nomi riservati incompleti | CONFERMATO, CORRETTO (nessun effetto su questo Windows) | `66d7696` |
| T-10 | Pagine HTML e SVG allegate senza domande | CONFERMATO, CORRETTO in parte; le pagine web lasciate all'autore | `66d7696` |
| T-11 | Annulla iscrizione dall'indirizzo principale, non dall'alias | CONFERMATO, CORRETTO | `6f2b588` |
| T-12 | Clic singolo senza la firma DKIM sulle due intestazioni | CONFERMATO, CORRETTO in parte (serve DKIM; `h=` lasciato) | `6f2b588` |
| T-13 | Conferma: il sito in due forme, e l'indirizzo attivo | CONFERMATO, CORRETTO in parte (la frase lasciata all'autore) | `6f2b588` |
| T-14 | Copia: l'identificativo di un account diventa un percorso | CONFERMATO, CORRETTO | `8a4506c` |
| T-15 | Copia: la password IMAP va al server scritto nella copia | CONFERMATO, CORRETTO | `6f94363` |
| T-16 | Copia: `importazione.dat` costruito chiude Missiva a ogni avvio | CONFERMATO, CORRETTO | `ebbcd09` |
| T-17 | Copia: regole, mittenti delle immagini e rubrica non visti | CONFERMATO in parte, CORRETTO in parte; due scelte all'autore | `b6a12c2` |
| T-18 | Copia: `domini.dat` salta la regola dei 14 giorni | CONFERMATO, CORRETTO | `ba52e04` |
| T-19 | Copia: espansione e iterazioni senza tetto | CONFERMATO, CORRETTO | `ce0e5a4` |
| T-20 | Copia: file messi da parte con gli accessi | CONFERMATO, LASCIATO (misura) | `11c8cf1` |
| T-21 | Modelli illeggibili: il segnaposto nel messaggio | CONFERMATO, CORRETTO | `4cceda7` |
| T-22 | Copia: un "dpapi:..." diventa testo vero | CONFERMATO, CORRETTO | `56f5e04` |
| T-23 | Graph: il token a un indirizzo della risposta; `uploadUrl` | CONFERMATO, CORRETTO in parte (il server dei pezzi lasciato) | `1b7c25b` |
| T-24 | Allegati grandi: misura letta prima di aprire il file | CONFERMATO, CORRETTO | `1b7c25b` |
| T-25 | Canale con nome occupato da un altro processo | CONFERMATO, CORRETTO in parte (il nome lasciato) | `fee95f9` |
| T-26 | Azioni delle notifiche su qualunque messaggio | CONFERMATO nel codice, LASCIATO | nessuno |
| T-27 | Comando del mailto: `"exe" "%1"` | CONFERMATO, CORRETTO | `fee95f9` |
| T-28 | Versione finale di prova senza PROVA | CONFERMATO, CORRETTO | `919ac06` |
| T-29 | Ganci rimasti nella versione installata | CONFERMATO, CORRETTO | `0fc2081` |
| T-30 | `disinstalla.ps1`, giunzioni e `LOCALAPPDATA` vuota | FALSO su questo PC | nessuno |
| T-31 | `font-family` di una bozza a `new FontFamily` | CONFERMATO, CORRETTO | `7569784` |
| T-32 | Incolla: RTF e XAML lasciati a WPF | CONFERMATO in parte, CORRETTO | `7569784` |
| T-33 | Inviti: `EndsWith` senza il punto | CONFERMATO, CORRETTO | `a6d58ac` |
| T-34 | Regole che inoltrano fuori, viste solo in Impostazioni | CONFERMATO, LASCIATO (misura) | `a6d58ac` |
| T-35 | Sito dei contatti verso la rete interna | CONFERMATO, CORRETTO | `a6d58ac` |
| T-36 | Registro: a capo e nomi delle cartelle | CONFERMATO, CORRETTO | `a6d58ac` |

## Audit dell'11 ottobre, codice, primo gruppo: difetti di comportamento (C-50..C-63)

Fatto il 12 ottobre 2026 sulla sezione 8 di `docs/audit/codice.md`, il rapporto scritto leggendo il commit `2084922` su
Linux, senza eseguire nulla. "Prima" è il commit `05e9616`. Commit: `0683d3c` (C-62), `61e8775` (C-59), `8a1414b` (C-58),
`5a056af` (C-57), `748c7a5` (C-61), `b2ab076` (C-56), `2a6ce9b` (C-50), `9b007b6` (C-55), `83cf803` (C-52), `81d5826`
(C-54), `26fbe2c` (C-60), `64c80cd` (C-53), `44abc71` e `1e6665c` (le misure di C-51, C-63 e C-53), `d361d75` (un
comando in più per il server di `--imap-finto`), più quello di questo esito. Su
quattordici punti: undici corretti (C-50, C-52, C-54, C-55, C-56, C-57, C-58, C-59, C-60, C-61, C-62), uno corretto in
parte con il resto lasciato (C-53), due confermati e lasciati (C-51, C-63). Nessuno è risultato falso per intero; di
C-56 la parte sul 25 ottobre non era come scritta (nessun promemoria perso: suonavano un'ora prima).

Come si è misurato:

- **Dopo**: `BehaviourAuditTests` (`Core/ProvaBozze.Comportamento.cs`, da soli in 4 secondi con `--prova-audit
  risultato.txt comportamento`; il nome `difetti` era già del blocco 5), 29 controlli e quattro righe di sola misura. Solo
  dati finti, i server IMAP e SMTP finti della suite su 127.0.0.1, orologi simulati.
- **Prima**: ogni controllo di un punto corretto è stato lanciato almeno una volta con la sola correzione spenta a mano
  (senza commit) e visto cadere: C-50 (le righe e la lettura del sorgente; il primo controllo, il rifiuto del server,
  non dipende dalla correzione), C-52 (2), C-53, C-54 (2), C-55 (2), C-56 (5), C-57, C-58, C-59 (i due che leggono orari;
  quello che legge i sorgenti no), C-60, C-61 (tre su quattro: il rifiuto dopo DATA passa prima e dopo), C-62 (4). Dove
  si poteva il "prima" è dentro il controllo stesso: per C-56 una copia fedele del codice di prima
  (`BehOldReminders`) gira sugli stessi orologi finti accanto a quello nuovo.
- **Il server SMTP finto** ha tre interruttori nuovi: `DropAfterData` (riceve tutto il messaggio, lo tiene e chiude
  senza rispondere), `DropOnce` (chiude a un comando, per esempio MAIL) e `RefuseData` (risponde 554 dopo DATA).
- **Le finestre**: nessuna prova le crea (la suite gira senza l'applicazione di WPF, e `MainWindow` e `ComposeWindow`
  vogliono le sue risorse). Delle correzioni che stanno nelle finestre si prova la funzione che decide
  (`MessageRows.RestoreFailed`, `ActionQueue.Take`, `Outbox.Forget`, `ComposeWindow.FollowsFrom`,
  `MainWindow.UndoStartedText` e `OutboxRemovalText`) e si legge il sorgente per vedere che la finestra la chiami. I passi a
  mano sono in "Non verificato".
- Nessun avvio della versione di prova, nessuna rete fuori dal PC, i dati veri e il registro vero mai letti.

### Punti del rapporto

- **C-50** (Sposta, Archivia, Elimina non riusciti: le righe spariscono e non tornano). CONFERMATO, CORRETTO (`2a6ce9b`).
  - Sul server IMAP finto un `UID MOVE` rifiutato (NO): `ImapProvider.MoveAsync` solleva una `MailException` che non è
    "non in linea", quindi `RunOrQueueAsync` prende il ramo dell'avviso "Operazione non riuscita" e restituisce false; il
    messaggio è ancora nella Posta in arrivo. Nel codice di prima quel ramo metteva solo `ok = false`.
  - Ora `MoveAsync` e `DeleteAsync` raccolgono i messaggi rifiutati per account e, finito il giro, `PutBackFailed` li
    rimette nell'elenco con `MessageRows.RestoreFailed`: solo le righe di quei messaggi, al posto giusto anche quando
    altre righe tolte insieme non tornano (un altro account, riuscito), e una conversazione chiusa torna intera. Solo se
    l'elenco è ancora quello di prima (`listVersion`); altrimenti una riga nel registro.
  - Vale anche per l'eliminazione definitiva non riuscita, e per i messaggi da eliminare per sempre che non partono
    perché lo spostamento nel Cestino dello stesso account è stato rifiutato: prima sparivano anche quelli.
  - Controllo: di quattro righe eliminate in due account (`a1 b1 c1(3) b2 a2 a3`), con il primo account che rifiuta,
    tornano la conversazione e `a2` (`a1 c1(3) a2 a3`); prima restavano fuori tutte (`a1 a3`).
  - Le righe tornate non si scelgono e la selezione resta com'è. Niente con la coda delle azioni (B-13): un rifiuto non
    passa dalla coda.
- **C-51** (regole di Missiva: un errore a metà fa saltare per sempre anche i messaggi mai toccati). CONFERMATO, LASCIATO.
  - Misura nella suite: tre messaggi nuovi, una regola "segna come letto e sposta", il server che rifiuta il primo `UID
    STORE`. Al primo controllo `ImapCommandException`, spostati 0 di 3; al controllo dopo nessun messaggio da guardare,
    0 comandi di scrittura, spostati 0 di 3.
  - Non basta togliere i messaggi da `RulesSeen`: prima di agire avanza anche il segno di dove le regole sono arrivate
    (`Rules.marks`), e il controllo dopo non li chiede nemmeno. E riprovare tutto ripete le azioni già fatte: una copia
    diventerebbe due, e un rifiuto che non passa si ripeterebbe a ogni controllo.
  - La correzione giusta è un lavoro a parte: ogni gruppo di azioni per conto suo, l'esito con gli errori invece
    dell'eccezione (anche per "Esegui ora"), e il segno che non avanza oltre i messaggi non finiti. Vedi "Scelte per l'autore".
- **C-52** ("Nessuna firma" scelta a mano annullata cambiando "Da"). CONFERMATO nel codice, CORRETTO (`83cf803`).
  - Prima: dopo Firma, Nessuna firma, `signature` è null e `ChangeSignature` metteva la firma predefinita del nuovo
    indirizzo.
  - Ora il pulsante Firma ricorda la scelta (`noSignature`) e il cambio del Da la chiede (`ComposeWindow.FollowsFrom`).
    Scegliendo poi una firma la scelta si dimentica. Il parametro `name` di `ReplaceSignature`, mai usato, è tolto.
  - La parte SOSPETTA del rapporto, letta: una firma scelta dal pulsante diventa "di Missiva" (`PutSignature`), quindi
    cambiando il Da, se non è stata toccata, lascia il posto alla firma predefinita dell'altro indirizzo. Lasciato così:
    le firme sono dell'indirizzo. Vedi "Scelte per l'autore".
- **C-53** (account rimosso o cambiato: i provider si chiudono sotto le richieste in corso). SOSPETTO nel rapporto,
  CONFERMATO e più pesante. CORRETTO in parte (`64c80cd`).
  - Misura prima, sul server IMAP finto con `EXAMINE` lento: un `ImapProvider` chiuso mentre una sua richiesta è in
    corso. Il LOGOUT partiva in mezzo al comando, e la richiesta restava ferma fino al tempo scaduto di MailKit: 60.017
    ms, poi `TimeoutException` (un giro solo, prima di mettere alla prova un tetto di 5 secondi). Con Gmail lo stesso:
    ancora ferma dopo 5 secondi (il suo tempo scaduto non è stato aspettato).
  - Ora la chiusura di una connessione (`Connection.Dispose`, IMAP e Gmail) aspetta la richiesta in corso su quella
    connessione, al più cinque secondi, poi fa il LOGOUT com'era. Chi chiude non aspetta: è tutto in sottofondo, come
    prima. Nel controllo la richiesta finisce in 0,6 secondi senza errori, per IMAP e per Gmail.
  - LASCIATO: il provider usato ancora dopo la chiusura. Vedi "Trovato in più".
  - LASCIATO: un annullamento per provider. Una richiesta più lunga di cinque secondi viene ancora tagliata.
- **C-54** (Rimuovi account non svuota la Posta in uscita di quell'account). CONFERMATO nel codice, CORRETTO (`81d5826`).
  - Ora la domanda di Rimuovi account dice quanti messaggi della Posta in uscita non partiranno
    (`MainWindow.OutboxRemovalText`); dopo la conferma e prima di chiudere i provider le loro finestre si riaprono
    (`LeaveOutbox`, con "Non inviato: l'account è stato rimosso da Missiva. Copia il testo, se ti serve.") e
    `Outbox.Forget` toglie quello che resta. Vale anche per le caselle condivise che escono con l'account.
  - La finestra riaperta tiene i provider di prima, chiusi: vedi "Trovato in più".
- **C-55** (Annulla di uno spostamento in coda partito solo in parte). CONFERMATO, CORRETTO (`9b007b6`).
  - Come può succedere, guardando B-13: una ripetizione prende tutte le azioni dell'account, quindi mentre gira Annulla
    non ne toglie nessuna. Il caso vero è dopo: tre spostamenti uno per messaggio (Graph), il primo riesce, la rete manca
    di nuovo, gli altri due restano in coda. Lì `ActionQueue.Remove` ne toglieva due su tre e Annulla li dava tutti per
    spostati, senza rimetterli nell'elenco.
  - Ora `ActionQueue.Take` dice quali messaggi sono usciti dalla coda: quelli tornano nell'elenco, l'avviso dice quanti
    erano già partiti (`MainWindow.UndoStartedText`) e poi l'elenco si rilegge. `Remove` resta, con lo stesso risultato
    di prima.
- **C-56** (promemoria del calendario persi quando la lettura non riesce, e nell'ora del 25 ottobre). La prima parte
  CONFERMATA, e più larga; la seconda non come scritta. CORRETTO (`b2ab076`).
  - Prima, con il codice di prima sugli orologi finti: Missiva avviata alle 08:55 senza rete, che torna alle 09:05; il
    promemoria delle 09:00 per la riunione delle 09:15 non suonava mai.
  - Più largo del rapporto: non serve che la lettura fallisca. PC fermo da venerdì alle 18:00 a lunedì alle 08:55, con la
    rete: la riunione di lunedì alle 09:00 non era fra gli eventi letti venerdì (oggi e domani), e alla riapertura gli
    eventi appena letti entravano solo con il promemoria dell'ultimo minuto. Quello delle 08:45 non suonava; suonava
    quello di sabato.
  - Il 25 ottobre 2026: nessun promemoria perso né doppio. Un promemoria alle 02:55 per una riunione alle 03:10 suonava
    alla prima delle due 02:55, 75 minuti prima invece di 15.
  - Ora `CalendarReminders` (`UI/CalendarReminders.cs`): per account, fin dove i promemoria sono stati guardati avendo
    gli eventi di quel momento; quell'ora avanza solo finché gli eventi letti coprono adesso. Alla prima lettura riuscita
    i promemoria scaduti nel buco suonano, in ritardo. Ognuno una volta sola (gli ultimi 2.000 mostrati). I confronti
    sono fra istanti (UTC), e anche l'ora della prossima lettura.
  - Un account senza il consenso al calendario non accumula arretrati: dato il consenso alle 15:00 non suonano insieme i
    promemoria della mattina.
  - `MainWindow.Upcoming`, provata da B-26, resta e passa da lì. Trenta giorni simulati: al più 26 eventi tenuti, 358
    promemoria una volta sola.
  - Non fatto: "era alle ..." nella notifica di un promemoria in ritardo, e il tetto di 30 minuti proposto. Chi riapre
    il PC il lunedì trovava già prima i promemoria del sabato: è lo stesso comportamento, esteso. Vedi "Scelte per l'autore".
- **C-57** (durate misurate con `DateTime.Now`). CONFERMATO, CORRETTO (`5a056af`).
  - Misura: il 25 ottobre 2026 fra le 02:50 e le 02:10 di venti minuti dopo l'orologio dice -40 minuti; il 28 marzo 2027
    fra le 01:50 e le 03:10 di venti minuti dopo ne dice 80.
  - In UTC i punti del rapporto: rete assente da meno di un minuto, ricerca IMAP riusata, finestra ferma, copia degli
    ultimi 30 giorni e raccolta a riposo, pannello dei filtri, eco dell'IDLE nel registro, copia dei contatti.
  - Un controllo legge i sorgenti: nessuna differenza fra due `DateTime.Now` (resta la riga "finestra pronta" dell'avvio).
- **C-58** (promemoria del contrassegno cancellato prima di controllare l'account). CONFERMATO l'ordine, CORRETTO
  (`8a1414b`).
  - Il caso vero resta raro, come dice il rapporto: all'avvio gli account sono già nell'elenco quando parte il primo
    controllo (`LoadAccountsAsync` li crea prima del suo primo `await`). Serve una voce di un account che non c'è.
  - Ora `MainWindow.TakeDueReminders` toglie il promemoria solo alle voci degli account che ci sono.
- **C-59** (orari in un fuso estero a un'ora che non esiste). CONFERMATO, CORRETTO (`61e8775`).
  - Prima: un .ics con `DTSTART;TZID=America/New_York:20260308T023000` non dava nessun invito; `Calendars.LocalTime`
    con lo stesso orario in "Eastern Standard Time" sollevava `ArgumentException`.
  - Ora `Calendars.FromZone`: l'ora che non esiste vale con la differenza da UTC di prima del salto (RFC 5545), cioè
    spostata avanti del salto. La usano gli .ics, il calendario di Graph e gli inviti di Exchange.
  - Le altre ore non cambiano: 70.074 mezz'ore del 2026 in quattro fusi uguali alla conversione di .NET.
  - Lasciato: un valore più corto di 15 caratteri. Un invito con un orario che non si legge resta senza scheda.
- **C-60** (la finestra "una sola cartella" non riceve i messaggi nuovi). SOSPETTO nel rapporto, CONFERMATO nel codice,
  CORRETTO (`26fbe2c`). Non provato a finestra.
  - Letto: la finestra di una sola cartella esce da `StartAsync` prima dei timer, e `App` parla solo alla finestra
    principale (6 chiamate). Non ha un suo aggiornamento.
  - Ora la principale passa alle sue finestre di una cartella (`FolderWindows`) i messaggi nuovi, le righe tolte dalle
    regole, i segni cambiati altrove e la rilettura dopo un avviso dell'IDLE.
  - Non come proposto: `AddIncoming` non si chiama sulle altre finestre. Cambia anche i non letti, la copia locale e i
    conteggi da rifare, una volta per finestra. Le altre finestre ricevono solo le righe (`ShowIncoming`), copie loro.
- **C-61** (invii pianificati: un errore dopo il DATA può far ripartire il messaggio). SOSPETTO nel rapporto, CONFERMATO.
  CORRETTO (`748c7a5`).
  - `SendDraftAsync` letto riga per riga, Gmail e IMAP: dopo il 250 tutto è assorbito, come dice il commento. Fra la fine
    del messaggio e il 250 no: l'errore esce com'è (`SmtpProtocolException`, che `Transient` conta come rete).
  - Prima, con il server SMTP finto che tiene il messaggio e chiude senza rispondere: 3 copie in tre minuti, una per
    controllo, senza fine.
  - Ora `MimeOptions.SendAsync` segue quanto del messaggio è stato consegnato (`ITransferProgress` di MailKit). Se la
    connessione cade, o il tempo scade, a messaggio consegnato per intero e senza risposta, l'errore è
    `MailException.MaybeSent`: "non si sa se è partito".
  - L'invio pianificato esce dall'elenco con un avviso (forse è partito, Missiva non riprova, la bozza è nelle Bozze).
    Una copia sola in tre minuti.
  - Lo stesso errore non conta come "non in linea" (`Network.IsOffline`): Invia non mette il messaggio nella Posta in
    uscita, che al ritorno della rete lo ripeterebbe. La finestra dell'errore si intitola "Invio interrotto".
  - Come prima: la connessione che cade prima del messaggio si riprova il minuto dopo; un rifiuto del server dopo DATA
    (554) è un rifiuto.
  - È la scelta fra un messaggio forse doppio e uno forse non partito. Vedi "Scelte fatte qui" e "Scelte per l'autore".
- **C-62** (filtri della ricerca: virgolette, parentesi e date senza anno). CONFERMATO, CORRETTO (`0683d3c`).
  - Prima: `da:"Mario fattura` cercava da "Mario fattura" senza parole; un valore con una virgoletta dentro, riscritto,
    si mangiava il filtro dopo; per Gmail partiva `from:(Rossi (Studio)`; `dal:25/12` scritto a gennaio era dicembre
    dell'anno in corso.
  - Ora una virgoletta mai chiusa non tiene insieme nulla; i valori dei filtri non tengono virgolette; per Gmail vanno
    senza parentesi; una data senza anno è l'ultima volta che quel giorno c'è stato.
- **C-63** (il colore del testo secondario non è lo stesso dappertutto). CONFERMATO l'uso misto, LASCIATO.
  - Contato nei sorgenti: 24 punti con il pennello di Fluent, 44 con quello di Missiva (#616161 nel chiaro, #ADADAD nello
    scuro), come dice il rapporto. Nella riga disegnata la graffetta usa Fluent, anteprima e data Missiva.
  - Quali siano voluti si giudica a finestra, e cambia ciò che si vede. Vedi "Scelte per l'autore".

### Trovato in più

- **Un provider chiuso non si riapre** (misura nella suite). Dopo Gestisci account, Aggiungi account o Rimuovi account
  tutti i provider si chiudono e se ne creano di nuovi; una finestra di scrittura aperta prima tiene quelli di prima. Su
  un provider chiuso l'elenco e il salvataggio della bozza danno `ObjectDisposedException`, per IMAP e per Gmail. Un
  messaggio nuovo inviato da un provider IMAP chiuso: nessun errore, arriva al server SMTP (ogni invio apre un
  collegamento suo), 0 copie in Posta inviata, e nessuno lo dice. Non corretto: vedi "Scelte per l'autore".
- **Il promemoria del lunedì mattina** si perdeva anche con la rete (C-56).
- **Il salvataggio automatico delle bozze e l'età dei file temporanei** misuravano anche loro con l'orologio. Dal conto
  di C-57, non provato con l'ora del PC cambiata: la notte del ritorno all'ora solare una bozza poteva restare un'ora
  senza salvarsi; a fine marzo un allegato aperto da cinque minuti risultava vecchio di un'ora. Corretti con C-57.
- **Elimina con messaggi già nel Cestino** (letto): quando lo spostamento degli altri era rifiutato, quelli da eliminare
  per sempre non partivano ma le loro righe sparivano. Corretto con C-50.
- **Un errore dopo DATA con un codice di rete** (letto; per esempio 10060, tempo scaduto) mandava il messaggio nella
  Posta in uscita, che lo ripeteva al ritorno della rete: lo stesso difetto di C-61, per Invia. Corretto con C-61.
- Gli heredoc di Bash su questo PC dimezzano le barre rovesciate anche dentro uno script Python: confermato due volte.

### Controlli e suite

- `BehaviourAuditTests`, 29 controlli: C-50 (3), C-52 (2), C-53, C-54 (2), C-55 (2), C-56 (5), C-57, C-58, C-59 (3),
  C-60, C-61 (4), C-62 (4). Quattro righe di sola misura: C-51, C-53 (il provider usato dopo la chiusura, e un invio da
  lì), C-57, C-63.
  Se il fuso del PC non ha il ritorno all'ora solare il 25 ottobre 2026, il controllo di C-56 su quella notte dice "non
  misurato". I controlli che leggono i sorgenti si saltano fuori dalla cartella dei sorgenti.
- Nessun controllo di prima è cambiato. `LongRunAuditTests` (B-26) passa uguale con `MainWindow.Upcoming` che ora va da
  `CalendarReminders.Upcoming`.
- Suite completa: 974 controlli, tutto bene, in 160 secondi, sul codice di `d361d75` (quello di questo esito: dopo
  cambiano solo i documenti). Due giri prima: sul codice di `44abc71` 974, tutto bene, in 167 secondi; sul codice di
  `1e6665c` 974, tutto bene, in 159 secondi. Prima di cominciare, sul codice di `05e9616`, la suite non è stata
  rilanciata: il numero di partenza, 945, è quello del gruppo precedente. Il controllo "S-01, un disegno che non
  finisce" del blocco 2, dato per instabile, non è caduto in nessuno dei tre giri.
- Compilazione Debug: 0 errori, nessun avviso nuovo (gli stessi dieci di prima, confrontati senza il numero di riga
  dopo una compilazione da capo prima e dopo). La configurazione Release compila (`dotnet build -c Release`): 0
  errori; i suoi avvisi sono quelli della Debug che non stanno nelle prove più `RulesView.StartAccount` (CS0649), che il
  rapporto dà per la sola Release; non confrontati con una Release di prima. `prova-release` non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Elimina, Archivia o Sposta rifiutati dal server: dopo l'avviso le righe tornano nell'elenco.
- Annulla di uno spostamento fatto senza rete e partito solo in parte: tornano le righe dei messaggi non partiti, e
  l'avviso dice quanti lo erano.
- Nella scrittura "Nessuna firma" resta anche cambiando il Da.
- Rimuovi account con messaggi nella Posta in uscita: la domanda lo dice e le loro finestre si riaprono.
- Promemoria del calendario: dopo un avvio senza rete, o riaprendo il PC dopo giorni, suonano in ritardo quelli scaduti
  intanto. Riaprendo il lunedì possono arrivarne diversi insieme.
- Una seconda finestra aperta sulla Posta in arrivo mostra i messaggi nuovi quando arrivano.
- Un invio pianificato interrotto proprio mentre il server riceveva il messaggio non riparte da solo: un avviso dice
  che forse è partito. Lo stesso con Invia: una finestra "Invio interrotto" invece della Posta in uscita.
- Ricerca: `dal:25/12` scritto a gennaio è il dicembre appena passato; una virgoletta dimenticata non cambia la ricerca.
- Un invito dall'estero a un'ora che lì non esiste ha la sua scheda.
- La notte del cambio dell'ora nulla aspetta un'ora in più o in meno.

### Scelte fatte qui, da rivedere

- **Un invio forse partito non si ripete** (C-61). Vale quando tutto il messaggio era stato consegnato e la risposta non
  è arrivata. Può voler dire un messaggio pianificato non partito, con un avviso; prima voleva dire una copia al minuto.
- **"Consegnato per intero"** lo dice MailKit quando ha scritto tutto il messaggio sulla connessione, non quando il
  server l'ha letto: un messaggio piccolo conta come consegnato appena dopo DATA. In dubbio vale "forse è partito".
- **I promemoria in ritardo non hanno un tetto** (C-56): suonano tutti quelli scaduti dall'avvio o dall'ultima lettura
  che copriva quel momento, anche di riunioni finite.
- **Un promemoria in un'ora che c'è due volte** vale per la seconda, quella solare.
- **Le righe tornate dopo un rifiuto non si scelgono** (C-50), e tornano tutte quelle del gruppo rifiutato, anche se il
  server ne avesse spostata una parte: la rilettura dopo le corregge.
- **La chiusura di un provider aspetta al più cinque secondi** la richiesta in corso (C-53): scelto, non misurato.
- **La finestra riaperta da Rimuovi account** dice solo di copiare il testo (C-54): i suoi provider sono chiusi.
- **Le finestre di una sola cartella ricevono solo le righe** (C-60): i loro non letti, se si vedono, non cambiano.
- **Una data senza anno** guarda indietro fino a otto anni (per il 29 febbraio).
- **I valori dei filtri perdono le virgolette**, tutte: `da:"Rossi "Studio" Srl"` cerca Rossi Studio Srl.

### Scelte per l'autore

- **Invio interrotto mentre il server riceveva il messaggio** (C-61): Missiva deve fermarsi e dirlo, o riprovare da
  sola? Proposta: fermarsi, com'è ora. L'alternativa è riprovare una volta sola e poi fermarsi: un doppione possibile,
  mai una copia al minuto.
- **Regole dopo un errore a metà** (C-51): vuoi il lavoro a parte (ogni gruppo di azioni per conto suo, l'errore
  nell'esito, i messaggi non finiti riprovati al controllo dopo)? Proposta: sì, è mezza giornata con le sue prove.
- **Una finestra di scrittura aperta mentre gli account si ricaricano** (trovato in più): deve passare al provider nuovo
  dell'account? Proposta: sì, al prossimo giro; oggi una bozza non si salva più e, per IMAP, il messaggio parte senza
  copia in Posta inviata.
- **Colore del testo secondario** (C-63): dei 24 punti con il grigio di Fluent, quali vuoi con quello di Missiva?
  Proposta: tutti, cominciando dalla graffetta della riga, guardando una foto per tema prima e dopo.
- **Promemoria in ritardo** (C-56): vuoi un tetto (per esempio solo le riunioni non ancora finite) e "era alle ..." nel
  testo? Proposta: il tetto sì, se il lunedì mattina ne arrivano troppi.
- **Una firma scelta a mano e poi un altro Da** (C-52): deve restare quella scelta? Proposta: no, com'è ora: le firme
  sono dell'indirizzo, e solo "Nessuna firma" resta.

### Non verificato

- **Niente a finestra.** Passi per l'autore, dopo l'installazione:
  1. Nuovo messaggio, Firma, Nessuna firma, poi un altro indirizzo nel Da: il testo resta senza firma.
  2. Apri la Posta in arrivo in un'altra finestra e fatti arrivare un messaggio: compare in tutte e due.
  3. Senza rete: Invia (va nella Posta in uscita), poi Rimuovi account: la domanda lo dice, la scrittura si riapre, la
     barra sparisce.
  4. Un evento con il promemoria fra cinque minuti, poi chiudi Missiva, stacca la rete, riaprila, aspetta dieci minuti e
     riattacca la rete: il promemoria arriva in ritardo, una volta.
  5. Cerca `dal:25/12` e `da:"mario`: risultati, non un elenco vuoto.
- **C-50**: `MoveAsync` e `DeleteAsync` a finestra. Per provarlo con i dati finti il server di `--imap-finto` ha un
  comando in più (`d361d75`, scritto e compilato, mai eseguito): `prova-finta.ps1 -Server 1 -Do "rifiuta UID MOVE"`, poi
  Canc su un messaggio: l'avviso "Operazione non riuscita", e la riga torna. Un 400 o un 403 di Graph: stesso ramo, non
  provato.
- **C-53**: un server vero; Gmail oltre i cinque secondi; il tempo scaduto di Gmail.
- **C-54**: che cosa fa la finestra riaperta se si preme Invia o Salva bozza (i provider sono chiusi).
- **C-55**: `UndoAsync` a finestra, e Graph vero.
- **C-56**: `CheckRemindersAsync` a finestra con un calendario vero; la notte del 25 ottobre dal vero.
- **C-52**: la finestra di scrittura (qui la funzione che decide, e il sorgente).
- **C-57**: nessuna prova con l'ora del PC cambiata.
- **C-58**: `CheckFlagReminders` a finestra.
- **C-59**: un invito vero dall'estero; Graph con un orario in un altro fuso (il rapporto stesso lo dà per SOSPETTO).
- **C-60**: tutto: il passaggio è letto dal sorgente.
- **C-61**: un server SMTP vero che tarda a rispondere; i server con CHUNKING (`BDAT` invece di `DATA`: il server finto
  non lo offre); la finestra "Invio interrotto".
- **C-62**: Gmail vero con i valori senza parentesi; il pannello dei filtri.
- **C-63**: le foto per tema.
- **La versione finale**: tutto è di Debug. Niente è stato installato.

## Audit dell'11 ottobre, codice, secondo gruppo: robustezza e avvisi del compilatore (C-24..C-37)

Fatto il 12 ottobre 2026 sulle sezioni 4 e 5 di `docs/audit/codice.md`, più due punti trovati dai gruppi di prima: X-1
(una scrittura aperta mentre gli account si ricaricano, dal primo gruppo del codice) e X-2 (`missiva-errori.log` nella
cartella vera dei dati, dal terzo gruppo della sicurezza). "Prima" è il commit `d3f8fbd`. Commit: `fd59ee1` (X-2),
`daba973` (C-24), `bf59f0d` (C-25 e C-26), `db8fc44` (C-27), `2dd436f` (C-29), `d4f8a56` (C-30), `d16f6c6` (C-32),
`ba3060b` (C-35), `45aac8f` (C-34), `08cbaa8` (C-33, con i controlli di C-34 e C-35), `fee4ac6` (la misura di C-36),
`880948a` (la misura di C-31), `56b4d4e` (X-1), `cc836be` (C-37), più quello di questo esito. Su sedici punti: dieci
corretti (C-24, C-25, C-26, C-27, C-29, C-32, C-34, C-35, X-1, X-2), tre corretti in parte con il resto lasciato (C-30,
C-33, C-37), uno confermato e lasciato (C-28), uno confermato nel codice ma senza il danno descritto, lasciato con la
misura (C-31), uno falso (C-36).

Come si è misurato:

- **Dopo**: `RobustnessAuditTests` (`Core/ProvaBozze.Robustezza.cs`, da soli in 12 secondi con `--prova-audit
  risultato.txt robustezza`), 25 controlli e una riga di sola misura. Quasi tutti fanno fallire un passo e guardano che
  cosa resta: un lavoro che solleva, un file tenuto aperto in una cartella temporanea, il processo dei PDF vero lasciato
  senza risposta, i server IMAP e SMTP finti, un Google finto.
- **Prima**: ogni controllo di un punto corretto è stato lanciato almeno una volta con la sola correzione spenta a mano
  (senza commit) e visto cadere: X-2 (2), C-24, C-25 (2), C-26 (2), C-27 (2: senza lucchetto tre giri su tre), C-29
  (qui il controllo è nato prima della correzione: due giri), C-30 (nato prima della correzione: caduto due giri su
  tre), C-32 (due su tre: il terzo, "togliere quello che non c'è", passa prima e dopo), C-33 (quello del sorgente),
  C-34 (2), C-35, C-37, X-1 (3). Non hanno un "prima" i controlli che sono misure di com'è: C-31, C-36 e quello di C-33
  sul gestore degli errori.
- **Il registro in una cartella temporanea**: `Log.TestDir` (solo Debug) sposta registro e file degli errori di
  scrittura. Le prove che guardano una riga del registro la scrivono lì, non nel registro della cartella di prova.
- **Le finestre**: nessuna prova crea le finestre di Missiva. Delle correzioni che stanno lì si prova il pezzo che decide
  (`Log.GuardAsync`, `ComposeWindow.Current`) e si legge il sorgente per vedere che la finestra lo chiami; se il
  sorgente c'è e il metodo non si trova, il controllo cade. Per C-36 tre finestre vere di WPF con WebView2, vuote, fuori
  dallo schermo e mai in primo piano.
- **Un errore che chiude il processo** (C-24) non si provoca dentro la suite: c'è un gruppo solo a mano, `--prova-audit
  risultato.txt caduta`, lanciato due volte con `MISSIVA_DATA` in una cartella temporanea.
- Nessun avvio della versione di prova, nessuna rete fuori dal PC, i dati veri e il registro vero mai letti.

### Punti del rapporto

- **C-24** (nessun gestore per gli errori fuori dall'interfaccia). CONFERMATO, CORRETTO (`daba973`).
  - Prima, con i gestori spenti: un Task scartato che fallisce non lascia nessuna riga dopo 150 raccolte forzate; un
    errore su un altro thread chiude il processo (uscita 0xE0434352) senza che il registro nasca nemmeno.
  - Ora `Program.Main` chiama `Log.WatchUnhandled`. Un Task fallito che nessuno aspettava: "ERRORE (lavoro in sottofondo
    che nessuno aspettava)" con l'errore e la traccia, quando .NET raccoglie il Task (può arrivare tardi). Un errore su
    un altro thread: "ERRORE (errore non gestito fuori dall'interfaccia, Missiva si chiude)", poi il processo si chiude
    come prima. Visto nel registro della cartella temporanea, con la traccia fino al metodo.
  - Solo il registro: nessun avviso a schermo. L'avviso dell'errore imprevisto dell'interfaccia non è cambiato. Non per
    il processo dei PDF né per gli strumenti di prova, che escono prima.
  - Quali lavori falliscono davvero in silenzio (SOSPETTO nel rapporto): la suite registra gli stessi gestori
    all'inizio e alla fine li conta. Nella suite intera: 3 al primo giro (due di un doppio di prova di questo gruppo, poi
    corretto, e un errore finto di una prova di prima), 1 al secondo (quell'errore finto). Nessuno viene dal codice
    di Missiva.
- **C-25** (avvio: se il caricamento degli account fallisce, chi li aspetta resta appeso). CONFERMATO nel codice,
  CORRETTO (`bf59f0d`). Non provato a finestra.
  - Letto: `accountsListed` e `accountsReady` si chiudevano solo in fondo ai loro tratti, senza `finally`. Li aspettano
    l'azione di una notifica, il link mailto:, il clic su una notifica, il promemoria di un contrassegno.
  - Ora `LoadAccountsAsync` li chiude in un `finally` (il lavoro è in `ListAccountsAsync`, uguale a prima). Chi aspettava
    riparte, trova gli account che ci sono e, se il suo manca, non fa nulla, come già faceva.
  - L'avvio nascosto, che nessuno aspetta, gira dentro `Log.GuardAsync`: il suo errore è nel registro subito. Con la
    finestra lo prendeva già il gestore dell'interfaccia.
  - Che un errore lì capiti davvero resta SOSPETTO, come nel rapporto: non se n'è provocato nessuno.
- **C-26** (azioni delle notifiche: l'uscita si salta se l'azione fallisce). CONFERMATO nel codice, CORRETTO
  (`bf59f0d`). Non provato a finestra.
  - `Log.GuardAsync`, nuovo: fa girare un lavoro, scrive il suo errore nel registro con il contesto e non lo lascia
    uscire. Provato con un lavoro che solleva prima e dopo il primo `await`.
  - `CountActionAsync` lo usa: il conto delle azioni scende e il controllo "finite tutte, esci" si fa anche dopo un
    errore. Raro, come dice il rapporto: i metodi chiamati hanno già un `catch` loro.
- **C-27** (`RuleWindow` chiama il provider da un thread del pool; `CategoryPalette.Known` senza lucchetto). CONFERMATO,
  CORRETTO (`db8fc44`).
  - La finestra della regola chiede le categorie con una chiamata asincrona come le altre (`LoadCategoriesAsync`), sul
    thread dell'interfaccia. Il `Task.Run` c'era dal commit che ha portato le regole, senza un motivo scritto.
  - `Known`: un thread aggiunge 120.000 account e un altro chiede il colore di una categoria che c'è da prima. Senza
    lucchetto, in tre giri, 306, 357 e 212 risposte sbagliate su 1,7-1,9 milioni (il dizionario, mentre si allarga,
    risponde come se la categoria non ci fosse); con il lucchetto zero.
  - Gli altri effetti temuti (Gmail che scrive le sue etichette dal pool, la finestra di Microsoft chiesta dal thread
    sbagliato) non sono stati provocati: spariscono con il `Task.Run`.
- **C-28** (un solo lucchetto dei token Microsoft, tenuto con la finestra di accesso aperta). CONFERMATO nel codice,
  LASCIATO.
  - Letto: `TokenAsync`, `ExtraTokenAsync`, `SignInAgainAsync` e il riscaldamento dell'avvio prendono tutti `TokenGate`,
    e la finestra account di Windows (`AcquireTokenInteractive`) si aspetta con il lucchetto in mano.
  - Chi aspetta: solo le richieste di un altro account Microsoft il cui token è scaduto proprio mentre la finestra è
    aperta. Un token ancora valido non passa dal lucchetto.
  - Non corretto: il lucchetto unico è lì apposta (due finestre insieme, due app MSAL sullo stesso file), il rapporto
    stesso dà rischio medio, e MSAL non ha un doppio di prova nella suite: senza due account veri non si prova né il
    difetto né la correzione. Vedi "Scelte per l'autore".
- **C-29** (processo dei PDF: dopo il tempo scaduto la chiusura e la richiesta in corso si incrociano). SOSPETTO nel
  rapporto, CONFERMATO. CORRETTO (`2dd436f`).
  - Misura prima, sul processo vero nel suo AppContainer, con una lettura lasciata senza risposta e il tempo di una
    richiesta accorciato a 0,8 s: la richiesta dopo partiva a 846 ms (841 al secondo giro), quella di prima finiva a
    1.340 (1.324). Per mezzo secondo due richieste insieme sugli stessi campi.
  - Nella prova la richiesta di prima ci mette apposta mezzo secondo a uscire. Nel codice vero, sbloccata la lettura,
    esce subito: lo spazio vero è stretto. Resta largo se è `Start` a fermarsi.
  - Ora, chiuso il processo, `RunAsync` aspetta la richiesta di prima, al più cinque secondi, prima di lasciare il
    posto. Nel controllo: prima finisce a 1.330 ms, dopo parte a 1.340, e la seconda anteprima si carica.
  - Per la prova il tempo di una richiesta è una proprietà (`RequestTimeout`, 15 secondi come prima) e c'è
    `TestRequestAsync`. Le 13 prove dei PDF di prima passano uguali.
- **C-30** (contatti: lucchetto tenuto durante DPAPI e la scrittura). Due parti.
  - Il lucchetto durante cifratura e file: è B-14, già misurato e lasciato (10 ms con 200 contatti, 30-46 con 4.500).
    Vale quell'esito: LASCIATO, non rimisurato.
  - `ForSuggestions` che copia l'elenco fuori dal lucchetto, chiamata anche da `AddressBook.Preload` in `Task.Run`:
    CONFERMATO, CORRETTO (`d4f8a56`). Con un thread che aggiunge 3.000 contatti e uno che chiede: `ArgumentException`
    dalla copia in due giri su tre. Con la copia sotto il lucchetto zero errori in tre giri (6.000 contatti).
- **C-31** (CancellationToken non passati). CONFERMATO che non passano; l'effetto descritto non c'è. LASCIATO, con la
  misura (`880948a`).
  - Il rapporto: con la rete lenta, cliccando più messaggi di fila, si accumulano collegamenti che nessuno aspetta più.
  - Misura: davanti al server IMAP finto un collegamento che tarda 600 ms, poi sei messaggi cliccati di fila, ognuno
    che supera quello di prima. Un collegamento solo verso il server. Il primo clic, già partito, lo porta a termine;
    gli altri quattro escono dalla coda (annullati); il sesto arriva a 665 ms su quel collegamento.
  - Con il token passato a `ConnectAsync`, come proposto, ogni clic farebbe ripartire il collegamento da capo: finché
    si clicca non si collega mai. Lasciato com'è.
  - Vero resta questo: se il collegamento non risponde affatto, la richiesta superata tiene il posto fino al tempo
    scaduto di MailKit, e quella dopo aspetta. Ma anche lei, poi, troverebbe la rete ferma.
  - La pulizia dell'HTML di un messaggio superato (`ReadingView`; 75-84 ms per il rapporto, su un thread del pool):
    LASCIATA. Letto: il risultato resta legato al corpo, che il provider tiene in memoria, e tornando su quel
    messaggio non si rifà.
  - Gli altri della lista sono voluti per il rapporto stesso. I commenti "token non passato" non sono stati aggiunti:
    l'analizzatore che li proporrebbe non è acceso nella compilazione.
- **C-32** (`catch` vuoti che nascondono qualcosa). CONFERMATO, CORRETTO (`d16f6c6`).
  - Copie di un account rimosso: in una cartella dei dati temporanea, con tre file tenuti aperti (elenchi, calendario,
    contatti di Outlook), le tre cancellazioni fallivano senza una riga. Ora ognuna scrive la sua; liberati i file le
    copie si tolgono; togliere quello che non c'è non scrive nulla.
  - Revoca di Google: la risposta non si guardava né si chiudeva. Ora, con un Google finto: 200 "revocato"; 400 e rete
    caduta "non revocato", con il motivo e che presso Google può restare valido. Mai il token nel registro.
  - `MicrosoftAuth.SignOutAsync` scrive il suo errore. Non provato: MSAL non ha un doppio di prova.
  - Solo il registro: la finestra di Rimuovi account non dice nulla di più. Vedi "Scelte per l'autore".
- **C-33** (`async void` senza protezione). CONFERMATO che il `try` manca; l'errore però non si perde. CORRETTO in parte
  (`08cbaa8`).
  - Misura: un `async void` che fallisce dopo un `await` su un Dispatcher arriva al gestore degli errori
    dell'interfaccia con la traccia che dice il metodo. In Missiva quel gestore lo scrive nel registro e lo dice una
    volta per sessione. Quindi `Render`, `Do`, `OnInboxEvent` e `NewEventWhenReady` restano come sono: un `try` con un
    messaggio loro cambierebbe che cosa si vede dopo un errore, che è una scelta già in attesa dell'autore (l'errore
    imprevisto che apre una finestra).
  - Lo stato fermo, quello sì: se la bozza o i destinatari fallivano, `recipientsReady` non si chiudeva e Rispondi con
    modello aspettava per sempre. Ora in un `finally`. Letto dal sorgente, non provato a finestra.
- **C-34** (domini.dat non salvato all'uscita). CONFERMATO, CORRETTO (`45aac8f`).
  - Prima: un dominio visto e l'uscita entro cinque secondi, e sul disco non c'era.
  - Ora `SenderLogos.Flush`, chiamata da `App.OnExit` e alla fine della sessione di Windows, scrive subito quello che
    aspettava. Senza niente in attesa non riscrive il file (stessi byte prima e dopo).
- **C-35** (`throw ex` nel riquadro di lettura). CONFERMATO, CORRETTO (`ba3060b`).
  - Ora `throw;`: nel registro di "lettura messaggio" la traccia arriva fino al provider. Il controllo mostra la
    differenza su un errore di prova e legge i sorgenti: nessun `throw ex` rimasto.
- **C-36** (WebView2 delle finestre dei messaggi chiuse). SOSPETTO nel rapporto, FALSO. Nessuna correzione (`fee4ac6`,
  solo il controllo).
  - Misura: tre finestre vere di WPF con il controllo di WebView2 dentro una griglia, chiuse senza chiudere il
    controllo e senza raccolte forzate. Processi di disegno: 1 con una finestra, 4 con tre in più, 1 due secondi dopo
    averle chiuse. Il controllo si chiude con la sua finestra.
  - `App.ThemeChanged` staccato solo dopo `Loaded`: riguarda un riquadro mai mostrato, cioè quello della finestra
    principale avviata nascosta, che vive quanto Missiva. TRASCURABILE, lasciato.
  - Non misurato: il riquadro vero dentro la finestra di un messaggio (vuole le risorse dell'applicazione).
- **C-37** (gli avvisi del compilatore, uno per uno). CONFERMATI tutti, CORRETTI (`cc836be`; CA2200 con C-35). Lasciata
  la proposta di farne errori.
  - Contati da una compilazione da capo (`--no-incremental`). Prima: in Debug 11 avvisi del codice più NU1510; in
    Release 9 più NU1510. Dopo: 0 e 0. NU1510 nella tabella del rapporto non c'è.
  - Gmail, nuova etichetta e nuova cartella (CS8602): difetti veri, come dice il rapporto. Sul server finto che risponde
    OK a CREATE e poi non elenca la cartella: `NullReferenceException` in tutti e due. Ora "Gmail non ha creato
    l'etichetta ...", come IMAP. Nessun colore resta salvato.
  - Gmail, rinomina (CS8604): la cartella che contiene quella da rinominare, se MailKit non la conosce, si chiede al
    server con il suo percorso. Non il primo livello, come fa IMAP: per una cartella annidata sarebbe uno spostamento.
    Solo compilato: il server finto non ha RENAME, e MailKit di solito il genitore lo conosce.
  - Misura degli allegati della bozza, Gmail e IMAP (CS8602): nessun difetto. Un contenuto senza flusso non conta come
    piccolo.
  - `MainWindow`, due CS8604: nessun difetto, come dice il rapporto. Riscritti perché il compilatore li segua.
  - `RulesView.StartAccount` (CS0649, solo Release): è un gancio delle prove, assegnato solo da argomenti della Debug.
    Ora esiste solo in Debug; fuori la pagina si apre sul primo account, come già faceva.
  - Prove: un confronto fra riferimenti scritto come tale (CS0252), e nel server IMAP finto un allegato senza
    contenuto vale zero byte (CS8602).
  - NU1510: `System.Security.Cryptography.ProtectedData` tolto dai pacchetti. Con .NET 10 è nel framework di WPF, nella
    stessa versione (10.0.12). Verificato: cancellato dall'uscita, la compilazione lo ricopia (stessi 59.216 byte); in
    una pubblicazione in un solo file (`dotnet publish`, mai avviata e poi cancellata) il suo nome c'è come quello
    degli altri assembly.
  - Nessun avviso zittito con un pragma o con `!`. L'unico pragma nuovo è in una prova, attorno a un `throw ex` messo
    apposta per il confronto di C-35.
  - Non fatto: gli avvisi di nullità come errori (`WarningsAsErrors`). Vedi "Scelte per l'autore".

### I due punti in più

- **X-1** (una scrittura aperta mentre gli account si ricaricano tiene i provider chiusi). CONFERMATO, CORRETTO
  (`56b4d4e`). Non provato a finestra.
  - Quando succede, letto: Aggiungi account, Aggiungi casella condivisa e Rimuovi account (`LoadAccountsAsync`).
    Gestisci account no: rifà le connessioni dello stesso provider, non lo chiude.
  - Misura prima: elenco e bozza `ObjectDisposedException`, per IMAP e per Gmail. Un messaggio nuovo arrivava al
    server SMTP senza errori, da IMAP (0 copie in Posta inviata) e anche da Gmail.
  - Ora un provider IMAP o Gmail chiuso rifiuta ogni richiesta, anche l'invio, con un errore che dice perché
    (`MailException.Closed`): 0 messaggi arrivati al server SMTP.
  - E la finestra di scrittura, quando il suo provider è chiuso, usa quello nuovo dello stesso account
    (`ComposeWindow.Current`, `IMailProvider.IsClosed`): per il Da, per salvare, per inviare, per togliere la bozza.
    Nella prova l'invio da lì arriva, con la copia in Posta inviata.
  - Un provider aperto non si cambia mai: Graph non si chiude, e la risposta a un .eml resta sul suo provider.
  - Se l'account non c'è più resta quello chiuso: Invia e Salva bozza danno l'errore, con "copia prima il testo".
  - Un messaggio aperto in una finestra sua tiene ancora il provider di prima: dopo il ricarico dà lo stesso errore,
    non più quello di .NET. Le finestre di una sola cartella si chiudono già da sole al ricarico.
- **X-2** (`missiva-errori.log` nella cartella vera dei dati anche dalle versioni di prova). CONFERMATO nel codice,
  CORRETTO (`fd59ee1`).
  - Letto: `Log.Append`, quando il registro non si scrive, annotava sempre in `%LOCALAPPDATA%\Missiva`.
  - Ora il file sta accanto al registro (`Log.FailurePath`). Per la versione installata è la stessa cartella di prima.
  - Il controllo guarda prima solo il percorso; la scrittura si provoca (registro tenuto aperto, in una cartella
    temporanea) soltanto se il percorso sta lì dentro. Con la correzione spenta il primo cade e il secondo dice "non
    provato" senza scrivere: verso la cartella vera non è stata provocata nessuna scrittura.

### Trovato in più

- **La versione di prova e la revoca di Google** (letto, non provato). `prova.ps1` lavora su una copia dei dati veri,
  con la stessa autorizzazione di Google. Lì Rimuovi account su un account Gmail avrebbe chiesto a Google di revocarla
  anche per la Missiva installata. Ora nella versione di prova (`MISSIVA_NO_READ`) va solo nel registro. Con C-32.
- **Gmail chiuso inviava anche lui** (misura di X-1): il gruppo di prima l'aveva visto solo per IMAP.
- **Gestisci account non chiude i provider** (letto): il testo del gruppo di prima lo metteva fra i casi di X-1.
- **Nessun lavoro in sottofondo di Missiva fallisce in silenzio negli scenari della suite** (misura di C-24): l'unico
  Task fallito che nessuno aspettava è un errore finto di una prova. Vale per quello che la suite esercita, non per
  l'uso vero.
- **`ImapProvider.RenameFolderAsync`** (letto): senza genitore noto usa il primo livello, anche per una cartella
  annidata. Non toccato: nessun avviso, e nessun modo di provarlo sul server finto. Vedi "Non verificato".
- Gli heredoc di Bash su questo PC hanno trasformato `'\\n'` in un a capo vero dentro due script Python: corretto a
  mano con Edit, e ricontrollato compilando.
- `Copy-Item` tiene la data del file copiato: rimettere un sorgente da una copia più vecchia non fa ricompilare. Una
  volta la suite del gruppo è girata sull'eseguibile con la correzione ancora spenta; visto dal risultato e rifatta.

### Controlli e suite

- `RobustnessAuditTests`, 25 controlli: X-2 (2), C-24, C-25 (2), C-26 (2), C-27 (2), C-29, C-30, C-31, C-32 (3), C-33
  (2), C-34 (2), C-35, C-36, C-37, X-1 (3). Una riga di sola misura: C-24, i Task falliti che nessuno aspettava.
  I controlli che leggono i sorgenti si saltano fuori dalla cartella dei sorgenti. Se WebView2 non parte in una finestra
  di prova, quello di C-36 dice "non misurato" e non cade.
- Di prima è cambiata solo la riga di misura di C-53 (`BehaviourAuditTests`), che diceva "Lasciato all'autore". Nessun
  controllo di prima è cambiato. Il server IMAP finto ha un interruttore in più, `ForgetCreated`.
- Suite completa: 999 controlli, tutto bene, in 170 secondi, sul codice di `cc836be` (quello di questo esito: dopo
  cambiano solo i documenti). Un giro prima, sul codice di `56b4d4e`: 998, tutto bene, in 170 secondi. Prima di
  cominciare, sul codice di `d3f8fbd`, la suite non è stata rilanciata: il numero di partenza, 974, è quello del gruppo
  precedente. Il controllo "S-01, un disegno che non finisce" del blocco 2 non è caduto in nessuno dei due giri.
- Compilazione da capo sul codice di `cc836be` (`dotnet build --no-incremental`): Debug 0 errori e 0 avvisi, Release 0
  errori e 0 avvisi. Anche la pubblicazione di prova con `-p:MissivaProva=true`, fatta prima dell'ultimo controllo del
  gruppo: 0 avvisi. `prova-release` non è stata ricostruita.

### Che cosa cambia per chi usa Missiva

- Quasi nulla si vede: il registro dice di più. Possono comparire righe nuove, "lavoro in sottofondo che nessuno
  aspettava": sono errori che c'erano già e non lasciavano traccia.
- Una scrittura lasciata aperta mentre aggiungi o rimuovi un account continua a salvare e a inviare. Se l'account della
  scrittura è quello rimosso, Invia dice che il collegamento è stato chiuso.
- Rispondi con modello non resta più senza modello se i destinatari non si riescono a leggere.
- In Gmail una nuova etichetta o cartella che il server non crea dà un errore che lo dice.
- Un dominio visto un attimo prima di uscire non si dimentica.
- Missiva avviata da Windows per un'azione di una notifica esce anche se l'azione fallisce.

### Scelte fatte qui, da rivedere

- **Gli errori presi dai gestori nuovi vanno solo nel registro** (C-24, C-25, C-26): nessun avviso a schermo.
- **La richiesta fermata del processo dei PDF si aspetta al più cinque secondi** (C-29): scelto, non misurato.
- **Il testo dell'errore di un provider chiuso** (X-1): "Il collegamento di ... usato da questa finestra è stato
  chiuso: l'account è stato rimosso da Missiva, o gli account sono stati ricaricati. Chiudi la finestra e riaprila; se
  stavi scrivendo, copia prima il testo."
- **La scrittura cambia provider solo quando il suo è chiuso** (X-1), e sceglie quello nuovo per account.
- **La revoca di Google nella versione di prova non parte** (C-32).
- **Una revoca non riuscita è solo una riga nel registro** (C-32).
- **`ProtectedData` non è più un pacchetto** (C-37): viene dal framework di WPF.
- **`RulesView.StartAccount` esiste solo in Debug** (C-37).
- **In Gmail la cartella che contiene quella da rinominare si chiede al server** (C-37), invece del primo livello.
- **I domini si salvano anche alla fine della sessione di Windows** (C-34), non solo all'uscita.

### Scelte per l'autore

- **Lucchetto dei token Microsoft** (C-28): vuoi un lucchetto per account, con quello unico solo per la finestra di
  accesso? Proposta: no per ora. Serve solo con due account Microsoft e una finestra di accesso lasciata aperta mentre
  scade il token dell'altro, e non si può provare senza i due account veri.
- **Avvisi di nullità come errori** (C-37): ora che sono zero, vuoi che un avviso nuovo fermi la compilazione?
  Proposta: sì, ma solo in Debug (dove lavorano gli agenti), così `installa.ps1` non si ferma mai per questo.
- **Revoca di Google non riuscita** (C-32): vuoi che Rimuovi account lo dica a schermo, con dove toglierla a mano?
  Proposta: sì, una riga nell'avviso dopo la rimozione.
- **Le altre finestre dopo un ricarico degli account** (X-1): un messaggio aperto in una finestra sua deve passare al
  provider nuovo come la scrittura? Proposta: sì, con lo stesso `Current`, al prossimo giro.
- **Token al collegamento IMAP** (C-31): lasciato com'è, per la misura. Proposta: non toccarlo.

### Non verificato

- **Niente a finestra.** Passi per l'autore, dopo l'installazione:
  1. Nuovo messaggio da un account Gmail o IMAP, due righe di testo. Senza chiuderlo aggiungi una casella condivisa o
     un account (o rimuovine un altro). Poi Invia: parte, e la copia è in Posta inviata.
  2. Come sopra, ma rimuovi proprio l'account del Da: Invia dice che il collegamento è stato chiuso.
  3. Tasto destro su un messaggio, Rispondi con modello: il modello entra nel testo.
  4. Apri dieci messaggi in finestre loro e chiudile: `strumenti\memoria.ps1 -Detail` torna al numero di processi di
     WebView2 di prima (C-36, qui misurato solo con finestre di prova).
- **C-24**: la riga di un Task non osservato arriva solo quando .NET lo raccoglie: quanto tardi, nell'uso vero, non si
  sa. La chiusura per un errore su un altro thread: solo con il gruppo a mano, non dentro Missiva con la finestra.
- **C-25, C-26**: tutto a finestra. Non c'è un modo di far fallire a mano il caricamento degli account o un'azione di
  una notifica: servirebbe un errore finto nella Debug, che non è stato aggiunto.
- **C-27**: Crea regola con un account Gmail e uno Microsoft veri (le categorie compaiono, nessuna riga "categorie non
  lette").
- **C-28**: tutto.
- **C-29**: un `Start` del processo fermo oltre il tempo (qui una lettura ferma); il tetto di cinque secondi.
- **C-31**: un server vero con la rete lenta; Gmail (stesso disegno, qui solo IMAP).
- **C-32**: la revoca presso Google vera; `MicrosoftAuth.SignOutAsync`; Rimuovi account a finestra.
- **C-33**: la finestra di scrittura (qui il sorgente). Il gestore degli errori di `App` (qui lo stesso gestore su un
  Dispatcher della suite).
- **C-34**: `OnExit` e la fine della sessione a finestra (qui `Flush`, e il sorgente).
- **C-35**: la riga vera nel registro dopo `--errore-rete` su un messaggio non in copia.
- **C-37**: la rinomina di una cartella di Gmail il cui genitore MailKit non conosce; lo stesso caso in IMAP, non
  toccato. Un Gmail vero che non elenca la cartella appena creata (il rapporto lo dà per raro).
- **X-1**: tutto quello che sta nella finestra di scrittura (qui `Current` e il sorgente); un salvataggio o un invio
  in corso proprio mentre gli account si ricaricano.
- **La versione finale**: tutto è di Debug, tranne il conto degli avvisi della Release e una pubblicazione di prova mai
  avviata. Niente è stato installato.

## Audit dell'11 ottobre, codice, terzo gruppo: soglie e prove (C-38..C-49)

Fatto il 12 ottobre 2026 sulle sezioni 6 e 7 di `docs/audit/codice.md`. "Prima" è il commit `3dd91cb`. Commit:
`4ff5e18` (C-38), `9ebcaac` (C-39), `2d6d81c` (la suite: C-40, C-44, C-49 e `Until` di C-46), `c0f7e5c` (il server
IMAP finto: C-45 e il BAD di C-43), `2905bca` (C-41, solo il gancio di `ImapProvider`), `ecd44c4` (C-42), `c0c73c6`
(C-43), `20a087b` (C-47), `ae867c1` (il controllo di C-41, e `ProvaBozze.cs` rimesso a posto), più quello di questo
esito. **Cinque di questi commit non compilano da soli**, per un errore mio nel dividerli: vedi "Controlli e suite".
Su dodici punti: sei corretti (C-38, C-40, C-41, C-43, C-45, C-49), quattro corretti in parte con il resto lasciato
(C-39, C-42, C-44, C-46), uno verificato con due controlli scritti e il resto lasciato (C-47), uno misurato e lasciato
(C-48). Nessuno è falso per intero; due affermazioni lo sono: il minuto di attesa di C-45 (misurati 11 e 24 ms) e il
sospetto di C-44 che oggi una prova approfitti dei ganci di un'altra.

Questo gruppo tocca la suite stessa. La regola in più: il numero dei controlli e il loro significato non peggiorano
senza dirlo. Il confronto dei nomi, uno per uno, è in "Controlli e suite".

Come si è misurato:

- **Il punto di partenza**: la suite intera sul codice di `3dd91cb`, prima di toccare nulla: 999 controlli, tutto
  bene, 170 secondi. Un secondo giro con una cartella dei dati vuota: 999, tutto bene, 168 secondi.
- **Dopo**: `SuiteAuditTests` (`Core/ProvaBozze.Prove.cs`, da soli in un secondo con `--prova-audit risultato.txt
  prove`), 11 controlli e una riga di sola misura. La suite si prova con gruppi finti passati allo stesso codice che
  fa girare quelli veri: uno che solleva, uno che lascia accesi tre ganci, uno che chiede a Graph senza gestore finto.
  Il server IMAP finto si fa sbagliare apposta (`FailOn`).
- **Prima**: ogni controllo nuovo o riscritto è stato visto cadere almeno una volta con il difetto rimesso a mano,
  senza commit. C-38 (una copia riscritta con 100), C-39 (rete di sicurezza a 130 s), C-40 (senza il try per gruppo
  il giro si ferma al gruppo finto che solleva), C-41 (senza la fermata fra le cartelle: 5 SEARCH invece di 0), C-42
  (disegno non fermato: thread vivo dopo 5 s, 13.037 giri in più; fine della scrittura non annotata), C-43 (cinque:
  vedi il punto), C-44 (senza il riposo: 3 ganci accesi e la risposta del gruppo di prima), C-45 (2), C-49 (nella
  cartella di prima). I due di C-47 non hanno un "prima": sono copertura nuova di codice non toccato.
- **"Passava per il motivo sbagliato"**, mostrato due volte. Tolta da `ImapProvider` la riga del registro "account
  IMAP ... cambiato, con una password nuova": nella cartella dei dati di prima il controllo passava lo stesso (la riga
  era di un giro vecchio), in una cartella nuova cade. Resa muta `AddressBook.Sent`: il controllo dei nomi sosia
  passava (la voce era nella rubrica dai giri vecchi), in una cartella nuova cade.
- **Sonde temporanee**, mai in un commit: una riga in ogni punto da cui Missiva esce in rete (collegamenti IMAP e SMTP
  non verso 127.0.0.1, DNS, loghi, token e revoca di Google, configurazione automatica), una nel ramo "comando non
  previsto" del server IMAP finto, e cronometri nei gruppi più lunghi.
- **Ordine dei gruppi**: ogni gruppo da solo, ciascuno nel suo processo, e la suite intera all'inverso.
- Nessun avvio della versione di prova, nessuna rete fuori dal PC, i dati veri e il registro vero mai letti.

### Punti del rapporto

- **C-38** (soglie scritte in più posti con lo stesso significato). CONFERMATO, CORRETTO (`4ff5e18`). Nessun valore è
  cambiato.
  - `Core/MailLimits.cs`, costanti: messaggi per pagina (50: Gmail, IMAP, Graph); corpi in memoria (20), messaggio
    scaricato intero (300.000 byte), allegati della bozza in memoria (5 MiB): Gmail e IMAP; attesa della richiesta in
    corso e LOGOUT alla chiusura (5 e 5 s: Gmail e IMAP); tempo scaduto di Google (30 s, tre punti); accesso nel
    browser (10 minuti, tre punti); prova del server (45 s, due punti).
  - Ogni provider tiene il suo nome (`PageSize`, `SmallMessage`...), che ora vale quello comune: le righe che lo usano
    non cambiano. Il controllo legge le costanti dai metadati e cade se una copia torna un numero suo.
  - Un nome nel loro file, senza unire: il tetto di Retry-After di Graph (5 s, due punti), la richiesta di Annulla
    iscrizione (15 s, due punti), i 2.000 di `RulesSeen` (due volte nella stessa riga), i "5 MB" in decimale
    dell'anteprima.
  - NON uniti, perché il numero coincide e il significato no. `Heavy` della scrittura (5 MiB di allegati in tutto:
    salvataggio più rado) e `KeepAttachment` (5 MiB per un allegato solo: resta in memoria): ora `HeavyBytes`, con
    scritto perché. I 2.000.000 dell'Origine (quanto si scarica) e dell'anteprima di un allegato di testo (quanto si
    mostra): hanno già ognuno il suo nome. E quelli che il rapporto stesso esclude.
- **C-39** (soglie che devono restare coerenti fra loro). CONFERMATO, CORRETTO in parte (`9ebcaac`). Nessun valore è
  cambiato.
  - Controllo periodico: `PollEvery` (2 minuti) e `SafetyNet`, che ora è `PollEvery` meno 20 secondi (100 s, come
    prima). `DeltaEvery` (30 s) e `DeltaFresh` (2 minuti) hanno un nome; `DeltaFresh` resta un numero, perché non
    c'è scritto da nessuna parte di quale dei due giri sia multiplo: il controllo chiede solo che sia più lungo.
  - Copia sul disco: `OfflineCopy.Full` è nove decimi di `BodyCache.DefaultLimit` (180 MB sotto 200), e
    `OfflineCopy.Days` è `BodyCache.MaxDays`. Anche le due prove che rimettevano "200 MB" a mano ora rimettono la
    costante.
  - Graph: `UploadChunk` era già scritto come 4 volte 320 KiB, con il conto nel commento. Aggiunto il controllo
    (multiplo di 320 KiB, sotto i 4 MB, sotto la misura da cui si carica a pezzi).
  - LASCIATO `StuckCheck` (2 minuti, come il tempo scaduto di Gmail): legarlo vuol dire scegliere un altro valore, ed
    è la stessa scelta di C-09, che è del quarto gruppo.
  - LASCIATE le due finestre della ricerca, `Fresh` (30 s, i suggerimenti) e `ReuseSearch` (60 s, IMAP). Letto: non
    c'è un ordine da tenere, ma non vanno d'accordo. Vedi "Trovato in più".
- **C-40** (un solo try per tutta la suite, e i nomi sconosciuti di `--prova-audit`). CONFERMATO, CORRETTO (`2d6d81c`).
  - Prima, con un errore messo apposta nel quarto gruppo: 59 controlli su 999, "1 controlli non passati". E
    `--prova-audit risultato.txt velocità` (con l'accento): 50 controlli dei blocchi 4 e 5, "tutto bene", uscita 0.
  - Ora i gruppi stanno in una tabella (`Core/ProvaBozze.Gruppi.cs`): 65 più `caduta`, ognuno con un nome. Ogni
    gruppo gira nel suo try: un errore lascia la riga "ERRORE: il gruppo ... si è fermato qui", conta come un
    controllo non passato, e i gruppi dopo girano.
  - Un nome che non c'è: una riga "ERRORE: nessun gruppo si chiama ...", con l'elenco dei nomi, e uscita 1. Visto
    anche dal vero: `velocità` dà due righe, quella e "1 controlli non passati". Lo stesso per un ordine che non c'è
    dopo `--prova-bozze`.
  - Si avviano da soli anche i gruppi di `ProvaBozze.cs` (`graph`, `gmail`, `imap`, `pec`, `calendario`...). I venti
    nomi di prima avviano gli stessi gruppi; senza nome girano i gruppi degli audit, nell'ordine di prima.
  - Il file del risultato: le righe di prima non cambiano. In fondo, prima della riga finale, un blocco nuovo: per
    ogni gruppo i suoi controlli e i suoi secondi, poi il totale. L'ultima riga è ancora "tutto bene" o "N controlli
    non passati".
  - `--prova-bozze risultato.txt inverso` fa girare i gruppi dall'ultimo al primo.
- **C-41** ("Ricerca che nessuno aspetta più": 6 SEARCH invece di 5). CONFERMATO, CORRETTO (`2905bca` e `ae867c1`).
  - Visto: in due giri su cinque con il controllo di prima, "6 SEARCH con quella dopo".
  - La lettura del rapporto è giusta: il codice si ferma fra una cartella e l'altra, come promette. Era la prova, che
    annullava subito e pretendeva che la ricerca annullata non fosse ancora entrata nella prima cartella.
  - Ora il server finto trattiene una risposta finché la prova non ha annullato (`FakeImap.Hold`), e la prova aspetta
    la fine vera della ricerca interna (`ImapProvider.TestEverywhere`, solo Debug) invece di 300 ms. Due casi, contati
    esatti: annullata prima dell'elenco delle cartelle, 0 SEARCH; annullata dentro la prima cartella, 1 SEARCH e
    nessuna cartella dopo. La ricerca seguente: 5, una per cartella.
  - Zero cadute in otto giri interi, più tre con il gruppo da solo.
- **C-42** (S-01 e gli altri controlli con soglie di tempo). CONFERMATO che sono a tempo. CORRETTO in parte
  (`ecd44c4`).
  - "S-01, un disegno che non finisce": non misura più il tempo di processore di tutto il processo. Guarda il thread
    del disegno: è finito (`Join`), i giri del suo ciclo non crescono più, e i disegni non sono sospesi (cioè è stato
    fermato al limite, non lasciato a girare dopo il doppio del tempo). Resta "almeno 900 ms", che non può cadere:
    il limite della prova è un secondo.
  - Con il controllo di prima, nei cinque giri di questo gruppo: 0, 0, 0, 16 e 0 ms di processore (soglia 200). La
    caduta a 266 ms l'ha vista il gruppo precedente; qui non si è ripetuta.
  - I non letti dopo una scrittura (`UnreadSync`, gruppo `arrivi`): la prova non aspetta più. L'attesa dopo una
    scrittura vale "non ancora passata" (un'ora) o "passata" (zero), secondo il punto. Prima era di 150 ms, con il
    conto da fare entro quel tempo e poi una pausa di 250 ms.
  - Le altre soglie: LASCIATE, con il margine misurato. Vedi "Soglie di tempo rimaste". Nessuna è stata allargata.
  - Le due finestre "nessuna connessione" (300 e 200 ms): LASCIATE. Letto: in tutti e due i casi il lavoro è già
    finito quando si comincia a contare. I sei SVG si disegnano uno alla volta aspettando la fine del thread del
    disegno, e lo stesso controllo guarda, senza tempi, che nessuno dia un logo; per il certificato del marchio la
    prova aspetta `IsBusy`. La finestra coprirebbe solo una connessione aperta da un altro thread a lavoro finito.
- **C-43** (controlli che possono passare per il motivo sbagliato). CONFERMATO, CORRETTO (`c0c73c6`, e con C-49 e
  C-45).
  - Il registro letto per intero, e le undici letture delle ultime righe: ora il registro è del giro (C-49). Mostrato:
    vedi "Come si è misurato".
  - I nomi sosia con nomi fissi nella rubrica: lo stesso, con la rubrica del giro.
  - `catch (Exception)` di "Annulla durante la prova del server": ora solo annullamento o `MailException`. Un altro
    errore ferma il gruppo e si vede.
  - `truncated != null`, sempre vero: ora un PDF troncato dev'essere disegnato lo stesso o spiegato come rovinato.
    Con un tempo scaduto messo apposta cade ("non si è disegnato in tempo"); prima passava.
  - Il server IMAP finto rispondeva OK a quello che non conosceva: ora BAD. In tutta la suite l'unico comando che
    finiva lì era NOOP (16 volte in quattro gruppi), che ora ha la sua riga. Controllo: rinominare ed eliminare una
    cartella danno un errore; prima "riuscivano" senza fare nulla.
- **C-44** (ganci statici di prova mai rimessi a posto). CONFERMATO per i gestori HTTP, FALSO il sospetto. CORRETTO in
  parte (`2d6d81c`).
  - I gestori finti di Graph, di Google e della configurazione automatica non si toglievano mai. Mostrato: un gruppo
    installa il suo, quello dopo chiede le cartelle senza installarne uno e riceve "Cartella del gruppo di prima".
  - I ganci a campo (`TestPlain`, i consensi finti, `TestOffline`...): nessun gruppo ne lascia. Ognuno li rimette in
    un `finally`.
  - Il sospetto che oggi una prova ne approfitti: FALSO. Con tutti i ganci a riposo e nessun gestore fra un gruppo e
    l'altro, gli stessi controlli passano; così ogni gruppo da solo, e la suite all'inverso.
  - Ora, prima di ogni gruppo: i 40 ganci tenuti d'occhio tornano al valore che avevano all'avvio
    (`Core/ProvaBozze.Aiuti.cs`), e al posto dei tre gestori HTTP c'è un gestore "nessuno" che rifiuta e annota. Dopo
    ogni gruppo: un gancio rimasto acceso, o una richiesta arrivata a "nessuno", è un controllo non passato con il
    nome del gruppo.
  - Non fatto: i ganci che restituiscono un `IDisposable`. Nelle prove sono 320 assegnazioni e una cinquantina di
    chiamate a `UseTestHandler` da riscrivere, per un risultato che il riposo fra i gruppi dà già.
  - In più, dalle sonde: in tutta la suite nessun collegamento vero, nessuna richiesta DNS vera, nessuna richiesta
    vera a Google.
- **C-45** (server IMAP finto: eccezioni perse e una connessione muta per 60 s). CONFERMATO in parte, CORRETTO
  (`c0f7e5c`).
  - L'errore di conversione con un .eml allegato prima di un file: CONFERMATO. Il server elencava nella struttura solo
    i file ma contava anche il .eml quando il client chiedeva una parte per numero.
  - L'eccezione persa: CONFERMATO. Finiva nel lavoro scartato, senza una riga.
  - Il minuto di attesa: FALSO. Rimesso il difetto e tolto il rimedio: il client riceve `ImapProtocolException` in 11
    e 24 ms. La connessione si chiude con il lavoro che la serve (`using` in `ServeAsync`); non resta muta.
  - Ora un allegato è una parte con un contenuto, nella struttura e nelle parti per numero. Un errore del server
    chiude con BYE e resta scritto (`ServerErrors`): dopo il gruppo è un controllo non passato, con il tipo e il
    testo. Lo stesso per il server SMTP finto.
  - L'allegato senza contenuto l'aveva già sistemato il gruppo di prima (C-37).
- **C-46** (aiuti ripetuti e controlli doppi). CONFERMATO, CORRETTO in parte (`2d6d81c`).
  - `Until` era scritta due volte: ora una, in `Core/ProvaBozze.Aiuti.cs`.
  - LASCIATI: le altre sette attese scritte a mano, le quattro forme dell'account verso il server finto, le sei
    classi di gestori HTTP finti, le cartelle temporanee. Il rapporto stima un giorno di lavoro; sono oltre 18.000
    righe di prove, con il rischio di cambiare un controllo senza volerlo. Vedi "Scelte per l'autore".
  - `TempFile`, cartella fissa mai pulita: LASCIATA. La prova dei PDF la vuole proprio in `%TEMP%`.
  - I doppioni: LASCIATI. Il controllo dei nomi sosia di `ProvaBozze.cs` è un caso di S-24, ma ora non dipende più
    dai giri prima. "Password assente dal registro" tre volte: sono tre password diverse, in tre gruppi (`imap`,
    `gestisci`, `copia`).
- **C-47** (parti senza copertura). CONFERMATO l'elenco, con due differenze. Due controlli scritti (`20a087b`), il
  resto LASCIATO.
  - Contati i riferimenti nelle prove, oggi. Zero: `MoveFolderAsync`, `MigrateFolderKeys`, `MarkAllReadAsync`. Solo
    il caso che fallisce: `CreateFolderAsync` (Gmail, C-37). Solo di passaggio: `Mailto.Parse`, `SafeFile` (due
    misure), `Calendars.Save` e `Load` (dentro la copia di sicurezza).
  - Le differenze: `CategoryPalette` oggi ha i controlli di C-27. E rinomina ed elimina ora hanno il controllo del
    rifiuto (C-43), non quello del caso che riesce.
  - Scritti, perché brevi e su funzioni che non toccano la rete: `MigrateFolderKeys` (Preferiti, nascoste, chiuse,
    colori, ordine, sottocartelle; una cartella dal nome che comincia uguale e un altro account restano) e
    `Mailto.Parse` da sola (destinatari prima e dopo il punto di domanda, Cc, Ccn, il "+", una % senza cifre).
  - Il resto è in "Scelte per l'autore", in ordine di utilità.
- **C-48** (tempo della suite). MISURATO, LASCIATO. Vedi "Dove va il tempo della suite".
  - Le attese fisse contate dal rapporto (22,7 s) sono una parte piccola. Il resto è lavoro: confronti con il codice
    di prima su migliaia di voci, cifratura, il processo dei PDF, WebView2.
  - Nulla è stato accorciato. Ogni taglio toglie casi a un controllo, ed è una scelta. L'unica attesa sparita sono i
    250 ms dei non letti (C-42).
- **C-49** (stato che resta da un giro all'altro, e divisione dei due file di prova). CONFERMATO, CORRETTO
  (`2d6d81c`). La divisione dei file LASCIATA.
  - Ogni giro lavora in una sottocartella nuova di `MISSIVA_DATA`, "giro-...": rubrica, registro, impostazioni,
    contatti, loghi e copia nascono con il giro. A giro riuscito si cancella. Se un controllo non passa resta, e il
    risultato dice dove; un giro successivo toglie quelle più vecchie di un giorno. Con `MISSIVA_PROVA_TIENI=1` resta
    comunque.
  - I profili di WebView2 delle prove restano nella cartella data: rifarli a ogni giro costa secondi.
  - Due dipendenze dall'ordine, trovate facendo girare ogni gruppo da solo. `sicurezza2` cadeva: il gancio
    `SenderLogos.TestForget` cancellava un file in una cartella dei loghi che in una cartella nuova non c'è ancora.
    `impostazioni` aveva 10 controlli invece di 11: uno era dentro un `if (File.Exists(...))`, e in una cartella nuova
    il file c'è solo se un gruppo di prima l'ha scritto. Corretti tutti e due. Con la cartella di sempre non si
    vedevano.
  - Due giri di fila danno lo stesso esito e gli stessi nomi dei controlli.

### Trovato in più

- **La suite senza `MISSIVA_DATA`** (letto, non provato: è vietato avviarla così). `--prova-bozze` e `--prova-audit`
  partono prima del controllo che chiude la Debug senza `MISSIVA_DATA`: lavoravano nella cartella dei dati vera. Ora la
  cartella del giro, in quel caso, sta fra i file temporanei, e se `Store` avesse già letto la cartella vera il giro
  si ferma prima del primo gruppo.
- **Sottocartelle e separatore** (misura di C-47). `MigrateFolderKeys` riconosce le sottocartelle dalla barra. Su un
  server IMAP che separa con il punto (INBOX.Clienti.Rossi), rinominata INBOX.Clienti, i Preferiti e i colori di
  INBOX.Clienti.Rossi non seguono. Non corretto: vedi "Scelte per l'autore".
- **Le due finestre della ricerca** (letto). Invio riusa la ricerca dei suggerimenti per 30 secondi, "poi si rifà:
  intanto può essere arrivata posta". Ma su IMAP la ricerca rifatta con le stesse parole riceve quella di prima fino
  a 60 secondi dall'inizio. Fra 30 e 60 secondi la ricerca "rifatta" non è nuova.
- **Una ricerca che si unisce a una che si sta fermando** (letto). Se le stesse parole tornano proprio mentre la
  ricerca annullata sta uscendo, chi le chiede riceve un annullamento che non ha chiesto. Lo spazio è di
  microsecondi. TRASCURABILE, non toccato; è il motivo per cui la prova di C-41 aspetta la fine vera.
- **Il server di `--imap-finto`**: è lo stesso server. Rinominare o eliminare una cartella nella versione di prova
  con i dati finti ora dà un errore, invece di sembrare riuscito e tornare com'era alla rilettura.
- Gli heredoc di Bash hanno rovinato altre due volte le barre rovesciate in script Python: rifatti con Write.

### Controlli e suite

- `SuiteAuditTests`, 11 controlli: C-38, C-39, C-40 (2), C-44, C-49, C-45 (2), C-43, C-47 (2). Una riga di sola
  misura: C-47, le sottocartelle con il punto come separatore.
- **I nomi dei controlli, prima e dopo**: 999 e 1010. Confrontati con uno script, tolte le cifre e i segni casuali.
  - Tolti: nessuno. Nemmeno i doppioni di C-46.
  - Aggiunti: gli 11 di `SuiteAuditTests`.
  - Riscritti, con un altro testo (2). "ricerca che nessuno aspetta più": prima "si ferma (N SEARCH con quella
    dopo)", cioè al più 5; ora i due casi con il conto esatto, 0 e 1, e 5 per la ricerca seguente (C-41). "S-01, un
    disegno che non finisce": prima "il thread non resta a girare (N ms di processore nei 400 ms dopo)"; ora "il suo
    thread è finito e non resta a girare (finito, 0 giri del ciclo dopo la fermata)" (C-42).
  - Cambiati di senso con lo stesso testo (4). "non letti: un conteggio del server chiesto prima di una lettura...":
    non aspetta più 250 ms, l'attesa vale un'ora o zero secondo il punto (C-42). "Annulla durante la prova del
    server": vale solo un annullamento o una `MailException` (C-43). "PDF rovinato, spiegato, e il processo resta
    quello": il PDF troncato ora si guarda davvero (C-43). "impostazioni.json della cartella di prova: letto e
    riscritto uguale con i due serializzatori": prima saltato senza dirlo se il file non c'era, ora il file si scrive
    lì e il controllo c'è sempre (C-49).
  - Stesso testo e stesso codice, ma su altri dati: i controlli che leggono `missiva.log` (sedici punti in sette file)
    e quelli che contano sulla rubrica guardano il giro in corso, non tutti i giri fatti in quella cartella.
  - Righe nuove possibili: dopo un gruppo può comparire un "NO" che prima non esisteva (un gancio acceso, una
    richiesta senza gestore finto, un errore di un server finto). In un giro buono non ce ne sono.
- Suite completa sul codice di `ae867c1` (quello di questo esito: dopo cambiano solo i documenti): 1010 controlli,
  tutto bene, in 204 secondi, subito dopo due compilazioni da capo.
- Quattro giri prima, sullo stesso codice non ancora diviso in commit: 1010, tutto bene, in 187, 195, 190 e 188
  secondi. Gli stessi nomi in tutti.
- Ogni gruppo da solo, 65 processi: 1010 controlli in tutto, nessuno caduto, gli stessi nomi della suite. Al primo
  tentativo erano 1009, e prima ancora `sicurezza2` da solo cadeva: vedi C-49.
- La suite all'inverso: 1010, tutto bene, 189 secondi, gli stessi nomi.
- Nessuna caduta dei controlli dati per instabili in questi giri, dopo la correzione.
- Tempo: la suite di prima (`3dd91cb`, compilata in un albero di lavoro a parte) e quella di adesso, una dopo
  l'altra: 205 e 192 secondi. La mattina, prima di cominciare, quella di prima ne aveva fatti 170: è il PC che
  rallenta dopo un'ora di giri di fila, non la suite. In quel giro quella di prima è caduta ancora su C-41.
- Compilazione da capo sul codice di `ae867c1` (`dotnet build --no-incremental`): Debug 0 errori e 0 avvisi, Release
  0 errori e 0 avvisi. `prova-release` non è stata ricostruita.
- **Non ogni commit compila.** Compilati uno per uno in un albero di lavoro a parte: `4ff5e18`, `9ebcaac` e `2d6d81c`
  sì, con 0 avvisi. `c0f7e5c`, `2905bca`, `ecd44c4`, `c0c73c6` e `20a087b` no: 5 errori in `ProvaBozze.cs`.
  - Che cosa è successo: le modifiche sono state divise in commit alla fine, con uno strumento mio. Sei pezzi aggiunti
    al server IMAP finto sono finiti 32 righe più in basso del loro posto (lo strumento li metteva contando le righe
    di un file che aveva già il controllo di C-41), e il controllo di C-41 è rimasto fuori dal suo commit.
  - `ae867c1` rimette `ProvaBozze.cs` uguale al file su cui è girata la suite. Da lì in poi tutto compila.
  - La storia non è stata riscritta. Nulla è stato inviato: prima del push quei sei commit si possono rifare in ordine
    partendo dallo stesso albero finale, se serve una storia in cui ogni commit compila.
  - Gli stati intermedi che compilano non sono stati provati con la suite: è girata solo sull'albero finale.

### Dove va il tempo della suite

I secondi di ogni gruppo sono in fondo a ogni risultato. Qui cinque giri sullo stesso codice (quattro in ordine, uno
all'inverso): minimo, mediana e massimo. I gruppi sotto mezzo secondo sono 41 e insieme fanno 5,7 secondi.

| Gruppo | Controlli | Minimo | Mediana | Massimo |
|---|---|---|---|---|
| velocita2 | 17 | 58,6 | 61,8 | 62,4 |
| dati | 12 | 18,7 | 19,2 | 23,6 |
| copia2 | 24 | 17,5 | 17,8 | 18,0 |
| sicurezza | 54 | 10,0 | 10,2 | 10,7 |
| velocita3 | 15 | 10,0 | 10,0 | 10,2 |
| robustezza | 25 | 8,9 | 9,0 | 9,1 |
| sicurezza2 | 26 | 7,6 | 8,1 | 9,9 |
| html | 32 | 5,3 | 6,0 | 6,2 |
| pdf | 13 | 5,9 | 5,9 | 5,9 |
| avvio | 10 | 5,2 | 5,2 | 5,3 |
| sicurezza3 | 25 | 4,9 | 4,9 | 5,6 |
| graph | 26 | 4,5 | 4,6 | 4,6 |
| loghi | 26 | 4,0 | 4,0 | 4,1 |
| pec | 35 | 2,3 | 3,6 | 4,1 |
| comportamento | 29 | 2,4 | 2,5 | 2,7 |
| copia | 18 | 2,2 | 2,2 | 2,3 |
| idle | 13 | 2,1 | 2,1 | 2,1 |
| gmail, difetti, modelli | 21, 41, 5 | 0,7 | 1,1-1,2 | 1,2 |
| imap, arrivi, coda, prove | 36, 4, 20, 11 | 0,1 | 0,1-0,8 | 0,8 |
| in tutto | 1010 | 185,6 | 188,0 | 194,1 |

- Tre gruppi fanno metà della suite: `velocita2`, `dati`, `copia2`.
- Dentro quei tre, con cronometri temporanei (un giro solo, poi tolti):
  - `velocita2`, 58,5 s: `SuggestIndexAuditTests` 34,6 (il confronto dei suggerimenti con il codice di prima, su 5.000
    voci e 4.500 contatti, rifatto dopo ogni tipo di cambio), `SaveSoonAuditAsync` 8,9, `PeopleSearchAuditTests` 8,7,
    `BodyCacheAuditTests` 2,5, `OfflineSearchAuditTests` 2,5.
  - `dati`, 23,2 s: `DraftBreaksAuditTests` 8,4, `BigDraftMeasure` 5,6, `DataWritesMeasure` 5,0.
  - `copia2`, 17,1 s: `Copy2Structure` 13,0 (dentro, 475 importazioni di una copia con un valore cambiato alla volta).
- Negli altri: `PdfWorkerSecurityAuditAsync` 5,6 e `PipeCommandAuditAsync` 3,0 (`sicurezza`); `SnoozeSavesAuditAsync`
  4,5 e i due `NewMailCommandsAuditAsync` 2,3 e 2,2 (`velocita3`); `RobWebViewWindows` 4,2 (`robustezza`); `Sec2Images`
  3,7 (`sicurezza2`).
- Le funzioni di sola misura, senza controlli, che il rapporto propone di mettere dietro un'opzione: `BigDraftMeasure`
  5,6, `DataWritesMeasure` 5,0, `ProviderWorkMeasure` 1,0, `SummaryCacheMeasureAsync` 0,5, `SuggestIndexMeasure` 0,4,
  `CleanMeasure` sotto 0,1. In tutto 12,5 secondi.
- Il tempo cambia da un giro all'altro più di quanto cambi per le modifiche. I gruppi che lavorano di processore e di
  disco (`velocita2`, `dati`, `copia2`), mai toccati, hanno fatto 50,3, 12,1 e 13,3 secondi nel primo giro con i
  tempi per gruppo, e fino a 66,4, 23,6 e 20,2 nei giri dopo, uno di seguito all'altro. Per distinguere le due cose:
  la suite di prima (`3dd91cb`, in un albero di lavoro a parte) e quella di adesso, una dopo l'altra, 205 e 192
  secondi.

### Soglie di tempo rimaste

Il valore misurato sta nel testo di ogni controllo. Qui dieci giri interi di questo gruppo (i due di prima, otto di
dopo): il minimo e il massimo misurati, e quante volte la soglia sta sopra il massimo. La suite girava da sola, con il
PC alla corrente.

| Controllo | Soglia | Misurato | Margine |
|---|---|---|---|
| T-07, disegno che non si ferma nemmeno al doppio del tempo | fra 1.800 e 3.500 ms | 2.000-2.017 | 1,7 volte |
| T-01, il più lento dei messaggi costruiti per le regole nuove | 1.500 ms | 10-794 | 1,9 volte |
| Origine, i primi 1.500 byte | 2.000 ms | 799-822 | 2,4 volte |
| Icone dichiarate, pagine costruite apposta | 500 ms | 119-185 | 2,7 volte |
| V-21, file letti dalla ricerca fermata | meno di 225 su 300 | 39-76 | 3,0 volte |
| S-22, client fermo sul canale | fra 300 e 1.400 ms | 400-413 | 3,4 volte |
| HTML costruiti apposta, tetto 1.500 ms | 1.500 ms | 0-438 | 3,4 volte |
| C-53, chiusura con una richiesta in corso | 3.000 ms | 602-620 | 4,8 volte |
| Bozza con un HTML costruito per costare | 500 ms | 24-83 | 6,0 volte |
| HTML costruiti apposta, tetto 4.000 ms | 4.000 ms | 221-657 | 6,1 volte |
| S-01, tre loghi disegnati | 4.000 ms | 150-615 | 6,5 volte |
| p7m da 20 MB | 1.500 ms | 99-219 | 6,8 volte |
| V-20, `Forget`, mediana | 10 ms | 1,0-1,4 | 7,1 volte |
| T-32, RTF di 141 KB | 3.000 ms | 153-336 | 8,9 volte |
| S-23, intestazione di 1,1 MB | 1.500 ms | 67-149 | 10 volte |
| PDF da 500 pagine, la prima | 500 ms | 28-35 | 14 volte |
| Altri nove (vCard, .ics, 20.000 elementi annidati, avviso dell'IDLE, V-29, S-02, riga di 2 MB, percorsi di rete, chiusura con il LOGOUT muto) | | | oltre 50 volte |

- Due soglie sono minimi, non tetti. "S-01, fermato dopo almeno 900 ms": misurati 1.012-1.048; non può scendere sotto
  il limite della prova, un secondo. "V-21, una ricerca deve durare più di 30 ms" (la prova annulla dopo 30 ms, e se
  la ricerca fosse già finita non ci sarebbe nulla da fermare): misurati 211-352.
- B-22 (`Log.WithoutAddresses`, soglia 100 ms): il caso più lento visto è 2,2 ms.
- La più stretta è T-07: 1,5 secondi di margine. Non dice nulla che il resto del controllo non dica già (il disegno è
  ancora fermo nel suo blocco quando la funzione torna): cadrebbe solo se la prova stessa restasse ferma un secondo e
  mezzo.
- Non toccate nemmeno le attese fisse che non sono soglie: la pausa di 30 ms di V-21 prima di annullare, le attese dei
  salvataggi accorpati.

### Che cosa cambia per chi usa Missiva

- Nulla. Nel codice di Missiva cambiano solo i nomi di alcuni numeri, con gli stessi valori.
- Per chi fa girare la suite: ogni gruppo ha un nome; un nome sbagliato è un errore; in fondo al risultato ci sono
  controlli e secondi per gruppo; la cartella di `MISSIVA_DATA` non accumula più i dati dei giri.

### Scelte fatte qui, da rivedere

- **I dati di un giro riuscito si cancellano**, registro compreso. Chi vuole guardarlo usa `MISSIVA_PROVA_TIENI=1`.
- **Un errore in un gruppo conta come un controllo non passato**, uno solo, qualunque sia il numero dei controlli che
  non sono partiti. Il blocco in fondo dice quanti controlli ha fatto ogni gruppo.
- **Un gancio lasciato acceso è un controllo non passato**, anche se il gruppo dopo non lo usa.
- **Fra i gruppi i gestori HTTP di Graph, Google e configurazione automatica rifiutano**. Loghi e Annulla iscrizione
  tornano al gestore vero: alcune prove ne guardano proprio il comportamento (indirizzi non pubblici).
- **Il server IMAP finto risponde BAD** a quello che non conosce. Chi aggiunge una prova con un comando nuovo deve
  aggiungerlo al server.
- **Un .eml allegato non è fra gli allegati del server finto**: non lo elenca e non lo dà.
- **`DeltaFresh` resta un numero**, con il solo controllo dell'ordine.
- **L'elenco dei ganci è fatto a mano** (40, in `Core/ProvaBozze.Aiuti.cs`). Un gancio nuovo che non ci entra non
  torna a riposo da solo e nessuno dice se resta acceso.
- **I nomi dei gruppi nuovi** (`graph`, `gestisci`, `rispondi-a`, `bozze-riprese`, `impostazioni`...) li ho scelti io.

### Scelte per l'autore

- **Sottocartelle con il punto come separatore** (trovato in più): `MigrateFolderKeys` deve usare il separatore
  dell'account? Proposta: sì, è una riga più un controllo; oggi dopo una rinomina si perdono Preferiti e colori delle
  sottocartelle sui server tipo Courier.
- **Le due finestre della ricerca** (C-39): 30 secondi per Invio e 60 per IMAP. Quale vale? Proposta: 30 per tutti e
  due, così "si rifà" è vero.
- **`StuckCheck`** (C-39): va con C-09 del quarto gruppo. Proposta: deciderlo lì.
- **Copertura che manca** (C-47), in ordine di utilità (i tempi sono stime mie):
  1. crea, rinomina, sposta ed elimina una cartella sul server IMAP finto, che oggi non ha RENAME né DELETE (mezza
     giornata con il server);
  2. `MarkAllReadAsync` con il conteggio dei non letti, per Gmail, IMAP e Graph (un'ora);
  3. le stesse azioni sulle cartelle per Graph con il gestore finto (due ore);
  4. andata e ritorno di `Calendars.Save` e `Load` (mezz'ora);
  5. `SafeFile` con una scrittura interrotta (un'ora).
  Proposta: i primi due.
- **Tempo della suite** (C-48): vuoi che si accorci? Proposta: solo `SuggestIndexAuditTests`, che da sola è quasi un
  quinto della suite (34,6 secondi su 188). Meno ricerche a confronto bastano, ma è togliere casi a un controllo.
- **Aiuti comuni delle prove** (C-46) e **divisione dei due file grandi** (C-49): proposta: la divisione sì, per
  spostamento puro, in un giro suo senza altre modifiche; gli aiuti no.
- **Ganci con `IDisposable`** (C-44): proposta: no, il riposo fra i gruppi basta.

### Non verificato

- **La suite senza `MISSIVA_DATA`**: il ramo che usa una cartella temporanea è solo letto.
- **Il server di `--imap-finto` con la versione di prova a finestra**: nessun avvio. Se MailKit manda un comando che
  la suite non manda mai, ora riceve BAD: il registro del server lo dice (`LogCommands`). Passi per l'autore, quando usa
  `prova-finta.ps1`: apri, leggi, sposta, elimina un messaggio come sempre; se compare un errore nuovo, nel registro
  c'è la riga "server IMAP finto:" con il comando.
- **C-41 su un server vero**: la fermata fra le cartelle è provata solo sul server finto.
- **C-45, il server SMTP finto**: il suo errore annotato è solo scritto. Nessun controllo lo fa scattare.
- **C-42**: nessun giro sotto carico pesante o a batteria. I margini sono quelli di questo PC, con la suite da sola.
- **C-39**: `PollEvery`, `SafetyNet` e `DeltaEvery` a finestra (qui i valori e il sorgente).
- **La versione finale**: la Release compila senza avvisi; niente è stato installato.

## Audit dell'11 ottobre, codice, quarto gruppo: residui, doppioni, file grandi, semplificazioni (C-01..C-23, C-64..C-66)

Fatto il 12 ottobre 2026 sulle sezioni 1, 2, 3 e 9 di `docs/audit/codice.md`. "Prima" è il commit `f83b0a0`. Commit, uno
per punto: `fb67542` (C-07), `ad91823` (C-05), `72e0652` (C-02), `55f7885` (C-04), `c0c01fb` (C-13), `268b0a3` (C-09),
`1f781f3` (C-10), `561c06e` (C-12), `ffe2f33` (C-14), `69e2f86` (C-15), `ba933b0` (C-16), `2b132d1` (C-17), `0e0f720`
(la misura di C-06), più quello di questo esito. Ognuno compila da solo in Debug e, dove tocca codice che in Release è
diverso, in Release, con zero avvisi, ed è entrato solo dopo i gruppi della suite che lo riguardano.

Su ventisei punti: sette corretti (C-02, C-05, C-07, C-10, C-12, C-14, C-17), cinque corretti in parte con il resto
lasciato (C-04, C-09, C-13, C-15, C-16), uno misurato e lasciato (C-06), due confermati e tenuti apposta (C-01, C-03),
uno lasciato perché la suite non lo copre (C-18), e dieci rimisurati e lasciati per decisione già presa (C-08, C-11,
C-19..C-23, C-65, C-66, e C-64 che riassume gli altri). Nessuno è falso per intero; tre affermazioni lo sono oggi:
`OfflineCopy.TestFull` "mai usato" (ora lo usano i controlli del terzo gruppo), il riferimento "di una finestra
distrutta" di C-13 (misurato: è 0), e il comando "disegni dopo arrivo" di C-04 (non è un comando: è il nome, in un
commento, della riga del registro di "arrivo misurato", che CLAUDE.md descrive). Due cose erano già corrette dai gruppi
di prima: il parametro di `ReplaceSignature` (C-52) e `RulesView.StartAccount` (C-37).

Questo è il gruppo dove è più facile togliere qualcosa che serve. La regola in più: prima di togliere un interruttore,
una variabile, un comando o un membro si è cercato chi lo usa fuori dal codice C#, anche negli strumenti dell'autore in
`C:\Progetti\Missiva\loop`. Quello che lì è usato resta, e qui c'è scritto da chi.

Come si è misurato:

- **Il punto di partenza**: la suite intera sul codice di `f83b0a0`, prima di toccare nulla: 1010 controlli, tutto
  bene, 189 secondi; Debug compilata con 0 avvisi.
- **Chi usa che cosa fuori dal C#**: uno script (mai nel repository) cerca un testo in `strumenti\`, negli altri script
  e documenti del repository, in `CLAUDE.md`, `LEGGIMI.md`, `docs\` e in `loop\` (solo `*.ps1`, `*.md`, `*.txt`, `*.py`,
  `*.cs`; mai dentro le cartelle `lampi-*`, mai immagini o registrazioni). Per i rapporti di `docs\audit`, che nominano
  tutto, conta solo se dicono che una cosa è tenuta apposta.
- **Membri mai usati**: uno script conta i nomi dichiarati che compaiono una volta sola in tutti i `.cs`, `.xaml` e
  `.ps1` (commenti tolti, stringhe tenute). Oggi 34: i tre tolti, e 31 fra campi di strutture di Windows, metodi che
  il framework chiama da sé e nomi di tipi che lo script prende per dichiarazioni.
- **Doppioni**: per ogni coppia unita, i due testi confrontati riga per riga senza commenti prima di unirli (C-10: 58
  righe, una diversa; C-15: 16 righe, uguali dopo aver cambiato il nome del menu). Per le coppie lasciate, le righe
  uguali nello stesso ordine e quelle in blocchi di almeno 8, su righe normalizzate (senza commenti, stringhe e numeri
  sostituiti, senza righe di sole graffe), prima e dopo.
- **File scritti dal codice di prima** (C-17): i tre file della copia locale scritti dalla compilazione di `ba933b0`,
  l'ultima prima del cambio, in una cartella tenuta da parte, e letti dalla compilazione di `2b132d1`: letti uguali, e
  le loro impronte non sono cambiate (nessuno li ha riscritti).
- **Dopo**: `CleanupAuditTests` (`Core/ProvaBozze.Residui.cs`, da soli in due secondi con `--prova-audit risultato.txt
  residui`), 8 controlli e tre righe di sola misura. Tre finestre di WPF vere fuori dallo schermo, senza prendere il
  fuoco; dove la correzione sta in una finestra, i sorgenti letti.
- **"Prima" dei controlli nuovi**: i controlli che leggono i sorgenti cadono per costruzione sul codice di prima (C-09:
  0 client su 4 con il nome; C-13: otto file scrivevano `Owner`; C-16: visto cadere per un "#C42B1C" rimasto in un mio
  commento). Per C-13 una riga di misura rifà nella stessa prova quello che faceva il codice di prima. C-06, C-15 e
  C-17 sono copertura nuova di comportamento non cambiato: non hanno un "prima".
- Nessuna prova a finestra, nessun avvio della versione di prova, nessuna rete fuori dal PC; i dati veri e il registro
  vero mai letti. `costo righe` e `scorri N` chiedono una finestra: non sono stati rifatti.

### Punti del rapporto

- **C-01** (il modello vecchio della riga dell'elenco). CONFERMATO, LASCIATO: è usato.
  - Oggi: 173 righe di XAML (`MainWindow.xaml`, 795-967) e otto proprietà di `MessageItem` che servono solo a quel
    modello. La variabile `MISSIVA_RIGA_VECCHIA` dall'11 ottobre vale solo nelle versioni di prova (T-29): il rapporto
    la dava attiva anche nella versione installata.
  - Chi lo usa: `loop\strumenti\vetrina-C.ps1` e `altezze-C.ps1` hanno l'interruttore `-Vecchia`, che mette la
    variabile, e `stati-C.ps1` chiama il primo con `-Vecchia`. I comandi "vetrina" e "vscroll" li usano anche
    `barra-I.ps1` e `misura-cursore.ps1`. Togliendo il modello, `-Vecchia` confronterebbe il modello nuovo con sé
    stesso senza dirlo.
  - Le tre notifiche in più (`Changed` di proprietà che il modello nuovo non conosce): letto, non misurato. Ognuna fa
    `Sync` e due invalidazioni, che prima del prossimo layout valgono una.
- **C-02** (interruttori della sonda del cursore). CONFERMATO, CORRETTO (`72e0652`).
  - Tolti senza-azioni, senza-hover, senza-tooltip, senza-mousemove, lista-inerte, rimedio-hover e senza-tutto, con le
    sei variabili d'ambiente e il rimedio provato e non tenuto (`CursorProbe.Later`, `MessageRowElement.SettleHover`).
    `MessageRowElement` li leggeva in otto righe; `CursorProbe.cs` passa da 367 a 257 righe.
  - Restano, perché `loop\strumenti\misura-cursore.ps1` li usa: "sonda senza-sospensione on|off", "cursore stato",
    "sospensione prova", `MISSIVA_SENZA_SOSPENSIONE`, i contatori e la riga "cursore:". Quello script nomina le sei
    variabili tolte in una riga sola, per cancellarle dall'ambiente.
  - Nella versione installata non cambia nulla: erano costanti false.
- **C-03** (copie "di prima" tenute per il confronto). CONFERMATO, TENUTE: le usano i controlli.
  `AddressBook.SuggestOld` (`SuggestIndexAuditTests`), `Contact.MatchesByCulture` con `TestOldMatch` (le prove di
  Persone e il comando "persone confronto"), `HtmlToFlow.TestOld` (`DataAuditTests`). Restano finché restano quei
  controlli.
- **C-04** (comandi di `MISSIVA_KEYS` e variabili senza un uso noto). CONFERMATO per uno, CORRETTO quello (`55f7885`);
  gli altri sono usati.
  - Tolto `--mostra-notifica`: nessuno script né documento lo nomina. Letto: girava prima del controllo che chiude la
    Debug avviata senza `MISSIVA_DATA`, e passava da `NotificationAvatar.PathOf`, che scrive un PNG nella cartella dei
    dati.
  - Restano, con chi li usa. "tau N": `loop\strumenti\touchpad-A.ps1`. "modello N" e i modelli 0-2 del touchpad:
    `touchpad-A.ps1` e `touchpad-C.ps1`. "vetrina", "vscroll", "altezze": C-01. "sonda", "cursore stato", "sospensione
    prova": C-02. `MISSIVA_REGISTRO_SUBITO`: ESITO.md lo dà per il confronto di A-10. Solo descritti in CLAUDE.md, e
    usati da nessuno script: `MISSIVA_VARIANTI` (la prova alla cieca del touchpad; `barra.ps1` la cancella soltanto),
    "scorri semplice", "arrivo di prova vecchio", "arrivo misurato", "persone confronto", "costo gruppi", "costo
    webview".
  - LASCIATA la tabella dei comandi al posto di `Press` (318 righe): è la riscrittura di un metodo lungo, C-23.
- **C-05** (membri mai usati). CONFERMATO per tre, CORRETTO (`ad91823`).
  - Tolti `PecInfo.IsReceipt`, `EventWindow.TestSaveAsync`, `PeopleView.TestSelectFirst`.
  - `OfflineCopy.TestFull`: FALSO oggi, lo usano i controlli delle soglie. `RulesView.StartAccount`: già solo in Debug
    (C-37). `MainWindow.QuickReply`: TENUTA, il suo commento dice che resta per le prove della risposta dei provider
    (tre controlli la usano).
  - I campi delle strutture di Windows restano, come dice il rapporto.
- **C-06** (codice di migrazione che gira a ogni avvio). CONFERMATO, MISURATO, LASCIATO.
  - `MailCache.EncryptOld` gira come prima. Il suo giro è ora un metodo a parte (`EncryptOldIn`), per misurarlo e
    provarlo (`0e0f720`): nessuna prova lo copriva.
  - Misura: 6,1, 6,4 e 6,7 ms in tre giri del gruppo da solo, 7,5 dentro la suite intera, per guardare una copia con 62
    file `.json` da 30 KB e 2.000 corpi quando non c'è più nulla da cifrare (1,9 MB letti per guardare il primo byte di
    ogni `.json`). A caldo: i file erano appena stati scritti. Gira 20 secondi dopo l'avvio, su un thread in
    sottofondo. TRASCURABILE.
  - Se nei dati veri ci sia ancora qualcosa in chiaro non si può sapere da qui: vedi "Non verificato".
- **C-07** (assegnazioni e parametri inutili). CONFERMATO, CORRETTO (`fb67542`).
  - `MessageRowElement.Compute`: via la lista mai usata e il pennello dell'accento mai usato; `ArrangeOverride` non
    tiene più il risultato di `Ensure`. `Store.TryWrite`: via un `error = null`. `App.AddActions`: via il parametro
    `now`; i due riassunti finiti sopra il membro sbagliato tornano al loro posto. `SenderCheck.FetchAsync` con il
    parametro `found` ora è `FetchIfThereAsync`.
  - `ReplaceSignature`: già corretto (C-52). I due parametri di `MailHtml` imposti da AngleSharp restano.
  - Quanto pesavano la lista e il pennello su una riga non è misurato: "costo righe" chiede una finestra, e una riga
    fuori da una finestra non si dispone (i suoi colori vengono dall'applicazione, che nella suite non c'è).
- **C-08** (Gmail e IMAP: corpi, anticipi, allegati e azioni per cartella scritti due volte). CONFERMATO, LASCIATO:
  decisione già presa.
  - Oggi, dopo C-10: 415 righe uguali nello stesso ordine fra i due file, 203 in blocchi di almeno 8, il blocco più
    lungo 59. Prima di questo gruppo 456 e 250.
  - La divergenza del rapporto c'è ancora: per scaricare un allegato Gmail apre la cartella con SELECT (`OpenAsync`),
    IMAP con EXAMINE dove può (`ExamineOrOpenAsync`).
  - La proposta regge: una base comune con tre ganci. 1-2 giorni, rischio medio.
- **C-09** (la connessione IMAP e `RunAsync` copiate, con un timeout diverso). CONFERMATO, CORRETTO per i nomi
  (`268b0a3`); il valore è una scelta dell'autore.
  - Misurato: il valore di base di MailKit è 120.000 ms, per IMAP e per SMTP. Gli account IMAP scrivevano 60.000 in due
    punti; Gmail nulla, nei suoi quattro client.
  - Nessuno dei due è mai stato scelto dopo una misura. Il 60 degli account IMAP è scritto dal loro primo commit
    (`924bf0f`, 26 settembre). Il 120 di Gmail è quello di MailKit: ESITO.md e CLAUDE.md lo nominano due volte come
    "non toccato" (7 ottobre), e C-53 ha misurato il 60 di IMAP (60.017 ms).
  - Ora `MailLimits.ImapSilenceMs` e `MailLimits.GmailSilenceMs`, uno accanto all'altro, e ogni client lo dice. Nessun
    valore cambia. Resta un numero suo la prova del server di un account IMAP (30 secondi): è un'altra cosa.
  - `App.StuckCheck` (2 minuti) per Gmail scade insieme al tempo scaduto, non dopo. Non toccato: vedi "Scelte per l'autore".
  - LASCIATE la classe di connessione unica e `RunAsync` comune: vanno con C-08.
- **C-10** (`BuildAsync` identico in Gmail e IMAP). CONFERMATO, CORRETTO (`1f781f3`).
  - 58 righe di codice ciascuno, una diversa: la connessione su cui si chiedono Message-Id e References. Ora
    `MimeOptions.BuildAsync`, con tre funzioni che ogni provider gli dà. Stesse chiamate nello stesso ordine.
  - `GmailProvider.cs` da 1540 a 1469 righe, `ImapProvider.cs` da 1791 a 1735.
  - Coperto dai gruppi che spediscono e salvano bozze sui server finti (`gmail`, `imap`, `opzioni-invio`, `identita`,
    `formato`, `inviti`, `rispondi-a`, `velocita3`, `comportamento`, `coda`): tutti passati prima del commit.
- **C-11** (bozze di Gmail e IMAP, e una differenza nella ricerca della bozza). CONFERMATO, LASCIATO: va con C-08.
  La differenza c'è ancora: `FindAsync` di IMAP esclude i messaggi `\Deleted` e un Message-Id vuoto, quello di Gmail
  no.
- **C-12** (Graph: cinque copie di "token del consenso più Consenti"). CONFERMATO, CORRETTO (`561c06e`).
  - Ora un elenco (`GraphProvider.Extra`), una tabella (`ExtraOf`: ambiti, token finto, frase) e due metodi
    (`ExtraTokenAsync`, `AllowAsync`). I metodi di prima restano, di una riga. Ambiti, token finti e frasi sono gli
    stessi; per le caselle condivise il token resta dell'account di lavoro.
  - Le righe non calano: `GraphProvider.cs` passa da 1958 a 1966. Il rapporto ne stimava 40 in meno; l'elenco e i suoi
    commenti pesano più delle copie tolte. Quello che cambia è che un permesso nuovo è una voce, non una sesta copia.
  - Coperto dai gruppi che contano "nessuna richiesta senza consenso" (`calendario`, `risposte-automatiche`, `contatti`,
    `suggerimenti`, `condivise`, `regole`, `inviti`).
- **C-13** (consenso nell'interfaccia: otto copie, e il proprietario della finestra di Microsoft che resta su finestre
  chiuse). CONFERMATO il difetto, CORRETTO (`c0c01fb`); le otto copie LASCIATE.
  - Prima: `MicrosoftAuth.Owner` scritto in dodici punti di otto file, rimesso dalla principale solo dopo Aggiungi
    account, Gestisci account e Risposte automatiche. Dopo una regola, le regole nelle Impostazioni e "Cerca anche fra i colleghi" della
    scrittura restava la funzione della finestra chiusa.
  - Misurato con una finestra vera: dopo la chiusura quella funzione dà 0, non il riferimento di una finestra
    distrutta come scrive il rapporto. Cioè alla richiesta dopo MSAL riceveva "nessun genitore".
  - Ora: `Owner` lo scrive solo la principale. Le altre chiamano `MicrosoftAuth.OpenOver(finestra)`, che vale finché la
    finestra resta aperta e si rilascia da sé. `CurrentOwner` decide alla richiesta: l'ultima finestra che l'ha chiesto
    e che si vede ancora, altrimenti la principale. Una scrittura nascosta nella Posta in uscita non fa da genitore.
  - Non si è seguita la proposta del rapporto ("la finestra attiva di Missiva"): la finestra attiva può essere il
    pannello dei filtri o un avviso, che si chiudono da soli, e senza un account vero non si può provare che cosa
    succede alla finestra di Microsoft quando il suo genitore sparisce.
  - Controlli: tre finestre vere (principale, regola, scrittura) aperte, nascoste e chiuse in ordine; nei sorgenti
    `Owner` lo scrive un file solo.
  - Che cosa fa MSAL con un genitore 0, e la finestra di Microsoft vera: NON VERIFICATO, vedi sotto.
- **C-14** (Google: la chiusura dell'accesso copiata tre volte). CONFERMATO, CORRETTO (`ffe2f33`).
  - `GoogleAuth.Apply(account, risultato)`: token di aggiornamento cifrato, `SetScopes`, token di accesso dimenticato.
    I tre punti lo chiamano; chi chiama salva le impostazioni, come prima.
  - I tre punti aprono il browser: la suite non ci arrivava e non ci arriva. Il controllo prova la funzione (un accesso
    in cui Google non ridà il calendario: avviso una volta, token riletto, token di accesso dimenticato) e legge nei
    sorgenti chi la chiama.
  - NON uniti i due avvisi "Nel browser hai scelto ...": finiscono con parole diverse. L'attesa di dieci minuti era già
    una costante (C-38).
- **C-15** (menu del tasto destro: ciclo copiato e nomi scritti due volte). CONFERMATO, CORRETTO il ciclo (`69e2f86`);
  i nomi LASCIATI.
  - Le sedici righe erano uguali nei due menu. Ora `MainWindow.FillCustomMenu`, statico, con la finestra di
    "Personalizza" come parametro.
  - Nessuna prova costruiva quei menu. Il controllo nuovo chiama il metodo da solo: voci nascoste mai chieste,
    separatori solo fra due voci presenti, Personalizza in fondo, e l'ordine di base dei messaggi.
  - `MainWindow.xaml.cs` da 5284 a 5277 righe (con C-07 e C-13).
- **C-16** (finestre di dialogo con lo stesso scheletro, e un rosso fisso nel tema scuro). CONFERMATO il rosso,
  CORRETTO (`ba933b0`); lo scheletro LASCIATO.
  - Nel codice: `#C42B1C` in sedici punti che sono testi d'errore o avvisi, uguale nei due temi. Quattordici finestre,
    gli avvisi delle regole nelle Impostazioni con il bordo della scheda, e "Calendario non disponibile".
  - Ora `CriticalBrush`, il rosso che Missiva usa già per bandierine, importanza alta e scadenze passate, con un
    riferimento alla risorsa: segue il tema anche a finestra aperta. `Dialog.ErrorText` e `Dialog.Critical`.
  - Si vede: nello scuro il rosso diventa `#FF99A4`; nel chiaro cambia di pochissimo, da `#C42B1C` a `#C50F1F`.
  - Restano fissi, perché non sono testi d'errore: la riga dell'ora nel calendario, il colore di ripiego del numero
    sull'icona, il tono rosso delle icone dei menu (ha già il suo valore per lo scuro), il segno del colore del testo
    nella scrittura, l'icona di un allegato pericoloso nella pagina.
  - Il contrasto vero sopra il fondo delle finestre non è misurato: serve una foto.
- **C-17** (scritture di file fuori da `SafeFile` e DPAPI chiamata direttamente in nove file). CONFERMATO, CORRETTO
  dove il file resta identico (`2b132d1`).
  - Oggi i file erano sette e le chiamate ventuno: ora `SafeFile.Protect` e `SafeFile.Unprotect`, la stessa chiamata.
    DPAPI si chiama in un file solo (`Store.cs`).
  - Il file temporaneo con la rinomina, senza svuotare fino al disco, era in cinque punti: ora `SafeFile.WriteCopy`,
    le stesse due righe e lo stesso nome del temporaneo.
  - Due scritture dirette sul file vero passano da `WriteCopy`: la copia del calendario e il PNG di un logo. Stesso
    contenuto; una scrittura interrotta non lascia più il file a metà.
  - "Identico byte per byte" vale per quello che c'è dentro: DPAPI dà ogni volta byte diversi per lo stesso contenuto.
    Controllato nei due versi nella suite, e con i tre file scritti dalla compilazione di prima.
  - NON fatto, perché cambierebbe qualcosa: lo svuotamento fino al disco per token, elenchi e corpi (i corpi si
    scrivono a centinaia: va misurato); un nome unico per il temporaneo (dopo una caduta resterebbero file che nessuno
    toglie); passare tutto da `Store.ProtectBytes` con l'entropia delle prove (cambierebbe le prove della copia di
    sicurezza, non la versione installata). I due file di segno dei loghi restano scritti direttamente.
  - Il sospetto del rapporto sulle prove della copia di sicurezza: letto, `Backup` legge contatti e rubrica con
    l'entropia e chi li scrive no. Le prove passano perché scrivono quei file da sé. Non toccato.
- **C-18** (doppioni minori nell'interfaccia). CONFERMATO, LASCIATO: la suite non lo copre.
  - Nascondere e rimettere la posta (calendario e Persone): le stesse righe con altri nomi. Sta in metodi della
    finestra principale, che nessuna prova costruisce: unirle senza un controllo è contro la regola di questo gruppo.
  - Sposta ed Elimina: dopo C-50 le due parti si somigliano meno. Rischio medio, lasciato.
  - La cultura italiana: non due copie ma ventiquattro, in venti file. TRASCURABILE.
  - L'azzeramento delle opzioni dell'elenco: tre righe. TRASCURABILE.
- **C-19, C-20, C-21, C-22** (divisione dei quattro file grandi). CONFERMATI, LASCIATI: decisione già presa.
  - Oggi: `MainWindow.xaml.cs` 5277 righe (564 solo Debug), `ComposeWindow.xaml.cs` 2143, `ReadingView.xaml.cs` 1909,
    `GraphProvider.cs` 1966. Il rapporto ne contava 5177, 2089, 1831 e 1919; all'inizio di questo gruppo erano 5284,
    2143, 1909 e 1958: negli ottanta commit fra il rapporto e questo gruppo sono cresciuti di 107, 54, 78 e 39 righe.
  - Dichiarazioni al livello della classe, campi compresi, contate dal mio script: 377, 159, 153 e 146. Il rapporto
    dà 298, 98, 83 e 95 con un altro conto: i due numeri non si confrontano. Per la divisione serve lo stesso conto
    prima e dopo.
  - La proposta regge per aree; gli intervalli di righe del rapporto non valgono più. Tempi del rapporto: 2,5 ore
    Graph, 5-6 MainWindow, 2 la scrittura, 1,5 il riquadro.
- **C-23** (i metodi più lunghi). CONFERMATO, LASCIATO: decisione già presa.
  - Oggi fuori dalle prove, contando le graffe come lo script del rapporto: 1 oltre 300 righe (`DevTools.Press`, 318),
    10 metodi fra 150 e 300 (più una classe annidata, `HtmlToFlow.Writer`), 25 dichiarazioni fra 80 e 150 (non
    guardate una per una).
  - I primi: `Press` 318, `StartAsync` 289, `SettingsWindow.SignaturesPage` 198, `WriteDraftAsync` 192,
    `MessageRowElement.Compute` 191, `LogoSvg.Clean` 168, il costruttore di `EventWindow` 167, `App.OnStartup` 166,
    `ReadingPage.Document` 163, `MailHtml.Clean` 156, `SettingsWindow.TemplatesPage` 153, `GoogleContacts.Merge` 149.
  - Dieci non sono nell'elenco del rapporto: `SignaturesPage`, `Compute`, `LogoSvg.Clean`, il costruttore di
    `EventWindow`, `TemplatesPage`, `CalendarView.WeekGrid` (144), `ReadingPage.Message` (124), `PeopleView.ShowCard`
    (119), `App.CheckAccountAsync` (118), `Rules.RunAsync` (117).
  - `BuildMessageMenu` è sceso da 116 a 100 con C-15.
- **C-64** (semplificazioni senza rischio). Fatte quelle dei punti corretti: C-07, C-12, C-13, C-15. Già fatta dal
  secondo gruppo quella di C-27. Lasciate: il blocco Debug di `StartAsync` (C-23), nascondi e rimetti la posta (C-18),
  `EncryptOld` (C-06).
- **C-65** (`IMailProvider` e `TestingProvider`). CONFERMATO, LASCIATO: decisione già presa.
  Oggi `IMailProvider` ha 88 membri, 76 metodi; `TestingProvider.cs` 294 righe. La proposta, una prova che con la
  riflessione controlla che ogni metodo che scrive sia fermato da `TestingProvider`, regge ed è piccola: vedi "Scelte
  per l'autore".
- **C-66** (codice di prova nell'eseguibile). CONFERMATO, LASCIATO: decisione già presa, e il rapporto stesso dice "non
  ora". Oggi `Core/ProvaBozze*.cs` sono 22 file, 19.414 righe, 1,41 MB, un terzo dei byte di tutto il C#; altre 3.748
  righe degli altri file stanno dentro `#if DEBUG`.

### Trovato in più

- **`--mostra-notifica` e la cartella dei dati** (letto): vedi C-04.
- **Il genitore 0** (misurato): vedi C-13. Il rapporto lo dava per un riferimento non più valido.
- **`FindAsync` di Gmail senza il controllo sul Message-Id vuoto** (letto): quello di IMAP non cerca se il Message-Id
  manca; quello di Gmail cercherebbe un'intestazione che contiene il testo vuoto. Non toccato: è la stessa differenza
  di C-11.
- **Dieci metodi lunghi che il rapporto non elenca**: vedi C-23.
- **C-12 allunga il file** di 8 righe invece di accorciarlo di 40: vedi il punto.
- **Il controllo dei nomi dei gruppi** (C-40) voleva "prove" in fondo al giro degli audit: ora si aspetta "residui"
  dopo "prove". Chi aggiunge un gruppo deve aggiornarlo.
- **Fine riga**: all'inizio, nell'albero di lavoro, 63 file `.cs` e `.xaml` avevano CRLF, 76 LF e 2 misti
  (`ActionQueue.cs`, `ViewModels.cs`). Nel repository sono tutti LF, quindi non conta; gli strumenti che riscrivono un
  file intero devono tenere quelli che trovano.
- **Le regole comuni e i due rapporti non erano più nello scratchpad**: il rapporto è stato letto dal ramo
  `origin/audit-codice` (`dfc7662`), senza copiarlo nel repository.

### Controlli e suite

- `CleanupAuditTests`, 8 controlli: C-13 (2), C-09, C-14, C-15, C-16, C-17, C-06. Tre righe di sola misura: C-13, C-09,
  C-06.
- **I nomi dei controlli, prima e dopo**: 1010 e 1018. Confrontati con lo script del terzo gruppo, tolte le cifre e i
  segni casuali.
  - Tolti: nessuno.
  - Aggiunti: gli 8 di `CleanupAuditTests`.
  - Cambiato con lo stesso testo (1): "C-40, i gruppi in una tabella sola": ora si aspetta "residui" in fondo al giro
    degli audit, e conta 68 nomi e 22 gruppi.
  - Nessun controllo è stato indebolito per farlo passare.
- Suite completa sul codice di `0e0f720` (quello di questo esito: dopo cambiano solo i documenti), subito dopo due
  compilazioni da capo: 1018 controlli, tutto bene, in 179 secondi. Un secondo giro: 1018, tutto bene, 173 secondi. Gli
  stessi nomi nei due.
- La suite all'inverso: 1018, tutto bene, 171 secondi, gli stessi nomi.
- Nessuna caduta dei controlli dati per instabili in questi giri. Il gruppo nuovo dura 1,7 secondi: quasi tutto è la
  scrittura dei duemila corpi finti di C-06.
- Compilazione da capo sul codice di `0e0f720` (`dotnet build --no-incremental`): Release 0 errori e 0 avvisi, Debug 0
  errori e 0 avvisi. `prova-release` non è stata ricostruita.
- Ogni commit è stato compilato e provato con i suoi gruppi prima di entrare, con uno script che rifiuta il commit se
  nell'albero di lavoro resta una modifica fuori dall'indice. Gli stati intermedi non sono stati provati con la suite
  intera, tranne `1f781f3` (dopo C-10): 1013 controlli, tutto bene, 168 secondi.

### Righe

Contate con `git diff --numstat` da `f83b0a0` a `0e0f720`.

- Codice di Missiva (`.cs` senza le prove): 512 righe tolte e 447 aggiunte, 65 in meno. Le aggiunte sono in buona parte
  i commenti dei metodi comuni.
- Prove: 449 aggiunte e 3 tolte, quasi tutte nel file nuovo.
- I quattro file grandi, prima e dopo: `MainWindow.xaml.cs` 5284 e 5277; `ComposeWindow.xaml.cs` 2143 e 2143;
  `ReadingView.xaml.cs` 1909 e 1909; `GraphProvider.cs` 1958 e 1966.
- Gli altri che cambiano di più: `GmailProvider.cs` 1540 e 1469, `ImapProvider.cs` 1791 e 1735, `CursorProbe.cs` 367 e
  257, `MessageRowElement.cs` 764 e 745, `DevTools.cs` 1053 e 1037.

### Che cosa cambia per chi usa Missiva

- **Il rosso degli errori nelle finestre** segue il tema: nello scuro è chiaro e si legge.
- **La finestra di Microsoft** (accesso, consenso) dopo aver chiuso una regola, le Impostazioni o una scrittura da cui
  si era chiesto un consenso si apre di nuovo sopra la finestra principale.
- Tutto il resto non si vede: stessi messaggi in uscita, stessi file, stessi tempi scaduti.
- Per chi usa gli strumenti: non ci sono più i sei interruttori della sonda del cursore né `--mostra-notifica`.

### Scelte fatte qui, da rivedere

- **Il genitore della finestra di Microsoft è l'ultima finestra che l'ha chiesto e si vede**, non la finestra attiva
  come proponeva il rapporto.
- **Il rosso degli errori è `CriticalBrush`**: nel tema chiaro cambia di pochissimo rispetto a prima.
- **La copia del calendario e i loghi si scrivono con un file temporaneo**: prima direttamente.
- **`PecInfo.IsReceipt` è tolta**, anche se `docs/audit/funzionalita.md` la nomina fra i pezzi pronti per le ricevute
  collegate: era una riga, e nessuno la usava. `PecInfo.Reference`, letta dall'intestazione e mai usata, resta.
- **`--mostra-notifica` è tolto**: per vedere com'è disposta una notifica resta "notifica N" in `MISSIVA_KEYS`.
- **I nomi nuovi** (`ImapSilenceMs`, `GmailSilenceMs`, `Extra`, `OpenOver`, `WriteCopy`, il gruppo `residui`) li ho
  scelti io. `App.StuckCheck` è diventato `internal` per una riga di misura.
- **`MISSIVA_PROVA_FILE_DI_PRIMA`** è una variabile nuova, letta solo dalle prove.
- **C-12 è fatto anche se non toglie righe.**

### Scelte per l'autore

In ordine di utilità.

1. **Il tempo scaduto di MailKit** (C-09): 60 secondi (account IMAP) o 120 (Gmail) per tutti e due? Proposta: 60. È il
   valore con cui gli account IMAP girano dal 26 settembre; quello di Gmail non l'ha scelto nessuno. Con 60 una
   connessione morta dopo la sospensione tiene ferma la principale un minuto invece di due, e `StuckCheck` (2 minuti)
   scatta dopo il tempo scaduto per tutti, non insieme. Il rischio: un'operazione in cui Gmail tace per più di un
   minuto (una ricerca in "Tutti i messaggi" di una casella grande, la risposta dopo una bozza con molti allegati)
   verrebbe interrotta e rifatta. Va guardato sulla casella vera prima di installarlo.
2. **La prova di `TestingProvider`** (C-65): proposta: sì, per prima. Un'ora o due, nessun rischio, e protegge la
   casella vera: oggi un metodo nuovo che scrive e che qualcuno dimentica di fermare scriverebbe davvero.
3. **Il modello vecchio della riga** (C-01): il confronto pixel per pixel è finito? Se sì si tolgono 173 righe di XAML,
   otto proprietà e la variabile, e l'interruttore `-Vecchia` dei due script di `loop\` non ha più senso. Proposta:
   toglierlo, dicendolo nei due script.
4. **Le divisioni dei file grandi** (C-19..C-22): proposta: in un giro loro, senza altre modifiche, in quest'ordine.
   `GraphProvider` (2,5 ore, il rischio più basso: le prove con il Graph finto coprono ogni area). Poi un file per
   quello che in `MainWindow` è solo Debug, il blocco di `StartAsync` compreso (mezz'ora secondo il rapporto: oggi le
   righe solo Debug di quel file sono 564). Poi il resto di `MainWindow` (5-6 ore), la scrittura (2 ore), il riquadro
   (1,5 ore). Prima di cominciare va scritto il conto dei membri che si userà prima e dopo.
5. **La base comune di Gmail e IMAP** (C-08, C-11): 1-2 giorni, rischio medio. Proposta: prima le due divergenze, da
   sole e con un controllo sul server finto: EXAMINE anche per gli allegati di Gmail, e `FindAsync` di Gmail come
   quello di IMAP. Poi la base. Restano 203 righe uguali in blocchi di almeno 8.
6. **Gli altri consensi nell'interfaccia** (C-13): otto copie di "prova, annullato, registro, spiegazione". Proposta:
   un aiuto solo, mezza giornata, insieme alle divisioni; chiede prove a finestra.
7. **`EncryptOld`** (C-06): toglierla, segnarla fatta con un file, o lasciarla? Proposta: segnarla fatta con un file,
   come `AuthenticateFromCopies`. Costa poco (6-8 ms a caldo), quindi non è urgente.
8. **Le copie "di prima"** (C-03): proposta del rapporto, risultati attesi scritti nelle prove al posto delle copie.
   Proposta: solo quando si tocca uno di quegli algoritmi. Per `SuggestOld` c'è un motivo in più: il suo controllo è
   quasi un quinto del tempo della suite.
9. **I nomi delle voci dei menu scritti due volte** (C-15): proposta: sì, 2-3 ore, con le foto dei menu prima e dopo.
10. **Lo scheletro comune delle finestre** (C-16): un giorno, sposta pixel. Proposta: non ora. Con lui i rossi rimasti
    fissi, se nello scuro danno fastidio: la riga dell'ora del calendario e l'icona dell'allegato pericoloso.
11. **Nascondi e rimetti la posta** (C-18): venti minuti più una prova a finestra (`--calendario`, `--persone` e
    ritorno). Proposta: con le divisioni.
12. **I metodi lunghi** (C-23): proposta: la tabella dei comandi al posto di `Press` (2 ore, solo Debug), poi gli
    altri quando si tocca il loro file.
13. **`MainWindow.QuickReply`** (C-05): tenerla per le tre prove, o riscrivere le prove senza? Proposta: tenerla.
14. **I comandi descritti solo in CLAUDE.md** (C-04): proposta: tenerli, e rivederli con la tabella dei comandi.
15. **Il codice di prova in un progetto a parte** (C-66): proposta: no.

### Non verificato

- **C-13, la finestra di Microsoft vera**: MSAL non ha un doppio di prova. Passi per l'autore, con un account Microsoft:
  1. Gestisci account, "Accedi di nuovo" (o, se il consenso non c'è ancora, il pulsante del consenso in Impostazioni,
     Posta, Regole): la finestra di Microsoft deve aprirsi sopra quella finestra.
  2. Chiudere quella finestra. Poi, dalla finestra principale, qualcosa che apre la finestra di Microsoft senza
     passare da un'altra finestra: "Consenti l'accesso" nel calendario o in Persone, se uno dei due consensi manca
     ancora. Deve aprirsi sopra la principale, non dietro e senza errori.
  3. Con un account di lavoro: "Cerca anche fra i colleghi" in una scrittura, chiudere la scrittura, e ripetere il
     passo 2.
  Se tutti i consensi ci sono già, il passo 2 non si può provare a comando: resta il caso di un token scaduto, che
  apre la finestra da solo. Che cosa faceva MSAL con un genitore 0 (finestra dietro, errore, o nulla) non lo so.
- **C-16 a finestra**, nei due temi: un errore in una delle quattordici finestre (di sicuro in Aggiungi account,
  Microsoft 365, con "Usa l'app della mia organizzazione" e un ID scritto male); e nelle Impostazioni, Regole, una
  regola che inoltra fuori, con testo e bordo rossi. Cambiando tema con la finestra aperta il rosso deve cambiare.
- **C-15 a finestra**: tasto destro su un messaggio e su una cartella (stesse voci e separatori di prima), e
  "Personalizza il menu…" che si apre.
- **C-02 con il cursore sulle righe**: `loop\strumenti\misura-cursore.ps1 -Collaudo` e una misura vera. I comandi che
  usa ci sono ancora; non è stato eseguito.
- **C-07**: "costo righe" e "scorri 120" prima e dopo.
- **C-10 sulla casella vera**: una risposta e un inoltro con allegati da Gmail e da un account IMAP. Sui server finti
  passano.
- **C-17 sui dati veri**: la versione nuova che legge la copia locale, i token e i contatti scritti da quella
  installata. Provato con tre file scritti dalla compilazione di prima, non con i dati dell'autore.
- **C-06**: il costo a freddo (PC appena acceso) e sui dati veri. Per sapere se c'è ancora qualcosa in chiaro, sul PC
  dell'autore: `Select-String -Path $env:LOCALAPPDATA\Missiva\missiva.log -Pattern 'ora cifrati'`; se l'ultima riga è
  vecchia di settimane, non c'è.
- **C-09**: nessun valore cambia, quindi nulla da provare finché non si sceglie.
- **La versione finale**: la Release compila senza avvisi; niente è stato installato.

## Audit dell'11 ottobre, codice, quinto gruppo: documentazione (C-67..C-71)

Fatto il 12 ottobre 2026 sulla sezione 10 di `docs/audit/codice.md`. "Prima" è il commit `7743a1f`. Commit: `b100e28`
(C-70), `9d274b8` (C-68), `a9299a4` (C-69), `b97ea28` (C-71), `495fbc4` (una prova resa indipendente dai tempi, trovata
provando la versione che si installa: vedi la sezione dopo), più quello di questo esito. Su cinque punti: tre corretti
(C-68, C-69, C-71), uno corretto in parte con due cose lasciate all'autore (C-70), uno rimisurato e lasciato (C-67).

Sono quasi solo documenti. Fuori dai documenti cambiano: un commento in `UI/ContactWindow.cs` (C-68), una riga in due
strumenti (`strumenti\lampi.ps1` e `scrivi.ps1`, C-71) e un controllo della suite (`495fbc4`). Nessuna riga del codice
di Missiva.

Come si è misurato:

- **Sul codice di oggi**, non su quello di `2084922` che il rapporto ha letto: per ogni frase, la riga del codice che
  la conferma o la smentisce. I numeri di riga del rapporto non valgono più (`CLAUDE.md` aveva 2.378 righe, all'inizio
  di questo gruppo 2.672): le frasi sono state cercate per parola.
- **Il controllo dei nomi**, rifatto con uno script mio (mai nel repository): ogni testo fra accenti gravi di
  `CLAUDE.md` (2.382 alla fine) e di `LEGGIMI.md` (24) cercato nei `.cs`, `.xaml`, `.ps1` e nel progetto. Dice che il nome c'è,
  non che il significato descritto sia ancora quello.
- **Argomenti e variabili**: gli argomenti `--...` letti dal codice (83) cercati in `CLAUDE.md`, `LEGGIMI.md` e negli
  script; le variabili `MISSIVA_` lette dal codice confrontate con quelle citate.
- **`installa.ps1` con Windows PowerShell 5.1**, senza eseguirlo: la sola analisi sintattica (`Parser.ParseFile`) con
  5.1.26100.9549 e con 7.6.6, e la sola riga che apre il canale per "esci" provata con i due su un canale di prova che
  non esiste (mai quello di Missiva).
- **I due strumenti** (C-71), senza eseguirli: analizzati, e il loro filtro del processo, preso dal file, applicato a
  quattro processi finti.
- Nessuna finestra, nessun avvio della versione di prova, nessuna rete; i dati veri e il registro vero mai letti. Mai
  `installa.ps1` né `disinstalla.ps1`.

### Punti del rapporto

- **C-67** (`CLAUDE.md` breve, con un file per area in `docs/`). CONFERMATO, RIMISURATO, LASCIATO: è una scelta dell'autore.
  - Oggi (contenuto nel repository, fine riga LF): 315.951 byte, 2.690 righe, circa 90 mila token a 3,5 caratteri per
    token. Al commit letto dal rapporto 279.642 byte e 2.378 righe; all'inizio di questo gruppo 313.249 e 2.672. Negli
    85 commit di questa verifica è cresciuto di 36 KB, il 13%: 22 sono le sette voci "Audit dell'11 ottobre".
  - Per sezione: "Stato e architettura" 275 KB, "Prove" 30 KB, le due in cima 1,5 KB l'una.
  - Le voci più lunghe (132 in tutto): "Fluidità dello scorrimento, Loop 4" 28,0 KB; "Solo in Debug" 24,5 KB, di cui
    19,2 sono la storia dei conteggi della suite ("1018 ... prima 1010 ... prima 258"); Persone 13,9; Calendario 13,9;
    "Scatti dello scorrimento" 9,2; "Lavoro senza rete" 8,4; "Messaggio aperto in un solo disegno" 8,3; "Suggerimenti
    dei destinatari" 7,7; le tredici voci che cominciano con "Audit" 43,2 KB in tutto. Il file rimanda 23 volte a
    `ESITO.md`.
  - Le ripetizioni che il rapporto elenca ci sono ancora; con questo gruppo l'elenco degli strumenti ha tre righe in più.
  - La proposta regge, e oggi costa un po' di più: il rapporto stima 5-8 ore per 280 KB, in proporzione 6-9 per 316.
    Rischio medio-basso: l'indice deve nominare le classi di ogni area, perché chi lavora sappia quale file aprire.
  - Non fatto qui: sposta 300 KB di testo che ogni agente usa, in un giro che doveva solo correggere le frasi.

- **C-68** (`CLAUDE.md`: frasi superate o in contraddizione con il codice). CONFERMATO, CORRETTO (`9d274b8`).
  Le tredici voci del rapporto, una per riga (la riga è quella di `CLAUDE.md` al commit `7743a1f`):
  1. Riga 131, "quel testo parte ancora ... poi 'Risposta inviata'". CONFERMATA: `NotificationActionAsync` apre sempre
     una scrittura (`OpenQuickReplyWindow`), e `MainWindow.QuickReply` resta solo per le prove. CORRETTA.
  2. Riga 1800, "senza nessuna finestra aperta apre l'app con la mail nel riquadro". CONFERMATA: `App.OnToast` apre
     sempre la finestra del messaggio. CORRETTA.
  3. Riga 678, "Il valore fisso è il 2". CONFERMATA: `WheelScrolling.Touch = TouchModels[3]`. CORRETTA.
  4. Riga 657, DirectManipulation "da provare come esperimento". CONFERMATA: provato e tolto il 30 settembre, lo dice lo
     stesso file settanta righe sotto. CORRETTA.
  5. Riga 276, "fino a 300 ms (`ShowDelay`)". CONFERMATA: 1.000 ms. CORRETTA.
  6. Riga 1050, "Non ancora: ... Gmail (Calendar API ...)". CONFERMATA: `Core/GmailProvider.Calendar.cs` c'è. CORRETTA.
  7. Riga 1452, contatti di Missiva per Gmail "finché Google Cloud non ha la People API". CONFERMATA: i contatti di
     Google ci sono, in lettura e in scrittura. CORRETTA.
  8. Riga 1936, `UI/MessageMenu.cs`. CONFERMATA: il file non esiste, il catalogo è in `UI/ContextMenus.cs`. CORRETTA.
  9. Riga 2585, la prova "limite di 3 MB". CONFERMATA: il limite non c'è più (F-15) e il controllo, che porta ancora quel
     nome, guarda solo che il salvataggio riesca. CORRETTA la frase; il nome del controllo non è cambiato.
  10. Righe 497 e 527, `FineSpread`. CONFERMATA: nel codice non c'è più (`SpringTau` sì). CORRETTE: ora lo dicono.
  11. Riga 1890, il tempo scaduto di MailKit "di 120 s". GIÀ CORRETTA dal quarto gruppo (C-09): vale per Gmail, 60 per
      gli account IMAP.
  12. Riga 629, `MISSIVA_RIGA_VECCHIA` "da togliere quando A ha finito". CONFERMATA che è ancora lì. CORRETTA la frase:
      resta perché due strumenti di `loop\` la usano, e toglierla è una scelta dell'autore (C-01).
  13. Il commento di `UI/ContactWindow.cs` ("gli account Microsoft in cui si può creare"). CONFERMATO: `Writable` dà
      anche gli account Google con l'ambito di scrittura. CORRETTO, due righe di commento.

  Le frasi storiche (3, 4, 5, 10) non sono state tolte: dicono che cosa valeva allora e che cosa vale oggi.

  Poi lo stesso controllo, a campione, sulle frasi che i blocchi di questi giorni possono aver reso false. Cercati in
  `CLAUDE.md` e in `LEGGIMI.md` i nomi delle cose cambiate: i domini noti dei loghi nella copia, il clic singolo su
  IMAP, il comando del mailto, `StartupHookSupport`, il pacchetto `ProtectedData`, gli interruttori della sonda,
  `--mostra-notifica`, i gruppi della suite e il nome sconosciuto, la cartella "giro-...", il tempo scaduto in
  `MailLimits`, e una trentina di nomi nuovi o tolti (`Unproven`, `ImagesOnce`, `MaybeSent`, `Owner`, `#C42B1C`,
  `FetchIfThereAsync`, `IsReceipt`...). I gruppi di prima avevano già aggiornato quasi tutto. Trovate e corrette cinque:
  1. `CLAUDE.md`, Prove: "`--prova-audit risultato.txt`" senza nome dato per "i controlli del blocco 1, con quelli del
     blocco 2, in pochi secondi". Oggi senza nome girano tutti i gruppi degli audit; il blocco 1 da solo è `html`.
  2. `LEGGIMI.md`: "Cambiando il mittente cambia anche la firma, se non l'hai modificata". Dopo C-52 "Nessuna firma"
     scelta a mano resta.
  3. `LEGGIMI.md`: "l'autorizzazione viene revocata anche su Google". Dopo C-32 si sa che può non riuscire: Missiva lo
     chiede, e se non riesce resta nel registro.
  4. `LEGGIMI.md`, Posta in uscita: mancava l'invio interrotto mentre il server riceveva il messaggio, che dopo C-61
     non ci va (c'era solo per gli invii pianificati).
  5. `LEGGIMI.md`, calendario: "I promemoria arrivano ... mentre Missiva è aperta". Dopo C-56 quelli scaduti a PC
     sospeso o senza rete arrivano in ritardo.

  Il controllo dei nomi: in `CLAUDE.md` 25 testi non si trovano nei sorgenti, e nessuno è un errore nuovo. Quattordici
  sono nomi di WPF e di .NET e uno di UI Automation; cinque sono cose fuori dal repository (`pix.ps1` e `PredictAdapt`
  in `loop\`, i due rapporti non ancora in main, il tag `missiva_bozza1`); due sono opzioni di Edge; tre il testo stesso
  li dà per tolti (`FineSpread`, `RemoteUrlFunction`, `--mostra-notifica`). In `LEGGIMI.md` nessuno.

- **C-69** (dati reali dentro `CLAUDE.md`). CONFERMATO, CORRETTO (`a9299a4`). Qui si dice solo il tipo e la riga (di
  `CLAUDE.md` al commit `7743a1f`).
  - L'identificativo della directory di Azure: righe 140 e 248. Nel codice non c'è.
  - Il valore dell'ID dell'app registrata: righe 139 e 244. Nel codice c'è, e serve
    (`MicrosoftAuth.DefaultClientId`): nel documento ora c'è quel nome.
  - Il dominio personale: righe 236, 242 e 257. Un indirizzo di posta su quel dominio: riga 257.
  - Il valore di un record TXT di verifica: riga 254.
  - In più del rapporto, il nome dell'account GitHub: righe 11, 237, 238 e 243 (anche dentro un indirizzo).
  - Al loro posto segnaposto e rimandi: "<directory>", "<dominio dell'autore>", `MicrosoftAuth.DefaultClientId`, `git remote
    -v` per l'indirizzo del repository. Una riga in "Come lavorare con l'autore" dice che cosa è uscito e dove si ritrova.
  - Dopo: nessuno di quei valori in `CLAUDE.md` né in `LEGGIMI.md`, nessun GUID in `CLAUDE.md`, e negli altri file
    tracciati non ce n'erano (cercati con `git grep`, contando soltanto).
  - Non spostati in una nota fuori dal repository, come proponeva il rapporto: i valori stanno dove vivono (portale di
    Azure, Cloudflare, GitHub) e nella storia di git, che non è stata riscritta.
  - Restano, perché non sono dati dell'autore: i nomi dei gestori (gmail.com, outlook.com, il mittente di sistema di un
    gestore di posta certificata) e gli indirizzi di prova inventati su due domini che non sono di prova, usati anche
    in dieci file del codice (nove delle prove, e i dati finti della finestra principale). Vedi "Scelte per l'autore".

- **C-70** (`LEGGIMI.md`: che cosa non corrisponde più). CONFERMATO salvo dove scritto, CORRETTO in parte (`b100e28`).
  I dieci punti del rapporto:
  1. La copia dei 30 giorni "solo quelli sotto 300 KB". CONFERMATO (`ImapProvider.Prefetchable`: anche testo e immagini
     nel testo dei messaggi più grandi, sotto 2 MB). CORRETTO.
  2. Requisiti "solo .NET 10 SDK". CONFERMATO: `installa.ps1` si lancia con `pwsh`. CORRETTO: PowerShell 7 è fra i
     requisiti. Che con 5.1 l'installazione fallisca, SOSPETTO nel rapporto: vedi sotto.
  3. "per Windows 11", con `SupportedOSPlatformVersion` 10.0.17763. CONFERMATO, LASCIATO all'autore.
  4. Le sezioni delle Impostazioni. CONFERMATO (`SettingsWindow.FillSections`). CORRETTO.
  5. Gli allegati temporanei. CONFERMATO (`Safety.CleanTemp`: all'uscita tutti, ogni minuto quelli fermi da un'ora,
     all'avvio quelli di più di un giorno). CORRETTO.
  6. "da Impostazioni, Apri la cartella dei registri". CONFERMATO: è nel menu del pulsante "Account e impostazioni"
     (`OnSettingsMenu`). CORRETTO.
  7. I loghi dei mittenti non citati. CONFERMATO. Aggiunti: una voce in "Cosa c'è" e una in Sicurezza, che dice che il
     sito di un'azienda vede la richiesta del logo (indirizzo IP e momento), non quale messaggio.
  8. "Annulla funziona come sempre". CONFERMATO in parte: vale finché la rete manca; dopo C-55, se lo spostamento è
     già partito, Annulla lo dice e rimette solo i messaggi non partiti. CORRETTO.
  9. Le risposte automatiche nel punto Persone. CONFERMATO, CORRETTO: un punto loro.
  10. I passi per un'app Microsoft propria con i soli permessi della posta. CONFERMATO. CORRETTO in parte: ora dicono
      quali permessi chiedono le altre funzioni (`MicrosoftAuth`: cinque elenchi). Che senza dichiararli il consenso
      funzioni lo stesso resta SOSPETTO, come nel rapporto.

  `installa.ps1` con Windows PowerShell 5.1.26100.9549, senza eseguirlo:
  - Nessun errore di sintassi, né con 5.1 né con 7.6.6. Il file non ha il BOM: 5.1 lo legge come ANSI e una stringa su
    162 arriva storpiata, il messaggio "il file è ancora in uso".
  - `PipeOptions.CurrentUserOnly` in 5.1 non esiste. La riga che apre il canale, provata su un canale di prova: con 5.1
    `New-Object` fallisce prima di `Connect`; con 7 il canale si crea. In `installa.ps1` quella riga sta in un `try`
    con un `catch` vuoto: con 5.1 "esci" non parte, e una Missiva aperta si chiude come con la X dopo 30 secondi e si
    termina dopo altri 8, senza le domande delle scritture aperte.
  - Quindi: "fallisce" non è dimostrato, e letto così non dovrebbe; ma chiude Missiva nel modo peggiore. `disinstalla.ps1`
    ha il BOM e usa `CurrentUserOnly` solo dove esiste (analizzato anche lui: nessun errore). `installa.ps1` non è
    cambiato: vedi "Scelte per l'autore".

- **C-71** (strumenti e argomenti non citati). CONFERMATO, CORRETTO (`b97ea28`).
  - `lampi.ps1` e `scrivi.ps1` cercavano il processo solo in `bin\Debug`. Ora hanno il filtro di `elenchi.ps1`. Sui
    processi finti: `lampi.ps1` sceglie la Debug e la versione finale di prova con una finestra, `scrivi.ps1` anche
    quella senza finestra, come `elenchi.ps1`; nessuno dei due la Missiva installata.
  - In `CLAUDE.md`, nell'elenco degli strumenti: `scrivi.ps1`, mai citato; `avvio.ps1` e `ingresso.ps1`, citati solo
    altrove. "Gli altri strumenti la riconoscono" ora è vero.
  - `MISSIVA_REGISTRO_SUBITO`: letta dal codice (solo nelle versioni di prova) e mai detta in `CLAUDE.md`. Ora c'è.
  - `--mostra-notifica`: l'ha tolto il quarto gruppo (C-04).
  - Rifatto il conto: gli 83 argomenti letti dal codice sono tutti citati. Delle variabili `MISSIVA_` lette mancava solo
    quella; `MISSIVA_PROVA_TOKEN` la scrive e la legge una prova. Le sei variabili della sonda tolte da C-02 sono
    ancora citate, e il testo dice che non ci sono più.
  - `docs/prove.md` non è nato: va con C-67.

### Trovato in più

- **Il nome dell'account GitHub** in `CLAUDE.md`, quattro volte: il rapporto non lo contava. Tolto con C-69.
- **`installa.ps1` con 5.1 non manda "esci"**: vedi C-70.
- **Un controllo che dipende dai tempi**: "nella prova Domani e Segna come completato vanno solo nel registro". Vedi la
  sezione dopo, "La versione che si installa".
- **Cinque frasi dei due documenti** rese false, o incomplete, dai blocchi di questi giorni: vedi C-68.
- **La riga 45 di `CLAUDE.md`** dice che il blocco del registro di `installa.ps1` non è mai stato provato sul PC di
  L'autore. Dal 5 ottobre la versione installata c'è, quindi quel blocco è girato; se Missiva compaia fra le app di posta
  non si può vedere da qui (il registro vero non si legge). Non toccata.
- **Il titolo del commit di C-70** dice "nove punti ... e i loghi": i punti corretti sono nove contando i loghi.
- `Missiva.exe` della versione finale con gli strumenti di prova pesa 273.717.965 byte prima e dopo il commit della
  prova: uguale al byte. Che dentro ci sia il codice nuovo è stato controllato cercando un nome nuovo nel file.

### Controlli e suite

- Nessun controllo nuovo: sono documenti. La suite resta a 1018 controlli, con gli stessi nomi.
- Cambiato un controllo, con lo stesso testo: "nella prova Domani e Segna come completato vanno solo nel registro"
  cerca le due righe fra quelle scritte dopo, invece che nelle ultime due del registro (`495fbc4`). Non è più largo:
  devono essere due, quelle, in quell'ordine. Visto con una riga messa apposta fra le due chiamate: quello di prima
  cade, quello nuovo passa; senza la seconda chiamata quello nuovo cade.
- I numeri dell'ultimo giro sono in fondo alla sezione dopo.

### Che cosa cambia per chi usa Missiva

- Nulla: il programma è lo stesso.
- Per chi usa gli strumenti: `lampi.ps1` e `scrivi.ps1` trovano anche la versione avviata con `prova.ps1 -Release`.
- Per chi legge: `LEGGIMI.md` chiede PowerShell 7, parla dei loghi dei mittenti e dice che cosa vede il sito di
  un'azienda; `CLAUDE.md` non ha più identificativi veri.

### Scelte fatte qui, da rivedere

- **Il nome dell'account GitHub è uscito da `CLAUDE.md`** insieme agli altri, anche dalla riga del repository: al suo
  posto "lo dice `git remote -v`".
- **Le frasi storiche sono annotate, non tolte** (C-68).
- **Nella sezione Sicurezza di `LEGGIMI.md`** c'è scritto che la richiesta del logo mostra al sito indirizzo IP e
  momento. È vero, ed è la prima volta che il documento lo dice.
- **`winget install Microsoft.PowerShell`** fra i requisiti: il comando non è stato eseguito qui.
- **I permessi in più per un'app Microsoft propria** sono elencati come "aggiungili se li vuoi usare": che servano
  davvero non è provato.
- **Due strumenti cambiati senza eseguirli** (C-71): la stessa riga degli altri quattro, analizzata e provata su
  processi finti.
- **Un controllo della suite cambiato** in un gruppo che doveva toccare solo documenti.
- **`docs/prove.md` non esiste**: gli strumenti sono citati in `CLAUDE.md`, che così cresce ancora di poco.

### Scelte per l'autore

- **`CLAUDE.md` breve** (C-67): vuoi la riorganizzazione? Oggi ogni sessione, e ogni agente, parte con circa 90 mila
  token di `CLAUDE.md`. Proposta: in due passi, ognuno in un giro suo senza altre modifiche. Il primo, 1-2 ore e
  rischio basso, è solo uno spostamento: la storia dei conteggi della suite (19 KB), le due voci sullo scorrimento con
  le loro misure (37 KB) e le tredici voci "Audit" (43 KB) vanno così come sono in `docs/storia/`, lasciando poche
  righe con lo stato di oggi; il file scende di circa un terzo. Il secondo è quello del rapporto, un file per area e
  un indice di 11-13 KB: una giornata, rischio medio-basso.
- **Windows 11 o anche Windows 10** (C-70): `LEGGIMI.md` dice Windows 11, il progetto dichiara Windows 10 1809, e su
  Windows 10 Missiva non è mai stata provata. Proposta: resta "Windows 11" nel documento; il progetto si alza a
  Windows 11 solo se vuoi che lo dica anche lui.
- **`installa.ps1` e Windows PowerShell 5.1** (C-70): renderlo compatibile come `disinstalla.ps1` (BOM, e
  `CurrentUserOnly` solo dove esiste)? Proposta: no finché installi solo tu, che hai PowerShell 7; sì prima di darlo
  ad altri. Sono due righe, ma vanno provate con un'installazione vera.
- **Indirizzi di prova su domini che non sono di prova** (C-69): indirizzi inventati (il dominio `.it` di uno studio
  finto, un indirizzo di un gestore di posta personale) in `CLAUDE.md` e in dieci file del codice. Proposta: passarli
  a `.example` quando si toccano quelle prove; oggi no, cambierebbe il testo di qualche controllo.

### Non verificato

- **`installa.ps1` con 5.1 eseguito davvero**: solo analizzato. Non serve provarlo se resta il requisito.
- **`lampi.ps1` e `scrivi.ps1` con la versione finale di prova aperta**. Passi per l'autore: `prova.ps1 -Release`, poi
  `strumenti\scrivi.ps1 -Text "prova"` (deve dire "scritto 'prova' in SearchBox") e `strumenti\lampi.ps1 -Keys file
  -Steps "clic 2"` (deve elencare qualche fotogramma, non un errore sulla finestra).
- **`LEGGIMI.md` a finestra**: le sezioni delle Impostazioni e la voce "Apri la cartella dei registri" sono lette dal
  sorgente, non viste.
- **C-70, punto 10**: il consenso a calendario o contatti con un'app propria che dichiara solo i permessi della posta.
- **`winget install Microsoft.PowerShell`**, e PowerShell 7 dal Microsoft Store.
- **C-69**: se quei valori compaiono in file fuori dal repository (`C:\Progetti\Missiva\sito`, `loop\`): non guardato.

## Audit dell'11 ottobre: riepilogo, scelte per l'autore e prove a mano

Chiude la verifica dei due rapporti dell'11 ottobre 2026 (`docs/audit/sicurezza-2.md`, 36 punti T-, e
`docs/audit/codice.md`, 71 punti C-), fatta in otto gruppi fra l'11 e il 12 ottobre: le otto sezioni qui sopra. Questa
sezione non aggiunge fatti: li raccoglie. Dove dice una cosa e una sezione ne dice un'altra, vale la sezione.

Nulla è installato e nulla è stato inviato a GitHub: main è 87 commit sopra `origin/main` (`2084922`).

### 1. Numeri

| Rapporto | Punti | CORRETTI | CORRETTI in parte | LASCIATI | FALSI o trascurabili | NON VERIFICATI |
|---|---|---|---|---|---|---|
| Sicurezza (T-01..T-36) | 36 | 23 | 8 | 4 | 1 | 0 |
| Codice (C-01..C-71) | 71 | 35 | 15 | 18 | 3 | 0 |
| Trovati dai gruppi (X-1, X-2) | 2 | 2 | 0 | 0 | 0 | 0 |

- Sicurezza. Lasciati: T-06, T-20, T-26, T-34. Falso su questo PC: T-30. In parte: T-05, T-08, T-10, T-12, T-13, T-17,
  T-23, T-25.
- Codice. In parte: C-04, C-09, C-13, C-15, C-16, C-30, C-33, C-37, C-39, C-42, C-44, C-46, C-47, C-53, C-70.
  Lasciati: C-01, C-03, C-08, C-11, C-18, C-19, C-20, C-21, C-22, C-23, C-28, C-48, C-51, C-63, C-64, C-65, C-66,
  C-67. Falso: C-36. Confermati nel codice ma trascurabili per la misura: C-06 (6-8 ms a ogni avvio) e C-31 (l'effetto
  descritto non c'è).
- Nessun punto è rimasto NON VERIFICATO per intero: ognuno è stato riprodotto con un controllo o una misura, o letto
  nel codice dove una prova non si poteva fare. Quello che non si è potuto provare (finestre, server veri, dati veri) è
  nelle parti "Non verificato" di ogni sezione e, raccolto, nel punto 5.
- Affermazioni false dentro punti confermati: T-17 (b) come scritto; il 25 ottobre di C-56 (nessun promemoria perso);
  il minuto di attesa di C-45; il sospetto di C-44 che una prova approfitti dei ganci di un'altra; il riferimento "di
  una finestra distrutta" di C-13; `OfflineCopy.TestFull` "mai usato" (C-05); il comando "disegni dopo arrivo" (C-04).
- Trovato in più dai gruppi e corretto, oltre a X-1 e X-2: le impostazioni importate che non facevano più partire
  Missiva (con T-16), la pulizia dei corpi che cancellava fuori dai dati (con T-14), la `url(` mai chiusa e gli
  attributi SVG letti come CSS (con T-01), il carattere-indirizzo in firme, modelli e impostazioni (con T-31), il
  promemoria del lunedì mattina (con C-56), l'invio da un provider chiuso (con X-1), due dipendenze dall'ordine nella
  suite (con C-49).
- Suite `--prova-bozze`: 870 controlli prima (l'8 ottobre, sul commit dei rapporti), 1018 dopo. Per gruppo: 894, 920,
  945, 974, 999, 1010, 1018, e ancora 1018 dopo il quinto, che non ne aggiunge. Sono 148 controlli in più, in sette
  file nuovi `Core/ProvaBozze.*.cs`, ognuno avviabile da solo con `--prova-audit risultato.txt nome`.
- Avvisi del compilatore: 11 in Debug e 9 in Release più NU1510 prima, zero dopo (C-37).
- Commit: 87 sopra `origin/main`. 25 per la sicurezza (10, 7 e 8 nei tre gruppi), 55 per i primi quattro gruppi
  del codice (16, 15, 10 e 14), 7 per il quinto gruppo e questo riepilogo.

### 2. I 71 punti dell'audit del codice, con X-1 e X-2

La tabella dei 36 punti T- è in fondo alla sezione del terzo gruppo della sicurezza ("Riepilogo del secondo audit di
sicurezza"): non è ripetuta qui.

| Punto | In breve | Esito | Commit |
|---|---|---|---|
| C-01 | Il modello vecchio della riga dell'elenco | CONFERMATO, LASCIATO: lo usano due strumenti di `loop\` | nessuno |
| C-02 | Interruttori della sonda del cursore | CONFERMATO, CORRETTO (tolti) | `72e0652` |
| C-03 | Copie "di prima" tenute per il confronto | CONFERMATO, LASCIATO: le usano i controlli | nessuno |
| C-04 | Comandi e variabili di prova senza un uso noto | CONFERMATO per uno (`--mostra-notifica`), CORRETTO; gli altri sono usati | `55f7885` |
| C-05 | Membri mai usati | CONFERMATO per tre, CORRETTO | `ad91823` |
| C-06 | Migrazione che gira a ogni avvio | CONFERMATO, TRASCURABILE (6-8 ms), LASCIATO | `0e0f720` (misura) |
| C-07 | Assegnazioni e parametri inutili | CONFERMATO, CORRETTO | `fb67542` |
| C-08 | Gmail e IMAP scritti due volte | CONFERMATO, LASCIATO (decisione già presa) | nessuno |
| C-09 | Tempo scaduto di MailKit diverso fra Gmail e IMAP | CONFERMATO, CORRETTO in parte: i nomi; il valore all'autore | `268b0a3` |
| C-10 | `BuildAsync` identico in Gmail e IMAP | CONFERMATO, CORRETTO | `1f781f3` |
| C-11 | Bozze di Gmail e IMAP | CONFERMATO, LASCIATO (va con C-08) | nessuno |
| C-12 | Graph: cinque copie del consenso | CONFERMATO, CORRETTO | `561c06e` |
| C-13 | Consenso nell'interfaccia, e il genitore della finestra di Microsoft | CORRETTO in parte: il genitore; le otto copie lasciate | `c0c01fb` |
| C-14 | Google: la chiusura dell'accesso tre volte | CONFERMATO, CORRETTO | `ffe2f33` |
| C-15 | Menu del tasto destro | CORRETTO in parte: il ciclo; i nomi doppi lasciati | `69e2f86` |
| C-16 | Finestre con lo stesso scheletro, e un rosso fisso | CORRETTO in parte: il rosso; lo scheletro lasciato | `ba933b0` |
| C-17 | Scritture fuori da `SafeFile`, DPAPI in più file | CONFERMATO, CORRETTO dove il file resta identico | `2b132d1` |
| C-18 | Doppioni minori nell'interfaccia | CONFERMATO, LASCIATO: la suite non lo copre | nessuno |
| C-19 | `MainWindow.xaml.cs` troppo grande | CONFERMATO, LASCIATO (decisione già presa) | nessuno |
| C-20 | `ComposeWindow.xaml.cs` troppo grande | CONFERMATO, LASCIATO | nessuno |
| C-21 | `ReadingView.xaml.cs` troppo grande | CONFERMATO, LASCIATO | nessuno |
| C-22 | `GraphProvider.cs` troppo grande | CONFERMATO, LASCIATO | nessuno |
| C-23 | I metodi più lunghi | CONFERMATO, LASCIATO | nessuno |
| C-24 | Nessun gestore per gli errori fuori dall'interfaccia | CONFERMATO, CORRETTO | `daba973` |
| C-25 | Avvio: chi aspetta gli account resta appeso | CONFERMATO nel codice, CORRETTO | `bf59f0d` |
| C-26 | Azioni delle notifiche: l'uscita saltata | CONFERMATO nel codice, CORRETTO | `bf59f0d` |
| C-27 | `RuleWindow` dal pool, `CategoryPalette.Known` senza lucchetto | CONFERMATO, CORRETTO | `db8fc44` |
| C-28 | Un solo lucchetto dei token Microsoft | CONFERMATO nel codice, LASCIATO | nessuno |
| C-29 | Processo dei PDF dopo il tempo scaduto | CONFERMATO, CORRETTO | `2dd436f` |
| C-30 | Contatti: lucchetto durante DPAPI e scrittura | CORRETTO in parte: la copia sotto il lucchetto; il resto è B-14, lasciato | `d4f8a56` |
| C-31 | CancellationToken non passati | CONFERMATO, senza l'effetto descritto, LASCIATO | `880948a` (misura) |
| C-32 | `catch` vuoti che nascondono qualcosa | CONFERMATO, CORRETTO | `d16f6c6` |
| C-33 | `async void` senza protezione | CORRETTO in parte: lo stato fermo; l'errore non si perdeva | `08cbaa8` |
| C-34 | `domini.dat` non salvato all'uscita | CONFERMATO, CORRETTO | `45aac8f` |
| C-35 | `throw ex` nel riquadro | CONFERMATO, CORRETTO | `ba3060b` |
| C-36 | WebView2 delle finestre dei messaggi chiuse | FALSO | `fee4ac6` (controllo) |
| C-37 | Gli avvisi del compilatore | CORRETTO in parte: zero avvisi; farne errori lasciato all'autore | `cc836be` |
| C-38 | Soglie scritte in più posti | CONFERMATO, CORRETTO | `4ff5e18` |
| C-39 | Soglie che devono restare coerenti | CORRETTO in parte: `StuckCheck` e le due finestre della ricerca lasciati | `9ebcaac` |
| C-40 | Un solo try per la suite, nomi sconosciuti | CONFERMATO, CORRETTO | `2d6d81c` |
| C-41 | 6 SEARCH invece di 5 | CONFERMATO, CORRETTO | `2905bca`, `ae867c1` |
| C-42 | Controlli con soglie di tempo | CORRETTO in parte: due senza più tempi; le altre soglie lasciate con il margine | `ecd44c4` |
| C-43 | Controlli che passano per il motivo sbagliato | CONFERMATO, CORRETTO | `c0c73c6` |
| C-44 | Ganci statici di prova mai rimessi a posto | CORRETTO in parte: il riposo fra i gruppi; il sospetto FALSO | `2d6d81c` |
| C-45 | Server IMAP finto: eccezioni perse | CONFERMATO in parte (il minuto di attesa FALSO), CORRETTO | `c0f7e5c` |
| C-46 | Aiuti ripetuti e controlli doppi | CORRETTO in parte: `Until`; il resto lasciato | `2d6d81c` |
| C-47 | Parti senza copertura | CORRETTO in parte: due controlli scritti; il resto lasciato | `20a087b` |
| C-48 | Tempo della suite | MISURATO, LASCIATO | nessuno |
| C-49 | Stato che resta da un giro all'altro | CONFERMATO, CORRETTO; la divisione dei due file lasciata | `2d6d81c` |
| C-50 | Sposta, Archivia, Elimina rifiutati: righe sparite | CONFERMATO, CORRETTO | `2a6ce9b` |
| C-51 | Regole di Missiva dopo un errore a metà | CONFERMATO, LASCIATO | `44abc71` (misura) |
| C-52 | "Nessuna firma" annullata cambiando il Da | CONFERMATO nel codice, CORRETTO | `83cf803` |
| C-53 | Provider chiusi sotto le richieste in corso | CORRETTO in parte: la chiusura aspetta; il resto con X-1 | `64c80cd`, `1e6665c` (misura) |
| C-54 | Rimuovi account e Posta in uscita | CONFERMATO nel codice, CORRETTO | `81d5826` |
| C-55 | Annulla di uno spostamento partito in parte | CONFERMATO, CORRETTO | `9b007b6` |
| C-56 | Promemoria del calendario persi | CONFERMATO e più largo, CORRETTO | `b2ab076` |
| C-57 | Durate misurate con l'orologio | CONFERMATO, CORRETTO | `5a056af` |
| C-58 | Promemoria del contrassegno cancellato prima del controllo | CONFERMATO l'ordine, CORRETTO | `8a1414b` |
| C-59 | Orari in un fuso estero a un'ora che non esiste | CONFERMATO, CORRETTO | `61e8775` |
| C-60 | La finestra "una sola cartella" senza i messaggi nuovi | CONFERMATO nel codice, CORRETTO | `26fbe2c` |
| C-61 | Invii: un errore dopo il DATA fa ripartire il messaggio | CONFERMATO, CORRETTO | `748c7a5` |
| C-62 | Filtri della ricerca | CONFERMATO, CORRETTO | `0683d3c` |
| C-63 | Colore del testo secondario | CONFERMATO l'uso misto, LASCIATO | `44abc71` (misura) |
| C-64 | Semplificazioni senza rischio | LASCIATO come punto: fatte quelle di C-07, C-12, C-13, C-15, C-27 | con quelli |
| C-65 | `IMailProvider` e `TestingProvider` | CONFERMATO, LASCIATO | nessuno |
| C-66 | Codice di prova nell'eseguibile | CONFERMATO, LASCIATO | nessuno |
| C-67 | `CLAUDE.md` breve | CONFERMATO, rimisurato, LASCIATO | nessuno |
| C-68 | `CLAUDE.md`: frasi superate | CONFERMATO, CORRETTO | `9d274b8` |
| C-69 | Dati reali dentro `CLAUDE.md` | CONFERMATO, CORRETTO | `a9299a4` |
| C-70 | `LEGGIMI.md`: che cosa non corrisponde più | CORRETTO in parte: nove punti su dieci; Windows 11 all'autore | `b100e28` |
| C-71 | Strumenti e argomenti non citati | CONFERMATO, CORRETTO | `b97ea28` |
| X-1 | Una scrittura aperta mentre gli account si ricaricano | CONFERMATO, CORRETTO | `56b4d4e` |
| X-2 | `missiva-errori.log` nella cartella vera dalle versioni di prova | CONFERMATO nel codice, CORRETTO | `fd59ee1` |

### 3. Scelte per l'autore

Tutte quelle delle otto sezioni, senza doppioni, per argomento. In cima quelle che cambiano qualcosa che vedi ogni
giorno. Per ognuna: la domanda, la proposta di chi ha verificato il punto, e il punto.

**Posta, invio, notifiche: quello che vedi ogni giorno**

1. **Tempo scaduto di MailKit** (C-09, con `StuckCheck` di C-39): 60 secondi come gli account IMAP o 120 come Gmail,
   per tutti e due? Proposta: 60, dopo averlo guardato sulla casella vera (una ricerca in "Tutti i messaggi" di una
   casella grande potrebbe tacere per più di un minuto e venire rifatta). Con 60 una connessione morta dopo la
   sospensione tiene ferma Gmail un minuto invece di due.
2. **Invio interrotto mentre il server riceveva il messaggio** (C-61): fermarsi e dirlo, com'è ora, o riprovare una
   volta sola? Proposta: fermarsi. L'alternativa dà un doppione possibile, mai una copia al minuto.
3. **Promemoria del calendario in ritardo** (C-56): vuoi un tetto (solo le riunioni non ancora finite) e "era alle ..."
   nel testo? Proposta: il tetto sì, se il lunedì mattina ne arrivano troppi insieme.
4. **Immagini negli .eml allegati a un messaggio normale** (T-02): per un mittente consentito devono caricarsi da
   sole? Proposta: no, come nella Posta indesiderata: il From di un .eml non lo verifica nessuno. Costa un clic su
   "Mostra immagini" quando apri un messaggio inoltrato come allegato.
5. **Pagine web allegate** (T-10): `.html`, `.htm`, `.svg` e `.mht` devono chiedere conferma prima di aprirsi nel
   browser? Proposta: sì, con un testo suo ("è una pagina web arrivata per posta: non scriverci password"), senza il
   rosso dei tipi che avviano programmi.
6. **Colore del testo secondario** (C-63): dei 24 punti con il grigio di Fluent, quali vuoi con quello di Missiva?
   Proposta: tutti, cominciando dalla graffetta della riga, con una foto per tema prima e dopo.
7. **Una firma scelta a mano e poi un altro Da** (C-52): deve restare quella scelta? Proposta: no, com'è ora: le firme
   sono dell'indirizzo, e resta solo "Nessuna firma".
8. **Le due finestre della ricerca** (C-39): Invio riusa la ricerca dei suggerimenti per 30 secondi, IMAP riusa la sua
   per 60. Quale vale? Proposta: 30 per tutti e due, così "si rifà" è vero.
9. **Conferma di Annulla iscrizione** (T-13): aggiungere "il mittente saprà che questo indirizzo è attivo"? Proposta:
   no: dove confermarlo è un rischio il comando non c'è già, e Outlook e Gmail non lo dicono.
10. **Mittente autenticato anche senza DMARC** (T-06, insieme alla scelta in attesa dal blocco 2 del 4 ottobre): per
    loghi e clic singolo chiedere DMARC superato? Proposta: sì, chiude tutti e due i punti; costa qualche logo.
11. **Account IMAP** (T-03): vuoi il lavoro più lungo per riavere dominio autenticato, loghi e clic singolo sui server
    che scrivono davvero la loro intestazione? Proposta: solo se usi un account IMAP normale; va provato su quel server.
12. **Seconda cartella di indesiderata su IMAP** (T-08): una cartella "Spam" o "Junk" in cima, oltre a quella segnata
    dal server, va trattata come Posta indesiderata? Proposta: sì per immagini, loghi e rubrica, senza cambiare Svuota,
    che lì cancella per sempre.
13. **Azioni delle notifiche** (T-26): devono valere solo per i messaggi notificati in questa sessione? Proposta: no: i
    pulsanti di una notifica rimasta da ieri, o con Missiva chiusa, aprirebbero il messaggio invece di agire.
14. **Revoca di Google non riuscita** (C-32): vuoi che Rimuovi account lo dica a schermo, con dove toglierla a mano?
    Proposta: sì, una riga nell'avviso dopo la rimozione. Oggi è solo nel registro.
15. **Regole di Missiva dopo un errore a metà** (C-51): vuoi il lavoro a parte (ogni gruppo di azioni per conto suo,
    l'errore nell'esito, i messaggi non finiti riprovati al controllo dopo)? Proposta: sì, mezza giornata con le sue
    prove. Oggi i messaggi di quel giro non passano più dalle regole.
16. **Regole di Outlook che inoltrano fuori** (T-34): vuoi un avviso quando ne compare una? Proposta: sì, come lavoro a
    parte: le regole lette all'avvio e una volta al giorno, e una notifica la prima volta.
17. **Un messaggio aperto in una finestra sua, dopo un ricarico degli account** (X-1): deve passare al provider nuovo
    come fa la scrittura? Proposta: sì, al prossimo giro. Oggi dà un errore che dice di chiudere e riaprire.
18. **Sottocartelle sui server che separano con il punto** (trovato con C-47): dopo una rinomina `MigrateFolderKeys`
    deve usare il separatore dell'account? Proposta: sì, una riga più un controllo; oggi su quei server Preferiti e
    colori delle sottocartelle si perdono.
19. **Il server dei pezzi degli allegati grandi** (T-23): limitare il caricamento a quel server? Proposta: sì, al
    prossimo giro. Serve il suo nome, che il registro scrive dopo un allegato da più di 3 MB con un account Microsoft.
20. **La firma DKIM che copre le due intestazioni di Annulla iscrizione** (T-12): vuoi il lavoro più lungo? Proposta:
    no.

**Copia di sicurezza**

21. **Regole di una copia** (T-17): devono arrivare spente, da riaccendere a mano? Proposta: no, restano accese: ora
    si leggono in parole prima di importare, e quelle per tutti i messaggi sono in cima.
22. **Rubrica dei destinatari** (T-17): deve entrare nella copia? Proposta: sì, ma con i conteggi azzerati, così un
    nome noto da un indirizzo nuovo accende ancora la fascia dei sosia.
23. **File messi da parte dopo un'importazione** (T-20): devono perdere gli accessi degli account, o serve un pulsante
    "Elimina i dati di prima"? Proposta: il pulsante.
24. **Modelli che non si decifrano** (T-21): vanno nascosti dal pannello Modelli della scrittura? Proposta: sì,
    restano solo nelle Impostazioni, dove si eliminano.
25. **Voci delle immagini per interi servizi** (T-17): toglierle anche a ogni avvio, non solo all'importazione?
    Proposta: sì, se nelle tue impostazioni non ce ne sono (non guardato: sono i dati veri).
    - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".

**Installazione, versioni di prova, documenti**

26. **La Debug avviata senza `MISSIVA_DATA`** (T-28): nella sua cartella lavora ancora sui dati veri. Deve chiudersi
    sempre, come la versione finale di prova? Proposta: sì, se non la avvii mai a mano senza `prova.ps1`.
27. **Windows 11 o anche Windows 10** (C-70): `LEGGIMI.md` dice Windows 11, il progetto dichiara Windows 10 1809, e su
    Windows 10 Missiva non è mai stata provata. Proposta: resta "Windows 11".
28. **`installa.ps1` e Windows PowerShell 5.1** (C-70): renderlo compatibile, come `disinstalla.ps1`? Proposta: no
    finché installi solo tu; sì prima di darlo ad altri (oggi con 5.1 non manda "esci" e termina la Missiva aperta).
29. **`CLAUDE.md` breve** (C-67): oggi sono 316 KB, circa 90 mila token a ogni sessione. Proposta: in due passi. Prima,
    in 1-2 ore, spostare così come sono in `docs/storia/` la storia dei conteggi, le misure dello scorrimento e le voci
    degli audit (circa un terzo del file). Poi, in una giornata, un file per area con un indice di 11-13 KB.
30. **Avvisi di nullità come errori** (C-37): ora che sono zero, un avviso nuovo deve fermare la compilazione?
    Proposta: sì, ma solo in Debug, così `installa.ps1` non si ferma mai per questo.
31. **Indirizzi di prova su domini che non sono di prova** (C-69): passarli a `.example`? Proposta: quando si toccano
    quelle prove.
32. **I cinque commit che non compilano da soli** (terzo gruppo del codice): rifarli prima dell'invio a GitHub?
    Proposta: no. Hanno sopra più di sessanta commit, e rifarli vuol dire riscrivere tutta quella storia; `ae867c1`
    rimette tutto a posto e il suo messaggio lo dice.

**Lavori sul codice che non si vedono**

33. **Una prova per `TestingProvider`** (C-65): proposta: sì, per prima. Un'ora o due, nessun rischio, e protegge la
    casella vera: oggi un metodo nuovo che scrive e che nessuno ferma scriverebbe davvero dalla versione di prova.
34. **Il modello vecchio della riga** (C-01): il confronto pixel per pixel è finito? Proposta: toglierlo (173 righe di
    XAML, otto proprietà, la variabile), dicendolo nei due script di `loop\` che lo usano.
35. **La divisione dei quattro file grandi** (C-19..C-22): proposta: in un giro loro, senza altre modifiche:
    `GraphProvider` (2,5 ore), poi quello che in `MainWindow` è solo Debug, poi il resto di `MainWindow` (5-6 ore), la
    scrittura (2), il riquadro (1,5).
36. **La base comune di Gmail e IMAP** (C-08, C-11): proposta: prima le due divergenze, da sole e con un controllo
    (EXAMINE anche per gli allegati di Gmail; la ricerca della bozza di Gmail come quella di IMAP), poi la base: 1-2
    giorni, rischio medio.
37. **Gli altri consensi nell'interfaccia** (C-13): otto copie di "prova, annullato, registro, spiegazione". Proposta:
    un aiuto solo, mezza giornata, insieme alle divisioni; chiede prove a finestra.
38. **La cifratura delle copie in chiaro a ogni avvio** (C-06): toglierla, segnarla fatta con un file, o lasciarla?
    Proposta: segnarla fatta con un file. Non è urgente.
39. **Le copie "di prima" tenute per i confronti** (C-03): proposta: toglierle solo quando si tocca uno di quegli
    algoritmi.
40. **I nomi delle voci dei menu scritti due volte** (C-15): proposta: sì, 2-3 ore, con le foto dei menu prima e dopo.
41. **Lo scheletro comune delle finestre** (C-16): proposta: non ora (un giorno, sposta pixel). Con lui i due rossi
    rimasti fissi: la riga dell'ora nel calendario e l'icona dell'allegato pericoloso.
42. **Nascondi e rimetti la posta** (C-18): proposta: con le divisioni.
43. **I metodi lunghi** (C-23): proposta: la tabella dei comandi al posto di `DevTools.Press` (2 ore, solo Debug), poi
    gli altri quando si tocca il loro file.
44. **`MainWindow.QuickReply`** (C-05): tenerla per tre prove, o riscrivere le prove? Proposta: tenerla.
45. **I comandi di prova descritti solo in `CLAUDE.md`** (C-04): proposta: tenerli.
46. **Il lucchetto dei token Microsoft** (C-28): uno per account? Proposta: no per ora: serve solo con due account
    Microsoft e una finestra di accesso lasciata aperta, e non si prova senza due account veri.
47. **Il token al collegamento IMAP** (C-31): proposta: non toccarlo (misurato: passarlo farebbe ripartire il
    collegamento a ogni clic).

**La suite di prove**

48. **Copertura che manca** (C-47): proposta: i primi due dell'elenco: crea, rinomina, sposta ed elimina una cartella
    sul server IMAP finto (mezza giornata), e `MarkAllReadAsync` con i non letti (un'ora).
49. **Tempo della suite** (C-48): vuoi che si accorci? Proposta: solo `SuggestIndexAuditTests`, quasi un quinto del
    totale; è togliere casi a un controllo.
50. **I due file grandi delle prove e gli aiuti comuni** (C-46, C-49): proposta: la divisione sì, per spostamento
    puro, in un giro suo; gli aiuti no.
51. **Ganci di prova con `IDisposable`** (C-44): proposta: no, il riposo fra i gruppi basta.
52. **Il codice di prova in un progetto a parte** (C-66): proposta: no.

In attesa da prima, e toccata di lato: che cosa si vede dopo un errore imprevisto dell'interfaccia (C-33 ha lasciato
com'erano i quattro `async void` proprio per non deciderlo).

### 4. Cambi di comportamento già fatti senza una scelta dell'autore

Sono nel codice di oggi. Ognuno ha il suo commit: se uno non va, si dice di no a quello. Prima quelli che si notano
di più.

**Leggere la posta**

1. **Account IMAP: niente più Annulla iscrizione con un clic, e nessun dominio diventa autenticato per la sola posta
   arrivata lì** (T-03, `2216c62`). Resta il messaggio da inviare, o la pagina. I loghi dei domini già autenticati
   restano; quelli nuovi, per chi ha solo account IMAP, non arrivano più.
2. **Annulla iscrizione con un clic solo se il messaggio è firmato dal dominio del mittente** (T-12, `6f2b588`):
   qualche newsletter che prima si annullava con un clic ora prepara un messaggio o apre la pagina.
3. **Annulla iscrizione di una newsletter arrivata a un altro tuo indirizzo**: la conferma nomina quello e il
   messaggio parte da lì; l'indirizzo di un sito con lettere non latine è scritto in punycode (T-11, T-13, `6f2b588`).
4. **Un mittente consentito con la fascia rossa non carica più le immagini da solo**: "Mostra immagini" vale per
   quella volta. Lo stesso in una busta che sembra posta certificata senza la firma di un gestore, e in un .eml aperto
   da un messaggio della Posta indesiderata (T-02, `27ea8d3`).
5. **Immagini scritte in modo insolito, o con un CSS mai chiuso, sono bloccate** anche per il mittente non consentito
   dentro una conversazione con uno consentito; un'immagine con un indirizzo che non è né un sito né dell'email perde
   l'indirizzo; le animazioni SVG che cambiano immagine non girano più (T-01, `9fb69c3`).
6. **Con Gmail, l'intestazione di autenticazione trovata non in cima non autentica più il mittente** (T-04, `2216c62`):
   se dice che qualcosa non va la fascia rossa resta.
7. **Allegati `.appcontent-ms`, `.udl` e `.diagpkg` chiedono conferma**; un allegato che si chiama come un dispositivo
   di Windows si salva con un `_` davanti (T-09, T-10, `66d7696`).
8. **Se il disegno di un logo si pianta, non compaiono loghi nuovi finché non si sblocca** (T-07, `d7560dc`).
9. **Un messaggio con l'etichetta di indesiderata di Gmail, trovato da una ricerca, è trattato come indesiderato anche
   nel riquadro**: niente immagini da sole, niente logo, niente rubrica (T-08, `1382c57`).
10. **Elimina, Archivia o Sposta rifiutati dal server: dopo l'avviso le righe tornano nell'elenco**, non scelte (C-50,
    `2a6ce9b`).
11. **Annulla di uno spostamento fatto senza rete e partito solo in parte**: tornano le righe dei messaggi non partiti,
    e l'avviso dice quanti lo erano (C-55, `9b007b6`).
12. **Una seconda finestra aperta su una cartella riceve i messaggi nuovi**, solo le righe: i suoi non letti, se si
    vedono, non cambiano (C-60, `26fbe2c`).
13. **Ricerca**: `dal:25/12` scritto a gennaio è il dicembre appena passato; una virgoletta mai chiusa non tiene
    insieme nulla; i valori dei filtri perdono le virgolette (C-62, `0683d3c`).
14. **In un invito "Partecipa alla riunione online" compare solo per i siti dei servizi veri**; un invito dall'estero a
    un'ora che lì non esiste ha la sua scheda (T-33, `a6d58ac`; C-59, `61e8775`).
15. **Il sito di un contatto scritto come indirizzo numerico del PC o della rete di casa non è più un collegamento**,
    nemmeno se l'hai scritto tu (T-35, `a6d58ac`).
16. **In Gmail una nuova etichetta o cartella che il server non crea dà un errore che lo dice**; la rinomina di una
    cartella annidata chiede il genitore al server (C-37, `cc836be`).
17. **Con gli account Microsoft, una risposta di Graph che rimanda a un indirizzo non suo ferma la lettura con un
    errore**, invece di seguirlo con il token (T-23, `1b7c25b`). Dal vero non è mai stato visto un rimando così: se
    esistesse, quella lettura ora fallirebbe.

**Scrivere e inviare**

18. **Un invio interrotto mentre il server riceveva il messaggio non si ripete da solo** (C-61, `748c7a5`). L'invio
    pianificato esce dall'elenco con un avviso ("forse è partito, Missiva non riprova, la bozza è nelle Bozze"). Con
    Invia compare una finestra "Invio interrotto" e il messaggio non va nella Posta in uscita. Prima un invio
    pianificato così ripartiva ogni minuto.
19. **"Nessuna firma" scelta a mano resta anche cambiando il Da** (C-52, `83cf803`).
20. **Incolla**: da un altro programma fatto con WPF arriva l'RTF o il testo, non lo XAML; un testo RTF con un carattere
    che non è un nome si incolla senza formato, tutto. Vale anche per firme, modelli e risposte automatiche, che prima
    non avevano controlli (T-32, `7569784`).
21. **In una bozza ripresa, in una firma o in un modello un carattere che non è un nome non vale**: quel testo prende il
    carattere di base, e un nome con punti o trattini bassi li perde. Il carattere dei messaggi nelle impostazioni, se
    non è un nome, torna "Segoe UI" in silenzio (T-31, `7569784`).
22. **Un allegato grande che cambia mentre il messaggio si prepara dà un errore**, invece di partire troncato (T-24,
    `1b7c25b`).
23. **Una scrittura lasciata aperta mentre aggiungi o rimuovi un account passa al collegamento nuovo dello stesso
    account** e continua a salvare e inviare; se l'account è quello rimosso, Invia e Salva bozza dicono che il
    collegamento è stato chiuso (X-1, `56b4d4e`).
24. **Rimuovi account con messaggi nella Posta in uscita**: la domanda dice quanti non partiranno e le loro finestre si
    riaprono, con "copia il testo, se ti serve" (C-54, `81d5826`).
25. **Un modello che su questo PC non si decifra non inserisce nulla e apre un avviso** (T-21, `4cceda7`).
26. **Due link mailto: di fila aprono due finestre insieme** (T-25, `fee95f9`).

**Notifiche e calendario**

27. **Promemoria del calendario in ritardo: suonano tutti** (C-56, `b2ab076`). Dopo un avvio senza rete, o riaprendo
    il PC dopo giorni, arrivano tutti quelli scaduti dall'avvio o dall'ultima lettura riuscita, anche di riunioni
    finite, senza un tetto. Prima si perdevano. Un promemoria in un'ora che c'è due volte vale per la seconda.
28. **Missiva avviata da Windows per un pulsante di una notifica esce anche se l'azione fallisce** (C-26, `bf59f0d`).

**Copia di sicurezza**

29. **All'importazione non entrano fra i mittenti delle immagini un servizio di posta personale intero né un suffisso
    nazionale da solo**; l'avviso dopo il riavvio dice quante voci non sono entrate (T-17, `b6a12c2`).
30. **La conferma dell'importazione è più lunga**: account con i loro server, regole in parole, mittenti delle
    immagini e, quando serve, "Da guardare prima di importare" in cima; l'avviso dopo il riavvio ricorda che la copia
    aveva regole accese (T-17, `b6a12c2`).
31. **Rimettendo una copia sullo stesso PC, un account a cui sono stati cambiati server, porte o utente chiede di nuovo
    la password**; lo stesso per Microsoft e Google con un'altra app o un altro client (T-15, `6f94363`).
32. **La copia non porta più i domini noti dei loghi**: su un PC nuovo i loghi tornano man mano, non subito (T-18,
    `ba52e04`).
33. **Una copia con dentro qualcosa che Missiva non scrive si rifiuta, con il motivo**; un `impostazioni.json` con un
    identificativo di account non valido è trattato come un file rovinato (T-14, `8a4506c`; T-16, `ebbcd09`).
34. **Dati oltre 64 MB non si esportano**; una copia con più di 5 milioni di iterazioni si rifiuta (T-19, `ce0e5a4`).

**Aspetto**

35. **Il rosso degli errori nelle finestre segue il tema** (C-16, `ba933b0`): nello scuro diventa `#FF99A4`, nel chiaro
    passa da `#C42B1C` a `#C50F1F`. Sedici punti: quattordici finestre, gli avvisi delle regole, "Calendario non
    disponibile".
36. **La finestra di Microsoft (accesso, consenso) si apre sopra l'ultima finestra che l'ha chiesta e che si vede
    ancora, altrimenti sopra la principale** (C-13, `c0c01fb`).

**Registro, installazione, strumenti**

37. **Nel registro**: le righe che continuano una voce sono rientrate di quattro spazi; due righe non dicono più il
    nome della cartella IMAP (T-36, `a6d58ac`); possono comparire righe "lavoro in sottofondo che nessuno aspettava" e
    "errore non gestito fuori dall'interfaccia" (C-24, `daba973`); la revoca di Google e le copie non cancellate
    lasciano la loro riga (C-32, `d16f6c6`).
38. **Dopo la prossima installazione il comando dei link mailto: è `--mailto "%1"`** (T-27, `fee95f9`).
39. **Le tre variabili d'ambiente di prova e `DOTNET_STARTUP_HOOKS` non hanno più effetto sulla versione installata**
    (T-29, `0fc2081`).
40. **Una Debug copiata fuori da `bin\Debug` e avviata senza `MISSIVA_DATA` si chiude subito** (T-28, `919ac06`).
41. **Nella versione di prova Rimuovi account non chiede più la revoca a Google** (C-32, `d16f6c6`).
42. **Per chi usa gli strumenti**: tolti i sei interruttori della sonda del cursore (C-02, `72e0652`) e
    `--mostra-notifica` (C-04, `55f7885`); il server di `--imap-finto` risponde con un errore a rinomina ed elimina
    cartella, invece di fingere (C-43, `c0f7e5c`); i dati di un giro riuscito della suite si cancellano (C-49,
    `2d6d81c`); `lampi.ps1` e `scrivi.ps1` trovano anche la versione finale di prova (C-71, `b97ea28`).
43. **`CLAUDE.md` non ha più i dati veri**, nemmeno il nome dell'account GitHub (C-69, `a9299a4`).

### 5. Prove a mano per l'autore

Niente di quello che segue è stato provato a finestra: nessun gruppo ha avviato la versione di prova né quella
installata. In ordine di uso.

**Subito dopo l'installazione, con l'uso normale (cinque minuti)**

1. **L'installazione**: lancia `installa.ps1` con Missiva aperta, anche nascosta nell'area di notifica. Deve chiuderla
   entro pochi secondi (non dopo trenta) e reinstallarla. La prima volta chiude la versione di prima; il canale nuovo
   (T-25) si prova rilanciandolo una seconda volta, con la versione nuova aperta.
2. **Il primo avvio**: l'elenco compare subito dalla copia sul PC, nessun account chiede di nuovo l'accesso, Persone ha
   i contatti di prima, i loghi ci sono. Vuol dire che la versione nuova legge i file scritti da quella di prima (C-17).
3. **Una newsletter di un mittente consentito**: le immagini si vedono come prima (T-01, T-02).
4. **Un link mailto: in una pagina web**, con Missiva scelta come app di posta: si apre Nuovo messaggio. Poi lo stesso
   con Missiva chiusa (T-27).
5. **Copia e incolla nella scrittura**: copia un pezzo con del grassetto e una tabella e incollalo più sotto: uguale
   all'originale. Incolla un testo formattato da Word: formato come prima. Trascina un pezzo di testo (T-32).
6. **Una bozza in Modifica, una firma e un modello**: si vedono come prima, con i loro caratteri (T-31).
7. **Nuovo messaggio, Firma, Nessuna firma, poi un altro indirizzo nel Da**: il testo resta senza firma (C-52).
8. **Tasto destro su un messaggio e su una cartella**: stesse voci e stessi separatori di prima; "Personalizza il
   menu…" si apre (C-15). Dallo stesso menu, Rispondi con modello: il modello entra nel testo (C-33).
9. **Rispondi e inoltra con un allegato**, da Gmail e da un account IMAP: partono, con la copia in Posta inviata (C-10).
10. **Cerca `dal:25/12` e poi `da:"mario`**: risultati, non un elenco vuoto (C-62).
11. **Stampa un messaggio e salvalo come PDF**: come prima (T-01: la pagina ora si dà solo al documento).
12. **Apri la Posta in arrivo in un'altra finestra** e fatti arrivare un messaggio: compare in tutte e due (C-60).
13. **Tema scuro, un errore in una finestra**: Aggiungi account, Microsoft 365, "Usa l'app della mia organizzazione" con
    un ID scritto male. Il testo d'errore è un rosso chiaro che si legge; cambiando tema a finestra aperta cambia (C-16).
14. **Crea regola** su un messaggio di Gmail e su uno di un account Microsoft: le categorie compaiono (C-27).
15. **Persone, un contatto con un sito**: "Apri il sito" c'è e apre il browser (T-35).

**Quando capita, senza preparare nulla**

16. **Annulla iscrizione** su una newsletter, meglio se arrivata a un altro tuo indirizzo: la conferma dice
    quell'indirizzo e il sito in una forma sola. Poi "Resta iscritto" (T-11, T-13).
17. **Un allegato da più di 3 MB con un account Microsoft**: parte. Poi nel registro cerca "allegato a pezzi caricato
    su": quel nome serve alla scelta 19 (T-23).
18. **La finestra di Microsoft**: Gestisci account, "Accedi di nuovo": deve aprirsi sopra quella finestra. Chiusa
    quella, un "Consenti l'accesso" dal calendario o da Persone, se un consenso manca ancora: sopra la principale, non
    dietro (C-13).
19. **Un invito vero di Teams, Zoom o Meet**: "Partecipa alla riunione online" c'è (T-33).
20. **Un messaggio con la fascia rossa di un mittente consentito**, se ne arriva uno: immagini bloccate, e il
    suggerimento di "Mostra immagini" dice che vale solo per questa volta (T-02).
21. **Dopo qualche giorno, nel registro**: cerca "che nessuno aspettava". Ogni riga è un errore che prima non lasciava
    traccia: vanno guardate una per una (C-24).
22. **La notte fra il 24 e il 25 ottobre**: bozze salvate, promemoria e copia degli ultimi 30 giorni non devono
    aspettare un'ora in più (C-56, C-57). Non si prepara: si guarda il giorno dopo se qualcosa è mancato.

**Con una preparazione**

23. **Promemoria in ritardo**: un evento con il promemoria fra cinque minuti; chiudi Missiva, stacca la rete, riaprila,
    aspetta dieci minuti e riattacca la rete. Il promemoria arriva in ritardo, una volta sola (C-56).
24. **Una scrittura aperta mentre gli account si ricaricano**: Nuovo messaggio da Gmail o IMAP, due righe; senza
    chiuderlo aggiungi una casella condivisa o un account. Poi Invia: parte, con la copia in Posta inviata (X-1).
25. **Rimuovi account con la Posta in uscita piena**, su un account che puoi riaggiungere: senza rete Invia (va nella
    Posta in uscita), poi Rimuovi account. La domanda dice quanti messaggi non partiranno, la scrittura si riapre, la
    barra sparisce (C-54). Se nella scrittura riaperta premi Invia: dice che il collegamento è stato chiuso (X-1).
26. **Copia di sicurezza, andata e ritorno**. Prima di installare, con la versione di oggi: Impostazioni, Generali,
    Copia di sicurezza, Esporta. Dopo l'installazione: Importa quel file. La conferma mostra "Account:" con i server,
    le regole in parole e i mittenti delle immagini, senza "Da guardare"; dopo "Sostituisci tutto" e il riavvio gli
    account non chiedono nulla e l'avviso ricorda le regole accese. Tocca i dati veri: quelli di prima restano da parte
    con `prima-dell-importazione` nel nome (T-14..T-22; la copia fatta dalla versione di oggi prova anche T-18).
27. **Un rifiuto del server, con i dati finti**: `loop\bug-I\prova-finta.ps1 -Server 1 -Do "rifiuta UID MOVE"`, poi Canc
    su un messaggio. Compare "Operazione non riuscita" e la riga torna (C-50; quel comando è scritto e mai eseguito).
    Nella stessa prova apri, leggi, sposta ed elimina come sempre: se compare un errore nuovo, nel registro c'è "server
    IMAP finto:" con il comando che il server non conosce (C-43).
28. **Dieci messaggi aperti in finestre loro e poi chiusi**: `strumenti\memoria.ps1 -Installed -Detail` (senza
    `-Installed` per la versione di prova) torna al numero di processi di WebView2 di prima (C-36, qui misurato solo
    con finestre di prova).
29. **Lo scorrimento**: `loop\strumenti\misura-cursore.ps1 -Collaudo` e una misura vera con il cursore sulle righe
    (C-02); "costo righe" e "scorri 120" come prima (C-07).
30. **Gli strumenti con la versione finale di prova**: `prova.ps1 -Release`, poi `strumenti\scrivi.ps1 -Text "prova"`
    e `strumenti\lampi.ps1 -Keys file -Steps "clic 2"`: trovano la finestra (C-71).
31. **Il registro vero dopo l'installazione**: in `HKCU\Software\Classes\Missiva.Mailto\shell\open\command` il comando
    finisce con `--mailto "%1"` (T-27).
32. **Se nei dati veri c'è ancora qualcosa in chiaro**: `Select-String -Path $env:LOCALAPPDATA\Missiva\missiva.log
    -Pattern 'ora cifrati'`. Se l'ultima riga è vecchia di settimane, non c'è (serve alla scelta 38, C-06).
33. **Solo se scegli 60 secondi per il tempo scaduto** (scelta 1): prima di installarlo, sulla casella vera, una
    ricerca in "Tutti i messaggi" di Gmail e una bozza con molti allegati: non devono interrompersi (C-09).
34. **Solo se scegli il colore del testo secondario** (scelta 6): una foto per tema, prima e dopo (C-63).

Non si possono provare a mano: gli errori che C-25 e C-26 chiudono (servirebbe un errore finto), C-28 senza due
account Microsoft, C-61 con un server SMTP vero che tace proprio dopo il messaggio.

### 6. Cose note e lasciate

- **Cinque commit non compilano da soli**: `c0f7e5c`, `2905bca`, `ecd44c4`, `c0c73c6` e `20a087b`, del terzo gruppo
  del codice (5 errori in `ProvaBozze.cs`, per un errore nel dividere le modifiche). `ae867c1` rimette il file com'era
  stato provato; da lì in poi tutto compila. Una ricerca con `git bisect` deve saltarli. Vedi la scelta 32.
- **Controlli instabili**. Tre sono stati resi indipendenti dai tempi: la ricerca IMAP annullata (C-41), "S-01, un
  disegno che non finisce" (C-42), e in questo gruppo "nella prova Domani e Segna come completato" (`495fbc4`).
  Restano:
  - altre nove letture delle ultime righe del registro, fatte come quella corretta qui (cinque guardano la posizione
    esatta delle righe): non sono mai cadute, ma una riga di un altro lavoro in mezzo le farebbe cadere. La correzione
    è la stessa;
  - le soglie di tempo della tabella "Soglie di tempo rimaste" del terzo gruppo: la più stretta ha un margine di 1,7
    volte, con la suite da sola e il PC alla corrente;
  - il tempo della suite, che cambia da 160 a oltre 200 secondi con lo stato del PC (una volta 327, senza un motivo
    cercato);
  - i controlli che, se manca qualcosa sul PC, dicono "non misurato" invece di cadere: T-31 nel motore (porta 80 di
    loopback occupata o servizio WebClient spento), C-36 (WebView2 che non parte in una finestra di prova), la notte
    del 25 ottobre di C-56 (un fuso senza il ritorno all'ora solare), T-28 e T-29 (senza `dotnet` nel percorso). E i 26
    che leggono i sorgenti si saltano quando l'eseguibile non sta nella cartella dei sorgenti.
- **Mai provato a finestra**: nessuna finestra di Missiva è mai stata aperta in questa verifica. Le sezioni provano la
  funzione che decide e leggono il sorgente per vedere che la finestra la chiami; le sole finestre create sono finestre
  di WPF vuote, fuori dallo schermo (C-13, C-36). In particolare restano da vedere: la conferma lunga
  dell'importazione, Incolla e trascinamento con gli appunti veri, la finestra "Invio interrotto", Rimuovi account, i
  promemoria in ritardo, le finestre di una sola cartella, il rosso nei due temi, i menu del tasto destro, la finestra
  di Microsoft sopra il suo genitore.
- **Mai provato con server veri**: Graph, Gmail, un server IMAP e uno SMTP veri, Google per la revoca. Tutto gira sui
  server finti della suite, su 127.0.0.1.
- **Mai letti i dati veri né il registro vero**: la shell di lavoro vede copie private.
- **La versione di `installa.ps1` non è mai stata avviata**: vedi qui sotto.
- **`prova-release`** contiene ancora l'eseguibile dell'8 ottobre: nessun gruppo l'ha ricostruita.
- **I due rapporti non sono in main**: le pull request 6 e 7 sono aperte. Questo file li cita come
  `docs/audit/sicurezza-2.md` e `docs/audit/codice.md`, dove finiranno.
- **`CLAUDE.md`** è cresciuto del 13% in questa verifica: vedi la scelta 29.

### La versione che si installa

Il secondo gruppo del codice ha tolto dai pacchetti `System.Security.Cryptography.ProtectedData` (C-37): con .NET 10 fa
parte del framework di WPF. La suite passava in Debug, ma la versione pubblicata, quella che cifra password e token, non
era mai stata eseguita. Provata il 12 ottobre 2026.

- **Come pubblica `installa.ps1`** (letto, mai eseguito): `dotnet publish Missiva.csproj -c Release -o` la cartella di
  installazione `--nologo -v q`. Il resto è nel progetto: un file solo con il runtime dentro, senza compressione,
  ReadyToRun, e fuori dalla Debug `StartupHookSupport` spento.
- **Quella versione non si può provare così com'è**: la suite è codice solo di Debug e lì non c'è, e senza DEBUG né PROVA
  l'eseguibile non guarda `MISSIVA_DATA`: avviarlo vuol dire lavorare sui dati veri. Si è provata la versione finale con
  gli strumenti di prova: gli stessi argomenti più `-p:MissivaProva=true -p:DefineConstants=DEBUG%3BTRACE`, verso una
  cartella del lavoro. `prova-release` non è stata toccata (l'eseguibile lì è ancora dell'8 ottobre).
- **Pubblicata due volte**, prima e dopo la correzione del controllo detto sotto: 0 avvisi, 143 e 100 secondi, nella
  cartella un file solo, `Missiva.exe`, 273.717.965 byte tutte e due le volte. La suite dice di sé "DEBUG PROVA"
  (`Program.Build`).
- **La suite intera in quella versione**, con `MISSIVA_DATA` in una cartella temporanea: 992 controlli, non 1018.
  - I 26 che mancano leggono i sorgenti o chiedono a MSBuild le proprietà del progetto. Cercano `Missiva.csproj`
    risalendo dalla cartella dell'eseguibile; non lo trovano, si saltano e lo dicono ("sorgenti non trovati accanto
    all'eseguibile", dieci righe). Sono: la versione del manifesto e lo script di disinstallazione (blocco 5), S-21,
    S-22, S-27, T-08, T-11, T-28 (le costanti chieste a MSBuild), T-29 (due), T-36, C-13, C-25 (due), C-26, C-27, C-33,
    C-34, C-50, C-52, C-54, C-55, C-57, C-59, C-60, X-1.
  - Non dipendono dalla Debug: dipendono da dove sta l'eseguibile. I nomi dei controlli sono stati confrontati uno per
    uno con un giro della Debug dello stesso codice (1018, tutto bene, 177 secondi): mancano quei 26, nessuno è in più.
  - Primo giro, 161 secondi: 992 controlli, uno non passato, "nella prova Domani e Segna come completato vanno solo nel
    registro". Poi tre giri sullo stesso eseguibile: 992, tutto bene, in 158, 158 e 157 secondi. Il gruppo da solo,
    dodici volte: mai caduto.
  - Il motivo, letto dal registro di quel giro: il controllo guardava le ultime due righe del registro, e fra le due
    attese c'era una riga "tempi: apertura di [Gmail]/Tutti i messaggi", scritta dal provider di Gmail che lo stesso
    gruppo aveva chiuso poco prima e che finiva in sottofondo la sua richiesta. Le due righe c'erano. Non è un difetto
    della versione pubblicata: è una gara che c'è anche in Debug, dove il codice più lento finora l'aveva nascosta.
    Corretto il controllo (`495fbc4`).
  - Dopo la correzione, eseguibile ripubblicato: tre giri, 992 controlli, tutto bene, in 178, 173 e 163 secondi.
  - In tutto sette giri interi della versione pubblicata: uno con quel controllo caduto, prima della correzione; sei
    tutto bene.
- **DPAPI in quella versione**: i gruppi `copia` (18 controlli), `copia2` (24) e `impostazioni` (11) da soli, sui due
  eseguibili: tutto bene. Cifrano e decifrano davvero: testi "dpapi:" delle impostazioni, rubrica, token di Gmail, e le
  copie di sicurezza con `Store.TestEntropy` a fare da altro PC.
- **La libreria c'è, e serve**. Nella cartella pubblicata c'è un file solo:
  `System.Security.Cryptography.ProtectedData.dll` non sta accanto, sta dentro `Missiva.exe` come gli altri assembly (il
  suo nome compare nel file tre volte, come `MailKit.dll` e `PresentationFramework.dll`), preso dal framework di WPF
  invece che dal pacchetto. Nella cartella della Debug sta accanto all'eseguibile, 59.216 byte.
- **La pubblicazione identica a quella di `installa.ps1`** (gli stessi argomenti, un'altra cartella), solo guardata e
  mai avviata: 0 avvisi, un file solo, `Missiva.exe`, 266.586.829 byte; il nome della libreria c'è allo stesso modo,
  e del codice di prova non c'è traccia.
- Le tre cartelle pubblicate e i dati temporanei sono stati cancellati.
- **Il PC si è sospeso a metà**: alle 14:31:52, per batteria scarica (registro di Windows), e si è riacceso alle
  15:19:41 con l'alimentatore. Le suite erano finite circa mezzo minuto prima; la sospensione è caduta dentro l'ultima
  pubblicazione, quella mai avviata, che per questo segna 2.981 secondi di orologio. Tutti i giri fino a lì sono girati
  a batteria, dove tutto costa di più: nessuna soglia di tempo è caduta.
- **Resta non provato**: l'eseguibile di `installa.ps1` in esecuzione; DPAPI sui dati veri (la prova 2 del punto 5: il
  primo avvio dopo l'installazione); tutto quello che chiede una finestra.

### L'ultimo giro

- Compilazione da capo (`dotnet build --no-incremental`) sul codice di `63b4ef0`, dopo il quale cambiano solo i
  documenti: Release 0 errori e 0 avvisi, Debug 0 errori e 0 avvisi.
- Suite completa in Debug subito dopo, con `MISSIVA_DATA` in una cartella temporanea: 1018 controlli, tutto bene, in 206
  secondi. Un secondo giro: 1018, tutto bene, 193 secondi. Gli stessi nomi nei due, e gli stessi del giro fatto prima
  di toccare nulla, sul codice di `7743a1f`: 1018, tutto bene, 177 secondi.
- Nessuna caduta, in questi tre giri, dei controlli dati per instabili.
- Albero di lavoro pulito; 87 commit sopra `origin/main`; nessun invio a GitHub.

## Scelte del 12 ottobre, primo gruppo: lettura, account, copia di sicurezza (scelte 4, 5, 10, 14, 22, 23, 24)

L'autore ha letto il riepilogo qui sopra e alle 52 scelte del punto 3 ha risposto: "fai tutto ciò che ritieni giusto".
Quali scelte fare e quali no l'ha deciso la sessione che coordina il lavoro, in quattro blocchi: questo è il primo.
Il lavoro è sul ramo `scelte` (albero di lavoro `C:\Progetti\Missiva\scelte`), che alla fine si unisce in main: la
cartella `Missiva-sorgente` resta ferma sul commit installato, `33aed26`.

Fatto il 12 ottobre 2026 su sette scelte dell'elenco "3. Scelte per l'autore": 4 (T-02), 5 (T-10), 10 (T-06), 14 (C-32), 22
(T-17), 23 (T-20) e 24 (T-21). "Prima" è il commit `33aed26`. Commit, uno per scelta: `de86bf2` (T-02), `9c914a1`
(T-10), `b370057` (T-06), `ed30aea` (C-32), `facc8b6` (T-17), `c8057fb` (T-20), `4f2b7d3` (T-21), più quello di questo
esito. Tutte e sette fatte; nessuna fermata. Tutte cambiano qualcosa che si vede: la parte "Che cosa vede l'autore, e come
provarlo" le dice una per una.

Come si è provato:

- **Dopo**: `Choices1Tests` (`Core/ProvaBozze.Scelte1.cs`, da soli in due secondi con `--prova-audit risultato.txt
  scelte1`), 26 controlli. Dati finti, cartelle temporanee loro, un Google finto, `Store.TestEntropy` per fare da altro
  utente di Windows; nessuna finestra.
- **Prima**: ogni controllo è stato lanciato con la sua modifica spenta a mano (mai entrata in un commit), e le righe
  "prima" qui sotto sono quelle. Dei 26, 22 sono stati visti cadere: 20 con la modifica spenta, uno (le chiamate del
  riquadro, scelta 4) togliendo l'argomento da una chiamata, uno (la scheda di una pagina web che non è rossa, scelta
  5) mettendo per prova `.html` e `.svg` fra i tipi che avviano programmi. Gli altri quattro non possono cadere così:
  tre provano un testo che prima non esisteva (la domanda delle pagine web, la riga e la domanda dei dati di prima, la
  nota del pannello Modelli), uno prova quello che la scelta 10 non doveva cambiare (la fascia rossa, Exchange) e
  passa prima e dopo.
- **Le finestre non si aprono.** Per le scelte 14, 23 e 24, e per le conferme della 5, si prova la funzione che decide
  (quando l'avviso c'è, quali file sono "di prima", quali modelli entrano nel pannello, chi chiede la conferma) e si
  legge il sorgente per vedere che la finestra la chiami, nell'ordine giusto. Chi legge i sorgenti qui toglie prima i
  commenti (`Ch1Code`): una riga spenta con `//` o con `/* */` non conta come codice (i primi due tentativi di spegnere
  una chiamata lasciavano passare il controllo proprio per questo).
- **La misura della scelta 10** con una sonda temporanea, mai entrata nel repository, sulla copia di prova dei dati
  (`prova-dati`), letta soltanto e con `MISSIVA_DATA` in un'altra cartella: in uscita solo numeri.

### Le scelte

- **Scelta 4, T-02** (immagini negli .eml allegati a un messaggio normale). FATTA (`de86bf2`).
  - Prima: un .eml con il From di un mittente consentito, allegato a un messaggio normale, caricava le immagini da solo,
    e "Mostra immagini" lì ricordava per sempre quel mittente (controllo con la modifica spenta: `True`, 1 ricordato).
  - Ora `ReadingView.ImagesOnce` ha un caso in più: il messaggio è un .eml aperto da un allegato (`IsEml`, cioè il
    provider è un `EmlProvider`), da qualunque cartella venga. Le immagini non si caricano da sole, "Mostra immagini"
    vale per quella volta e non ricorda nessuno, e il suggerimento della fascia dice il motivo ("Questo messaggio è
    stato aperto da un allegato, e nessuno ha verificato chi lo ha scritto").
  - Solo le immagini: per il resto l'.eml allegato a un messaggio normale resta posta normale ("Annulla iscrizione",
    rubrica). Quello allegato a un messaggio della Posta indesiderata resta com'era dall'11 ottobre (`EmlJunk`).
  - Controlli: 4 (la regola, "Mostra immagini" che non ricorda, la pagina, le cinque chiamate del riquadro).
- **Scelta 5, T-10** (pagine web allegate). FATTA (`9c914a1`).
  - Prima: `.html`, `.htm`, `.shtml`, `.xhtml`, `.svg`, `.mht`, `.mhtml` si aprivano nel browser senza una parola.
  - Ora sono una classe a parte in `Safety` (`WebPages`, `IsWebPage`), non fra i `Dangerous`: niente rosso sulla
    scheda, "Salva tutti" le salva, Salva con nome non chiede nulla. Apri chiede prima una conferma con un testo suo
    (`Safety.WebPageQuestion`, `Dialog.ConfirmOpenWebPage`): "\"Accesso.html\" è una pagina web: è arrivata per posta,
    non viene da un sito. Si apre fuori da Missiva, di solito nel browser, e può imitare la pagina di accesso di una
    banca o di un servizio. Non scriverci password, codici o dati di pagamento. Aprila solo se la aspettavi e ti fidi
    di chi l'ha mandata.", con "Apri la pagina" e Annulla. Per un `.svg` il testo dice "un disegno che il browser apre
    come una pagina web" e il pulsante è "Apri il disegno".
  - La chiede il riquadro (`ReadingView.OpenAttachmentAsync`, dopo la domanda dei tipi in rosso, prima di scrivere il
    file temporaneo) e la finestra dei file firmati (`P7mWindow.Open`: una pagina dentro un `.p7m`).
  - Le sette estensioni sono quelle del punto. Non `.xht` né `.svgz`: non erano nel punto.
  - Controlli: 4.
- **Scelta 10, T-06** (e la scelta in attesa dal blocco 2 del 4 ottobre: "autenticato anche senza DMARC"). MISURATA,
  SOTTO LA SOGLIA, FATTA (`b370057`).
  - **La misura**, sulle copie dei corpi della copia di prova dei dati (4 account: uno Microsoft e tre Gmail, nessuno
    IMAP; 851 copie, 817 da un dominio di marchio, di 116 domini diversi, 813 con l'intestazione del server della
    casella). Messaggi autenticati con la regola di prima: 809; chiedendo DMARC superato: 724; 85 in meno, 84 senza
    nessun risultato di DMARC e uno con `bestguesspass`. **Domini autenticati con la regola di prima: 114; chiedendo
    DMARC superato: 110. Ne perdono il segno 4, il 3,5%**: molto sotto il quarto della soglia. Quei 4 hanno fra le
    copie 83 messaggi (uno ne ha uno solo): sono pochi domini che scrivono molto e non pubblicano DMARC. Dei 64 loghi
    sul disco della copia, 2 sono di quei domini. Annulla iscrizione con un clic: 33 messaggi prima e 33 dopo, nessuno
    perso. Il 4 ottobre il blocco 2 aveva contato 3 domini su 102 e 2 loghi su 63: nello stesso ordine.
  - **La regola** (`SenderCheck.Authenticated`): ora è `DMARC superato` insieme a quello che valeva prima, che resta
    com'era con un altro nome (`SenderCheck.Aligned`). Quindi: DMARC superato con `header.from` del dominio del marchio
    (anche un suo sottodominio); DMARC superato senza `header.from`, con una firma DKIM o un SPF di quel dominio. Non
    vale più, senza DMARC, la sola firma DKIM o il solo SPF del dominio, né `bestguesspass` di Microsoft, né
    `dmarc=none`. Con questo il caso di T-06 (la firma di un altro cliente della stessa piattaforma che autenticava la
    piattaforma) non c'è più: l'allineamento lo giudica il server della casella.
  - **Quello che non è stato toccato**: `SenderCheck.Failed` e la fascia rossa (un messaggio senza DMARC ma firmato
    non la prende: non è autenticato, non è nemmeno respinto); `SenderCheck.Own`, e con lei la decisione di T-05
    (l'intestazione di Exchange senza authserv-id autentica come prima, se dice DMARC superato). Un controllo lo dice.
  - **I domini già segnati.** Il segno "autenticato" in `domini.dat` non scade, e un dominio poteva averlo avuto con
    la regola di prima. Senza altro, il suo logo sarebbe rimasto sulle righe e sparito messaggio per messaggio
    all'apertura. Quindi una volta sola, in sottofondo due secondi dopo l'avvio, `SenderLogos.RecheckFromCopies` rilegge
    le copie dei corpi (come `AuthenticateFromCopies` del blocco 2; segno fatto: il file `loghi\autenticati-dmarc`): un
    dominio che fra le copie ha messaggi che valevano con la regola di prima e nessuno che supera DMARC perde il segno
    (il file del logo resta: torna al primo messaggio che supera DMARC). Chi non ha copie, o ha solo messaggi che non
    valevano nemmeno prima (un From falsificato, l'intestazione di un account IMAP), lo tiene: nulla dice come l'aveva
    avuto, e ogni suo messaggio nuovo si giudica per sé. Nella copia di prova: 106 domini con il segno, 101 confermati
    da un messaggio con DMARC superato, 4 lo perdono, uno non ha fra le copie nessun messaggio che valesse nemmeno prima
    e lo tiene.
  - Controlli: 5 (dodici forme di intestazione con la regola di prima e quella di ora; quello che non cambia; logo;
    Annulla iscrizione; la rilettura delle copie con cinque domini).
- **Scelta 14, C-32** (revoca di Google non riuscita). FATTA (`ed30aea`).
  - Prima: `GoogleAuth.SignOutAsync` diceva vero o falso, e `RemoveAccountAsync` non lo guardava: solo il registro.
  - Ora l'esito ha tre valori (`GoogleRevocation`): revocato; niente da dire (nessun accesso salvato, la versione di
    prova, o Google risponde 400 con `invalid_token`: l'autorizzazione non valeva già più); non riuscita (la rete che
    cade, un 400 con un altro errore o che non si legge, ogni altra risposta). Solo con "non riuscita", a rimozione
    fatta e account ricaricati, compare un avviso (`GoogleAuth.RevocationNotice`): "Account rimosso, ma l'accesso di
    Google può restare", e sotto: "... è uscito da Missiva, ma Google non ha confermato la revoca dell'accesso dato a
    Missiva: presso Google può restare valido. Si toglie a mano dalla pagina dell'account Google, fra le app e i servizi
    di terze parti collegati: myaccount.google.com/permissions".
  - Nella versione di prova (`MISSIVA_NO_READ`) la revoca non parte, com'era, e l'avviso non c'è (nel controllo: zero
    richieste, nessun avviso).
  - Controlli: 3 (otto esiti con un Google finto; il testo, senza il token; l'ordine in `RemoveAccountAsync`).
- **Scelta 22, T-17** (rubrica dei destinatari in una copia importata). FATTA (`facc8b6`).
  - Prima: la rubrica della copia entrava com'era. Con conteggi scritti da altri (lo stesso nome di una persona a cui
    scrivi, a un loro indirizzo, con 900 messaggi scritti) il primo suggerimento per "mario" era il loro indirizzo e un
    messaggio di "Mario Rossi" da lì non aveva la fascia dei sosia (controllo con la modifica spenta).
  - Ora chi apre una copia riceve la rubrica riscritta (`AddressBook.Imported`, da `Backup.Validate`, come già i
    contatti): gli indirizzi con i loro nomi e nient'altro. A zero i messaggi scritti e anche quelli ricevuti; via le
    date e l'ora dell'ultima lettura della Posta inviata. Riletta la Posta inviata di questo PC (in sottofondo, quindici
    secondi dopo l'avvio e poi a ogni controllo della posta finché non riesce: gli ultimi 500 messaggi per account; un
    account che deve rifare l'accesso, dopo l'accesso), il primo suggerimento è la persona a cui si scrive e il
    messaggio dall'indirizzo nuovo ha la fascia.
  - Perché anche i ricevuti e le date, e non solo gli scritti come diceva la proposta: il primo suggerimento si sceglie
    con `Sent * 3 + Received`, quindi 5.000 messaggi "ricevuti" scritti da altri lo dirottano lo stesso; e una data nel
    futuro ferma per sempre il conteggio e il nome di quella voce (`AddressBook.Add` conta solo i messaggi più recenti
    dell'ultimo visto), e un'ora di lettura nel futuro fa saltare per sempre la rilettura della Posta inviata.
  - I nomi entrano come li imparerebbe Missiva: puliti dai caratteri che non si vedono, mai l'indirizzo o il sito di un
    altro al posto del nome, niente indirizzi "noreply".
  - Nel file della copia la rubrica resta com'è sul PC: si azzera aprendo la copia, non esportandola.
  - Una rubrica che non è quella che Missiva scrive (un conteggio che non è un numero, una voce null, un elenco che non
    è un elenco) ora fa rifiutare la copia, con la regola dei null di tutto il resto. Prima entrava e all'avvio non si
    leggeva (rubrica vuota, una riga nel registro). Nel controllo "ogni valore di una copia" del primo gruppo della
    sicurezza le importazioni rifiutate passano da 250 a 262 su 475.
  - Controlli: 3.
- **Scelta 23, T-20** (file messi da parte dopo un'importazione). FATTA (`c8057fb`).
  - Missiva continua a non toglierli da sola. In Impostazioni, Generali, Copia di sicurezza, in fondo, solo quando quei
    file ci sono, c'è la parte "Dati di prima di un'importazione": dice quanti file sono e di quando, che tengono ancora
    gli accessi degli account di allora, e ha il pulsante "Elimina i dati di prima…". La domanda ("Eliminare i dati di
    prima dell'importazione?", con Annulla come scelta di base) dice che cosa si perde: non si torna più a com'era
    Missiva prima dell'importazione, i file non vanno nel Cestino, quello che Missiva ha adesso non cambia.
  - Quali file (`Backup.AsideFiles`): solo quelli con i nomi che Missiva dà mettendoli da parte, nella cartella dei
    dati e non nelle sottocartelle: `impostazioni-prima-dell-importazione-data.json` e `.bak`,
    `contatti-prima-dell-importazione-data.dat`, `rubrica-prima-dell-importazione-data.json`, anche con `-2` in fondo.
    Non i file messi da parte perché illeggibili (`...-illeggibile-...`), non i nomi che gli somigliano.
  - `Backup.DeleteAside` li cancella per sempre; un file in uso da un altro programma resta, viene detto, e si riprova.
  - L'avviso dopo il riavvio di un'importazione ora dice anche dove si eliminano.
  - Controlli: 4. Dopo un'importazione vera sopra due account: 4 file da parte, 4 accessi che si decifrano ancora;
    dopo "Elimina" restano i tre file di adesso, le impostazioni uguali byte per byte, zero accessi di prima sul disco.
    In una cartella con 21 file: 6 sono "di prima", 15 restano, più lo stesso nome in una sottocartella e una cartella
    con quel nome. In tutta Missiva c'è una sola chiamata a `DeleteAside`, quella del pulsante.
- **Scelta 24, T-21** (modelli che su questo PC non si decifrano). FATTA (`4f2b7d3`).
  - Prima: nel pannello Modelli della scrittura erano in elenco con il nome "(testo cifrato non leggibile su questo
    PC, rif. ...)"; scelti, non inserivano nulla e aprivano l'avviso dell'11 ottobre.
  - Ora `Templates.For` non li dà: non sono nel pannello Modelli né sotto "Rispondi con modello" del menu del
    messaggio (che con i soli illeggibili non compare). La nota del pannello (`Templates.PanelNote`) dice quanti non
    sono in elenco e dove sono: "Un modello non è in elenco perché su questo PC non si legge: è cifrato per un altro
    utente di Windows. In Impostazioni, Posta, Modelli c'è ancora, e lì si elimina."
  - In Impostazioni, Posta, Modelli restano tutti (la pagina legge `Settings.Templates`), con Elimina.
  - Controlli: 3.

### Trovato in più

- **La proposta sulla rubrica azzerava solo i messaggi scritti**: non bastava per quello che voleva (vedi la scelta
  22). Azzerati anche i ricevuti, le date e l'ora dell'ultima lettura della Posta inviata.
- **La scelta 10 da sola non toglieva il segno a chi l'aveva già**: da qui la rilettura delle copie, una volta.
- **Una rubrica rovinata dentro una copia entrava** e poi non si leggeva: ora la copia si rifiuta.
- **Un controllo che legge i sorgenti passava anche con la chiamata spenta da un commento**: vedi "Come si è provato".
  Vale per i controlli di questo gruppo; quelli dei gruppi precedenti che cercano un testo nel sorgente non tolgono i
  commenti (non guardato quanti).
- **Il controllo dei nomi dei gruppi (C-40) voleva "residui" per ultimo**: ogni gruppo nuovo l'avrebbe fatto cadere.
  Ora "residui" è nell'elenco dei nomi di prima, nel suo ordine, e i gruppi nati dopo stanno in fondo.

### Controlli e suite

- `Choices1Tests`, 26 controlli: scelta 4 (4), scelta 5 (4), scelta 10 (5), scelta 14 (3), scelta 22 (3), scelta 23
  (4), scelta 24 (3).
- **Cinque controlli di prima dicono una cosa nuova**, nessuno è sparito:
  - "T-02, un .eml con il From di un mittente consentito..." (secondo gruppo della sicurezza): l'allegato di un
    messaggio normale ora non carica le immagini; prima il controllo diceva "sì, come prima";
  - "S-04, autenticato: ..." (blocco 2): le tre forme senza DMARC che valevano ora non devono valere;
  - "su un altro PC: ..." (blocco 7): la rubrica importata non è più uguale byte per byte a quella di partenza, è la
    stessa voce con i conteggi a zero;
  - "C-32, rimozione di un account Gmail..." (secondo gruppo del codice): legge l'esito a tre valori; il testo dice
    che con il 400 `invalid_token` di quella prova il registro scrive che l'accesso non valeva già più;
  - "C-40, i gruppi in una tabella sola" (terzo gruppo del codice): vedi "Trovato in più".
- Quattro righe di sola misura hanno un testo nuovo: le pagine web e T-06 nel secondo gruppo della sicurezza, la rubrica
  e i file messi da parte nel primo. Il controllo "ogni valore di una copia" conta 262 rifiutate e 213 applicate (prima
  250 e 225).
- Suite completa (`--prova-bozze`): 1044 controlli, tutto bene, in 163 secondi, sull'albero di questo commit. Rispetto
  a `4f2b7d3` cambiano i documenti e, in `ProvaBozze.Scelte1.cs`, due indirizzi finti e un confronto reso indipendente
  dai loghi degli altri gruppi. Due giri prima: sul codice di `4f2b7d3` 1044, tutto bene, in 164 secondi; con i soli
  indirizzi cambiati, lo stesso in 164. Prima di cominciare, sul codice di `33aed26`: 1018, tutto bene, in 170. Sono 26
  in più, quelli di `Choices1Tests`. I nomi dei controlli del primo e dell'ultimo giro sono stati confrontati: nessuno
  è sparito; tre hanno un testo nuovo (T-02, S-04, C-32, qui sopra), gli altri due cambiati hanno lo stesso testo.
- Compilazione Debug e Release da capo (`--no-incremental`), prima dell'ultimo giro: 0 errori, 0 avvisi. La versione
  finale di prova non è stata ricostruita.

### Che cosa vede l'autore, e come provarlo

Niente di questo è stato provato a finestra. Per ogni scelta: che cosa cambia, in una riga, e i passi.

1. **Scelta 4. Un messaggio inoltrato come allegato (.eml) si apre senza immagini anche se il mittente è fra quelli
   consentiti: c'è "Mostra immagini", che vale per quella volta.**
   Passi: da un altro programma di posta inoltra "come allegato" una newsletter di un mittente di cui Missiva mostra
   sempre le immagini; in Missiva apri l'allegato. Nella finestra che si apre le immagini sono bloccate; il suggerimento
   di "Mostra immagini" dice "aperto da un allegato" e "vale solo per questa volta"; dopo il clic si vedono; chiusa e
   riaperta la finestra, sono di nuovo bloccate. La stessa newsletter nella Posta in arrivo le mostra come prima.
2. **Scelta 5. Aprendo un allegato .html, .htm, .svg o .mht Missiva chiede prima: "Aprire una pagina web arrivata per
   posta?".**
   Passi: mandati un messaggio con allegati `prova.html` e un `logo.svg`. Le due schede non sono rosse. Un clic su
   ognuna: compare la domanda, con il nome del file e "Non scriverci password"; Annulla non apre nulla; "Apri la
   pagina" (o "Apri il disegno") apre il browser. Salva con nome e "Salva tutti" non chiedono nulla. Un PDF o
   un'immagine si aprono come prima, senza domande.
3. **Scelta 10. Qualche mittente perde il logo: quelli il cui dominio non pubblica DMARC (nella copia di prova 4 domini
   su 114, 2 loghi su 64).**
   Passi: al primo avvio dopo l'installazione guarda l'elenco per una decina di secondi: i loghi ci sono come prima,
   tranne pochi che dopo due o tre secondi tornano iniziali. Nel registro una riga "loghi: N copie di messaggi rilette
   in ... ms, M domini autenticati senza DMARC superato non lo sono più" dice quanti. Annulla iscrizione su una
   newsletter che prima andava con un clic: come prima (nella copia di prova nessuna cambia). Se un mittente che ti
   scrive spesso ha perso il logo e lo rivuoi, è il caso da dire: la scelta si rifà al contrario con una riga.
4. **Scelta 14. Rimuovendo un account Google senza rete, dopo la rimozione compare "Account rimosso, ma l'accesso di
   Google può restare", con la pagina dove toglierlo.**
   Passi, solo su un account Google che puoi riaggiungere: stacca la rete, Impostazioni, Account, Rimuovi; l'account
   esce e poi compare l'avviso, con l'indirizzo della pagina dei permessi dell'account Google. Riattacca la rete, apri
   quella pagina e togli Missiva; poi riaggiungi l'account. Con la rete, lo stesso giro non mostra nessun avviso. Nella
   versione di prova (`prova.ps1`) l'avviso non c'è mai: lì la revoca non parte.
5. **Scelta 22. Dopo aver importato una copia di sicurezza i suggerimenti dei destinatari ci sono tutti, ma per
   qualche minuto in ordine sparso: si rimettono in ordine quando Missiva ha riletto la Posta inviata.**
   Passi: insieme alla prova della copia di sicurezza (punto 26 delle prove a mano del riepilogo): dopo il riavvio
   scrivi le prime lettere di una persona a cui scrivi spesso in un Nuovo messaggio. C'è; dopo un minuto con la rete è
   la prima proposta.
6. **Scelta 23. Dopo un'importazione, in Impostazioni, Generali, Copia di sicurezza compare in fondo "Dati di prima di
   un'importazione", con "Elimina i dati di prima…".**
   Passi: dopo l'importazione del punto sopra apri quella pagina. La parte c'è, dice quanti file sono e di quando.
   Premi il pulsante: la domanda dice che cosa si perde e ha il fuoco su Annulla. Con Annulla non cambia nulla. Con
   Elimina la parte sparisce dalla pagina, e nella cartella dei dati (menu delle Impostazioni, "Apri la cartella dei
   registri") non ci sono più file con `prima-dell-importazione` nel nome. Su un PC dove non hai mai importato la
   parte non c'è. Elimina cancella davvero: fallo quando sei sicuro che l'importazione è andata bene.
7. **Scelta 24. Nel pannello Modelli della scrittura non compaiono più i modelli con il nome "(testo cifrato non
   leggibile...)": una riga dice quanti sono e che stanno nelle Impostazioni.**
   Passi: sul tuo PC non dovresti averne (servono impostazioni scritte da un altro utente di Windows): il pannello
   Modelli e "Rispondi con modello" devono mostrare i modelli di sempre. Se in Impostazioni, Posta, Modelli ce n'è uno
   con quel nome, nel pannello non c'è e la nota lo dice; lì si elimina.

### Scelte fatte qui, da rivedere

- **Scelta 4**: vale per ogni .eml aperto, anche `postacert.eml` aperto a mano da una busta di posta certificata, e
  anche se nell'.eml c'è una Authentication-Results che dice tutto bene (l'ha scritta chi l'ha mandato).
- **Scelta 5**: sette estensioni, quelle del punto; "Apri la pagina" è il pulsante di base (Invio apre), come "Apri
  comunque" dei tipi in rosso; la domanda compare a ogni apertura, non si ricorda il mittente; anche per gli `.svg`,
  che spesso sono solo loghi.
- **Scelta 10**: i domini già segnati si rivedono una volta sola, e solo con le copie che ci sono sul PC; chi non ha
  copie tiene il segno. L'alternativa (togliere il segno a tutti e ripartire) avrebbe fatto sparire per qualche
  secondo tutti i loghi al primo avvio, e fino al messaggio dopo quelli dei domini senza copie. Con DMARC superato ma
  senza `header.from` vale ancora l'allineamento a due etichette di prima: nella copia di prova, zero messaggi.
- **Scelta 14**: il 400 con `invalid_token` non dà l'avviso. È quello che Google risponde per un token già scaduto
  o già revocato, e l'app di Google dell'autore non è verificata: se i suoi accessi scadono, ogni rimozione avrebbe dato
  un avviso inutile. Non verificato con Google vero. L'indirizzo della pagina è scritto nel testo, da copiare: nessun
  pulsante che apra il browser.
- **Scelta 22**: azzerati anche i ricevuti e le date, oltre agli scritti. La conferma dell'importazione non dice che
  i conteggi non entrano. Una copia con la rubrica rovinata si rifiuta tutta, invece di entrare senza rubrica.
- **Scelta 23**: i file si cancellano per sempre, non vanno nel Cestino (lo scopo è toglierli dal disco: non sono
  sovrascritti, solo cancellati). Restano fuori i file `importazione-illeggibile-...`, che un'importazione non
  riuscita mette da parte: dentro c'è il contenuto della copia, cifrato per l'utente di Windows, con le password IMAP
  se la copia le aveva. Non hanno un pulsante.
- **Scelta 24**: la nota in più nel pannello; anche "Rispondi con modello" li nasconde.

### Non verificato

- **Niente a finestra**: l'.eml aperto in una finestra vera, la domanda delle pagine web, l'avviso dopo Rimuovi
  account, la pagina Copia di sicurezza con la parte nuova (va a capo? il pulsante sta nella pagina senza scorrere?),
  il pannello Modelli con la nota sopra l'elenco. I passi sono qui sopra.
- **Scelta 5**: che cosa apre davvero Windows per ognuna delle sette estensioni (dipende dal programma predefinito:
  per un `.svg` può essere un visualizzatore di immagini, e il testo dice "di solito nel browser").
- **Scelta 10**: la rilettura sui dati veri (qui cinque domini finti; sulla copia di prova solo i conteggi, con una
  sonda che non scrive). I tempi: `AuthenticateFromCopies` aveva riletto 654 copie in 0,75 s, questa fa lo stesso
  lavoro. Server veri: come Gmail e Exchange scrivano un DMARC superato oggi è preso dai controlli dei blocchi 2 e 3.
- **Scelta 14**: Google vero. Che la revoca di un token già scaduto dia proprio 400 con `invalid_token`, e che
  `myaccount.google.com/permissions` porti ancora alla pagina delle app collegate, non si è potuto guardare (niente
  rete). `RemoveAccountAsync` con la finestra: letto dal sorgente.
- **Scelta 22**: la rilettura della Posta inviata dopo un'importazione vera (qui un messaggio inviato finto, in
  memoria); la rubrica importata letta da `AddressBook` dal file (qui il file si decifra e si legge con lo stesso
  JSON, ma non dalla rubrica vera della cartella dei dati).
- **Scelta 23**: la cartella dei dati vera; un file davvero in uso da un antivirus (qui un file tenuto aperto dalla
  prova).
- **Scelta 24**: il pannello e il menu del messaggio a finestra; un modello illeggibile vero.
- **La Missiva installata e la versione finale di prova** non sono state eseguite; `prova-release` non è stata
  ricostruita.

## Scelte del 12 ottobre, secondo gruppo: promemoria, regole, finestre, ricerca, cartelle (scelte 3, 8, 15, 17, 18, 36)

Secondo dei quattro blocchi con cui si mettono in pratica le scelte che l'autore ha lasciato alla sessione che coordina il
lavoro ("fai tutto ciò che ritieni giusto"). Sul ramo `scelte`, albero di lavoro `C:\Progetti\Missiva\scelte`; la
cartella `Missiva-sorgente` resta ferma sul commit installato, `33aed26`.

Fatto il 13 ottobre 2026 su sei scelte dell'elenco "3. Scelte per l'autore": 3 (C-56), 8 (C-39), 15 (C-51), 17 (X-1), 18
(trovata con C-47) e, della 36, solo la prima parte (le due divergenze di C-08 e C-11; la base comune di Gmail e IMAP
resta da fare). "Prima" è il commit `c0dc848`, la fine del primo gruppo. Commit, uno per scelta e due ritocchi:
`c5c6f1d` (C-56), `575042a` e `7c3f543` (C-51), `1261793` e `c466a4f` (X-1), `76ad92e` (C-39), `00acbd3` (le
sottocartelle), `9d4faee` (C-08), `87f07b4` (C-11), più quello di questo esito. Tutte fatte; nessuna fermata. Le
scelte 3, 15 e 17 cambiano qualcosa che si vede: la parte "Che cosa vede l'autore, e come provarlo" le dice una per una.

Come si è provato:

- **Dopo**: `Choices2Tests` (`Core/ProvaBozze.Scelte2.cs`, da soli in due secondi con `--prova-audit risultato.txt
  scelte2`), 28 controlli e una riga di sola misura. Orologi finti per i promemoria; i server IMAP finti (con MOVE e
  senza) e quello di Gmail su 127.0.0.1, fatti rifiutare un comando; cartelle date a mano a un account; nessuna
  finestra.
- **Prima**: dei 28, 24 sono stati visti cadere. 22 con la loro modifica spenta a mano (mai entrata in un commit), e i
  due delle divergenze fra Gmail e IMAP lanciati prima di toccare `GmailProvider`. Gli altri quattro provano quello che
  non doveva cambiare, e passano prima e dopo: un promemoria puntuale di giorno e la notte del 25 ottobre (scelta 3), i
  server che separano con la barra (scelta 18), la bozza ripresa e l'allegato aperto dal riquadro (scelta 36).
  - Scelta 3: con il tetto e il testo spenti cadono 6 controlli su 8.
  - Scelta 15: con il primo errore di nuovo sollevato cadono 9 controlli su 10, tutti quelli sui server; poi, a motore
    acceso, spenti uno per uno "una copia non si rifà", "uno spostamento si riprova solo se rifiutato per intero", "tre
    tentativi" e "solo se c'è ancora" cadono i cinque che li provano, e il decimo (il sorgente) con la riga di
    `App.cs` spenta da un commento. Con quegli interruttori spenti il risultato dice il danno che evitano: "nell'archivio
    restano 4 messaggi", "nella cartella di arrivo restano 4 copie".
  - Scelta 17: tutti e tre (la riga che non cambia account, il testo unico dell'errore, un comando della barra rimesso
    sulla riga di prima; poi, da solo, il riquadro rimesso sul provider di apertura).
  - Scelta 8: tutti e due, con i 60 secondi rimessi in `ImapProvider`.
  - Scelta 18: uno su due, con la barra fissa rimessa.
- **Le finestre non si aprono.** La notifica del promemoria, la barra della finestra del messaggio e il riquadro di
  lettura si provano nella funzione che decide (`CalendarReminders.Due` e `Text`, `MainWindow.CurrentItem`,
  `ComposeWindow.Current`) e si legge il sorgente, senza i commenti (`Ch1Code`), per vedere che la finestra la chiami.
- I controlli delle regole chiamano le regole dentro un `try`: se le regole sollevano, come prima, il controllo cade e
  dice quale errore, invece di fermare il gruppo.

### Le scelte

- **Scelta 3, C-56** (promemoria del calendario in ritardo). FATTA (`c5c6f1d`).
  - Prima: dopo un avvio senza rete, o riaprendo il PC dopo giorni, suonavano tutti i promemoria scaduti. Nel controllo
    del gruppo che l'aveva corretto, PC fermo da venerdì alle 18:00 a lunedì alle 08:55: alle 08:55 suonava anche
    quello di sabato alle 10:00. Con sette riunioni nel controllo nuovo: sette promemoria, cinque insieme alle 08:55.
  - Ora `CalendarReminders.Due` distingue. Un promemoria che arriva entro due minuti dalla sua ora
    (`CalendarReminders.Late`) è puntuale: suona com'era, con lo stesso testo, anche se la riunione è già finita (un
    evento senza durata con il promemoria all'inizio). Uno più in ritardo suona solo se la riunione non è ancora finita
    in quel momento, e la notifica ha una riga in più: "Promemoria in ritardo: era alle 08:45" (`CalendarReminders.Text`;
    "era ieri alle 23:45", o "era venerdì 16 ottobre alle 17:15", se non è di oggi). L'ora è quella in cui il promemoria
    doveva arrivare, non quella della riunione, che resta scritta sopra com'era.
  - Quelli delle riunioni finite non suonano e contano come mostrati. `CheckRemindersAsync` scrive nel registro quanti,
    senza titoli: "promemoria del calendario: 3 in ritardo non mostrati, le riunioni erano già finite".
  - Nel controllo: di sette riunioni fra venerdì e lunedì suonano il promemoria puntuale di venerdì, quelli in ritardo
    delle due riunioni di lunedì non finite alle 08:55 ("era alle 08:15", "era alle 08:45") e quello puntuale delle
    10:45; tre non suonano (venerdì sera, sabato, lunedì alle 08:00).
  - La notte del 25 ottobre 2026, PC fermo dalle 00:30 legali alle 03:20 solari: suonano in ritardo i promemoria delle
    02:15 e delle 02:55 (65 e 25 minuti veri di ritardo: i confronti sono fra istanti), non quello della riunione
    finita all'01:45, e quello delle 03:45 è puntuale. Senza fermate, la stessa notte: quattro promemoria puntuali,
    quello delle 02:55 alla seconda delle due.
  - Un calendario nascosto non suona e non entra nel conto dei saltati.
  - Controlli: 8.
- **Scelta 15, C-51** (regole di Missiva dopo un errore a metà). FATTA (`575042a`; le righe del registro, `7c3f543`).
  - Prima, misurato dal gruppo che l'aveva verificato e rimisurato qui: tre messaggi nuovi, una regola "segna come
    letto e sposta", il server che rifiuta il primo `UID STORE`. Al primo controllo un errore, 0 spostati su 3; al
    controllo dopo nessun messaggio da guardare, 0 su 3, per sempre.
  - **Ogni gruppo di azioni per conto suo** (`Rules.RunAsync`): segna come letti, contrassegna, ogni categoria, ogni
    copia, ogni spostamento e il Cestino partono ognuno nel suo `try`. Quello che non riesce non solleva più: è scritto
    in `Outcome.Errors`, in parole ("3 non segnati come letti: ..."), e gli altri gruppi partono lo stesso.
  - **Un messaggio con un'azione non riuscita resta dov'è**: in quel giro non si sposta e non si elimina, perché dopo
    lo spostamento non lo si ritroverebbe per finire il resto. L'esito lo dice ("3 lasciati dove sono, non spostati in
    Clienti: un'azione di prima non è riuscita") e il messaggio è in `Outcome.Pending`, con le azioni da non rifare.
  - **Riprovati al controllo dopo** (`Rules.ApplyToNewAsync`): i non finiti restano in memoria, per account. Al
    controllo dopo si guarda quali sono ancora nella Posta in arrivo (`StillThereAsync`, una lettura) e solo su quelli
    si rifanno le azioni che restano. Uno spostato o eliminato a mano nel frattempo non si tocca.
  - **Che cosa non si ripete mai.** Fra le azioni delle regole di Missiva non ci sono inoltri né risposte (li hanno
    solo le regole di Outlook, che girano sul server). Quelle che rifatte lascerebbero un doppione sono due:
    - la copia: dopo che è partita non si riprova mai, nemmeno se non è riuscita (non si sa se il server l'ha fatta).
      L'esito lo dice: "2 non copiati in Archivio, e non si riprova: ...". Il resto delle azioni di quel messaggio va
      avanti al controllo dopo;
    - lo spostamento e l'eliminazione (nel Cestino): si riprovano solo quando il server ha rifiutato per intero un
      `UID MOVE`, perché allora ogni messaggio è stato spostato oppure è rimasto com'era (`MailException.Repeatable`,
      che `ImapProvider.MoveAsync` e `GmailProvider.MoveAsync` mettono solo se il server ha MOVE). Su un server senza
      MOVE lo spostamento è una copia seguita da un'eliminazione: rifiutato dopo la copia non si riprova, il messaggio
      resta nella Posta in arrivo e l'esito lo dice. Così anche per un errore che non è un rifiuto del server (la
      connessione caduta due volte): non si sa che cosa è successo, non si riprova.
    Segna come letto, contrassegna e categoria si riprovano: rifarle non cambia nulla.
  - **Non all'infinito**: tre tentativi in tutto, il primo compreso (`Rules.MaxTries`). Poi il messaggio si lascia
    dov'è e il registro lo dice, senza oggetti né mittenti: "un messaggio lasciato com'è dopo 3 tentativi, le regole
    non lo riprovano più".
  - **"Esegui ora"** passa dallo stesso codice: non si ferma più al primo errore, e il testo dice che cosa è riuscito e
    che cosa no (`RulesView.RunResult`): "2 dei 2 messaggi corrispondono; 2 contrassegnati. Non riuscito: 2 non segnati
    come letti: ...; 2 lasciati dove sono, non spostati in Clienti: ...". Prima: "Esegui ora non riuscito", senza dire
    che cosa era già stato fatto.
  - **Le righe dell'elenco**: un messaggio che le regole finiscono a un controllo dopo è già nell'elenco. Il controllo
    dei nuovi messaggi cambia i segni sulla sua riga (`ApplyIdleFlags`) e la toglie se è stato spostato (com'era).
  - Nei controlli, sul server IMAP finto con MOVE: il caso misurato (al controllo dopo un `UID STORE` e un `UID MOVE`,
    3 spostati su 3, letti; a quello dopo ancora nessun comando); due regole e quattro messaggi (i due dell'altra regola
    contrassegnati lo stesso al primo controllo, e non più toccati dopo); "copia e sposta" con l'`UID MOVE` rifiutato
    (la copia fatta una volta: nell'archivio 2 messaggi, non 4) e con l'`UID COPY` rifiutato (nessuna copia riprovata,
    lo spostamento sì); tre rifiuti di fila (tre `UID STORE`, poi più nulla); le cartelle dell'account non lette e un
    messaggio sparito intanto. Senza MOVE: lo spostamento rifiutato dopo la copia non si ripete (2 copie nella cartella
    di arrivo, non 4). Con Gmail: rifiutato prima il segno e poi lo spostamento, al terzo controllo i due messaggi sono
    letti e spostati.
  - Controlli: 10. La riga di misura di C-51 nel primo gruppo del codice ora dice: "al primo controllo 3 corrispondono,
    spostati 0 di 3; al controllo dopo 3 corrispondono, spostati 3 di 3, 2 comandi di scrittura: la regola recupera".
- **Scelta 17, X-1** (un messaggio aperto in una finestra sua dopo un ricarico degli account). FATTA (`1261793`,
  `c466a4f`).
  - Prima, nel controllo: l'azione sul provider della finestra dava "Il collegamento di ... usato da questa finestra è
    stato chiuso ... Chiudi la finestra e riaprila".
  - Ora per la stessa strada della scrittura, `ComposeWindow.Current` (`IMailProvider.IsClosed`, `56b4d4e`), e non per
    una seconda:
    - il riquadro di lettura chiede il provider a `ComposeWindow.Current` a ogni uso (`ReadingView.provider`, sopra
      quello con cui il messaggio è stato aperto, `opener`): corpo, allegati, immagini, Rispondi, Annulla iscrizione,
      inviti, il menu "..." dentro il messaggio;
    - i comandi della barra della finestra (Elimina, Archivia, Sposta in, Già letto/Da leggere, Contrassegna, Altre
      azioni, Apri nel riquadro) passano da `MainWindow.CurrentItem`: se il provider di adesso è un altro, la riga
      dell'elenco di adesso con lo stesso messaggio, o una riga nuova sullo stesso messaggio con l'account di adesso.
  - Un provider aperto non si cambia mai: per gli account Microsoft, che non si chiudono, nulla cambia.
  - **L'account rimosso**: non c'è un provider nuovo, resta quello chiuso e l'azione dà l'errore. Il testo ora dice
    quale dei due casi è (`MailException.ClosedText`): "... è stato chiuso: l'account è stato rimosso da Missiva. Da
    questa finestra non si può più leggere né cambiare nulla di quell'account: chiudila; se stavi scrivendo, copia
    prima il testo." Prima un testo solo diceva "l'account è stato rimosso, o gli account sono stati ricaricati" e
    proponeva sempre di riaprire la finestra. Per una finestra che tiene ancora un provider di prima dopo un ricarico
    resta "gli account sono stati ricaricati ... Chiudi la finestra e riaprila".
  - Un controllo legge i sorgenti: in tutta l'interfaccia solo `ComposeWindow.xaml.cs` guarda `IsClosed`.
  - Controlli: 3.
- **Scelta 8, C-39** (le due finestre in cui una ricerca già fatta si riusa). FATTA (`76ad92e`).
  - Prima: `SuggestSearches.Fresh` 30 secondi (Invio dopo i suggerimenti, tutti gli account) e `ImapProvider.ReuseSearch`
    60 (la ricerca in tutte le cartelle di un account IMAP). Nel controllo con i 60 rimessi: a 31 secondi Invio non
    riusa più la ricerca dei suggerimenti, la rifà, e IMAP gli dà quella di prima (0 SEARCH).
  - Ora un numero solo, `MailLimits.SearchReuseSeconds`, 30, letto da tutte e due. Su IMAP a 20 secondi le stesse parole
    riusano (0 SEARCH), a 31 la ricerca si rifà davvero (5 SEARCH, una per cartella).
  - Per IMAP è un cambio: cercare di nuovo le stesse parole fra 30 e 60 secondi dopo ora interroga il server. Le
    pagine dopo riusano sempre, come prima.
  - La frase nei documenti: in `CLAUDE.md` le due voci (account IMAP e pannello dei suggerimenti) dicevano 60 e 30;
    ora 30 tutte e due, con il nome della costante. In `LEGGIMI.md` non c'era.
  - Controlli: 2.
- **Scelta 18, trovata con C-47** (sottocartelle sui server IMAP che separano con il punto). FATTA (`00acbd3`).
  - Prima, nel controllo con la barra fissa rimessa: rinominata `INBOX.Clienti` in `INBOX.Fornitori`, nei Preferiti
    restava `INBOX.Clienti.Rossi`, e così fra le nascoste, le chiuse, i colori e l'ordine; e `INBOX.Clienti/Vecchi`, una
    cartella con una barra nel nome, diventava `INBOX.Fornitori/Vecchi` come se fosse una sottocartella.
  - Ora `MainWindow.MigrateFolderKeys` usa il separatore dell'account (`MainWindow.FolderSeparator`): lo legge da una
    cartella che ne ha una sopra, il cui identificativo è quello della madre, il separatore, il nome. Senza
    sottocartelle, o con identificativi che non sono percorsi (Graph, dove rinominare non li cambia), la barra.
  - Dove le sottocartelle sono scritte con la barra (Gmail, molti server IMAP) nulla cambia: un controllo lo dice.
  - Controlli: 2. La riga di misura di C-47 nel terzo gruppo del codice ora dà all'account le sue cartelle e dice "la
    sottocartella segue, con il separatore dell'account".
- **Scelta 36, prima parte** (le due divergenze fra Gmail e IMAP). FATTE tutte e due, ognuna da sola (`9d4faee`,
  `87f07b4`). La base comune dei due provider NON è stata fatta, e nessun codice è passato da un file all'altro.
  - **(a) C-08, gli allegati con SELECT o con EXAMINE.**
    - Dove differivano davvero: non nell'apertura dal riquadro di lettura (lì tutti e due usano una connessione su cui
      passano anche le azioni, in lettura e scrittura), ma sulla connessione delle bozze, quando si leggono gli
      allegati di un messaggio inoltrato o di una bozza ripresa. Lì IMAP apriva in sola lettura
      (`ExamineOrOpenAsync`), Gmail con SELECT.
    - Perché: la storia di git. La lettura degli allegati di Gmail è nata sulla connessione principale; il 26
      settembre è passata alla connessione delle bozze (`721a023`) con l'apertura che aveva. Gli account IMAP sono
      stati scritti la sera dello stesso giorno (`924bf0f`) e hanno distinto subito, con il commento "sulla
      principale in lettura e scrittura (come per le azioni), sulle altre in sola lettura". Nessun commento dice che a Gmail serva SELECT:
      la differenza non era voluta. Giusto è il comportamento di IMAP.
    - Che cosa cambia sul server finto, guardato prima e dopo: il comando sulla connessione che legge l'allegato
      (`SELECT INBOX`, ora `EXAMINE INBOX`). `\Seen` non cambiava nemmeno prima: MailKit legge con `BODY.PEEK`, e il
      messaggio resta senza segni in tutti e due i modi. `\Recent`, che un SELECT toglie ai messaggi della cartella
      (RFC 3501) e che il server finto ora tiene (`Recent`), prima si perdeva e ora resta. L'allegato arriva nella
      bozza uguale.
    - Quello che costa: una bozza con allegati ripresa dopo un riavvio, al primo salvataggio, apre le Bozze due volte
      (EXAMINE per leggere l'allegato, poi SELECT per scrivere) invece di una. È una riga di misura della suite. Gli
      account IMAP lo fanno già. Se le Bozze sono già aperte per scrivere restano così, senza comandi in più.
  - **(b) C-11, la ricerca della bozza senza il filtro sui messaggi segnati `\Deleted`.**
    - Dove: `FindAsync`, che in Gmail serve solo quando un salvataggio riparte dopo una connessione caduta e cerca la
      bozza appena scritta con il suo Message-Id, per non scriverla due volte.
    - Perché differivano: quella di Gmail è nata per prima (`721a023`); quella di IMAP dopo, con il filtro, perché lì
      la stessa funzione ritrova anche i messaggi spostati, che senza UIDPLUS restano segnati `\Deleted`. Non era
      voluto per come funziona Gmail. Giusto è il comportamento di IMAP: una bozza segnata `\Deleted` sta per sparire.
    - Il caso che le distingue, sul server finto: la connessione cade dopo APPEND e, prima della ripresa, la bozza
      appena scritta viene segnata `\Deleted` (un altro programma l'ha eliminata e il server non l'ha ancora tolta).
      Prima Gmail la ritrovava e la restituiva alla finestra come salvata, senza riscriverla (1 APPEND): al primo
      EXPUNGE la bozza spariva. Ora la riscrive (2 APPEND) e restituisce quella nuova, come IMAP.
    - In Gmail con l'eliminazione automatica accesa (l'impostazione di base) un messaggio segnato `\Deleted` sparisce
      subito: lì non cambia nulla. Conta per chi l'ha spenta nelle impostazioni IMAP di Gmail.
    - Aggiunto anche il Message-Id vuoto, che non si cerca, come in IMAP: in Gmail non capita (il Message-Id si genera
      prima), e non ha un controllo.
  - Controlli: 3 (due per (a), uno per (b), lo stesso caso su Gmail e su IMAP).

### Trovato in più

- **Due controlli del primo gruppo del codice provavano il comportamento che la scelta 3 toglie** (il promemoria di
  sabato che suona il lunedì; 358 promemoria in trenta giorni, compresi quelli della sera alla riapertura): ora dicono
  la cosa nuova. Vedi "Controlli e suite".
- **Le righe già in elenco non cambiavano quando una regola segnava letto un messaggio** che l'elenco aveva già (una
  rilettura dell'IDLE arrivata prima del controllo): c'era anche prima, e con i messaggi riprovati sarebbe stato il
  caso normale. Ora il controllo dei nuovi messaggi lo dice alla riga. Letto, non provato a finestra.
- **Il testo dell'errore di un provider chiuso** dava sempre tutte e due le cause e lo stesso consiglio: con le finestre
  che passano da sole al provider nuovo, nella scrittura e nel messaggio arriva solo per l'account rimosso, dove
  "riaprila" non ha senso.
- **Una cartella con una barra nel nome**, su un server che separa con il punto, passava per sottocartella di quella
  rinominata (scelta 18): il contrario del difetto cercato, tolto dalla stessa riga.
- **Fra 30 e 30 secondi più qualche millisecondo** le due finestre della ricerca non coincidono ancora: quella di Invio
  conta da quando il pannello chiede la ricerca, quella di IMAP da quando il provider la comincia, un attimo dopo.
  TRASCURABILE, non toccato.
- **Il server IMAP finto** ha due cose in più: `Traffic`, i comandi con il numero della connessione su cui arrivano, e
  `Recent` sui messaggi, che SELECT toglie ed EXAMINE no. Nessun controllo di prima le usa.
- Gli heredoc di Bash su questo PC hanno tolto una barra rovesciata doppia e trasformato `\uE74D` nel carattere, in due
  script Python: il primo si è fermato da solo prima di scrivere, il secondo è stato ricontrollato leggendolo.

### Controlli e suite

- `Choices2Tests`, 28 controlli: scelta 3 (8), scelta 15 (10), scelta 17 (3), scelta 8 (2), scelta 18 (2), scelta 36
  (3). Una riga di sola misura: la bozza ripresa dopo un riavvio (scelta 36).
- **Due controlli di prima dicono una cosa nuova**, nessuno è sparito. Tutti e due in `BehaviourAuditTests`:
  - "C-56, PC fermo da venerdì alle 18:00 a lunedì alle 08:55...": alla riapertura suonano il promemoria di venerdì
    (puntuale) e i due di lunedì; quello di sabato, che prima doveva suonare, ora non deve;
  - "C-56, trenta giorni di letture ogni mezz'ora...": 300 promemoria invece di 358. I due della sera, in ritardo al
    mattino dopo e di riunioni finite, non suonano più: 10 al giorno invece di 12.
- Due righe di sola misura hanno un testo nuovo: C-51 (la regola recupera) e C-47 (la sottocartella segue). Il
  controllo "C-40, i gruppi in una tabella sola" conta un gruppo in più.
- Suite completa (`--prova-bozze`): 1072 controlli, tutto bene, in 163 secondi, sull'albero di questo commit con questa
  sezione ancora senza i suoi numeri: dopo quel giro sono cambiate solo le righe che li riportano. Due giri interi
  prima, tutti e due 1072 e tutto bene: sul codice di `87f07b4` in 167 secondi, e su quello di `7c3f543` in 166. Prima
  di cominciare, sul codice di `c0dc848`: 1044, tutto bene, in 163. Sono 28 in più, quelli di `Choices2Tests`. Rispetto
  a `7c3f543` cambiano i documenti e, in `ProvaBozze.Scelte2.cs`, l'età data alla ricerca di IMAP nel controllo della
  scelta 8: 20 secondi invece di 29, perché un secondo perso dal PC non lo faccia cadere (rivisto cadere con i 60
  secondi rimessi).
- I nomi dei controlli del giro fatto prima di cominciare e dell'ultimo sono stati confrontati: nessuno è sparito; 28
  in più, quelli di `Choices2Tests`; cambiano i due di C-56 qui sopra, e tre righe che hanno dentro un pezzo casuale.
- Compilazione Debug e Release da capo (`--no-incremental`): 0 errori e 0 avvisi tutte e due, prima di ognuno dei tre
  giri interi. La versione finale di prova non è stata ricostruita.

### Che cosa vede l'autore, e come provarlo

Niente di questo è stato provato a finestra.

1. **Scelta 3. Riaprendo il PC, o quando torna la rete, non arrivano più i promemoria delle riunioni già finite; quelli
   delle riunioni non ancora finite arrivano con una riga in più: "Promemoria in ritardo: era alle 08:45".**
   Un promemoria che arriva alla sua ora è identico a prima.
   Passi: nel calendario tre eventi di prova con il promemoria 15 minuti prima. Il primo fra 20 minuti, di un'ora; il
   secondo fra 20 minuti, di 10 minuti; il terzo fra un'ora e mezza. Sospendi il PC (o stacca la rete e riavvia
   Missiva) prima dell'ora dei primi due promemoria, e riaprilo 40 minuti dopo: entro un minuto arriva una notifica
   sola, quella del primo evento, con tre righe: il titolo, "alle HH:MM", "Promemoria in ritardo: era alle HH:MM". Del
   secondo, finito, nessuna notifica; nel registro la riga "promemoria del calendario: uno in ritardo non mostrato, la
   riunione era già finita". Il promemoria del terzo arriva alla sua ora, con due righe come sempre.
2. **Scelta 15. Con Gmail e gli account IMAP, se il server rifiuta un'azione di una regola, le altre azioni vanno
   avanti e il messaggio viene riprovato ai controlli dopo (ogni due minuti), fino a tre volte; prima quei messaggi
   restavano nella Posta in arrivo per sempre. Esegui ora dice che cosa è riuscito e che cosa no.**
   Con una casella vera un rifiuto non si provoca a mano. Quello che si vede di sicuro: le regole di tutti i giorni
   funzionano come prima (una regola "sposta" su un messaggio nuovo: il messaggio va nella cartella e non arriva la
   notifica). Dopo qualche giorno, nel registro, cerca "regole di Missiva": le righe con "azioni non riuscite", "non
   finiti, si riprovano" o "lasciati" dicono che è successo; se compare "lasciati come sono dopo 3 tentativi", è da
   guardare.
   Con i dati finti (dopo l'unione in main e una compilazione in `Missiva-sorgente`; comandi scritti e mai eseguiti):
   `loop\bug-I\prova-finta.ps1`; in Impostazioni, Posta, Regole, sul primo account, una regola "Se l'oggetto contiene
   Fattura: segna come letto e sposta in" una cartella; poi `loop\bug-I\prova-finta.ps1 -Server 1 -Do "rifiuta UID
   STORE"` e `-Server 1 -Do "arrivo Fattura di prova"`, senza toccare altro. Il messaggio compare nella Posta in
   arrivo, da leggere, con la sua notifica; al controllo dopo (entro due minuti) diventa letto ed esce dall'elenco,
   nella cartella della regola. Nel registro: "regole di Missiva su ...: non riuscito (2): ...", "un messaggio non
   finito, si riprova al prossimo controllo", poi "1 di 1 messaggi corrispondono (0 nuovi, 1 riprovati)".
3. **Scelta 17. Un messaggio lasciato aperto in una finestra sua mentre aggiungi un account o una casella condivisa (o
   ne rimuovi un altro) continua a funzionare: prima ogni comando dava un errore che diceva di chiudere e riaprire.**
   Passi: doppio clic su un messaggio di un account Gmail (o IMAP) con un allegato: si apre nella sua finestra. Senza
   chiuderla: Impostazioni, Account, aggiungi una casella condivisa o un account (o rimuovine un altro). Torna alla
   finestra del messaggio: Già letto/Da leggere e Contrassegna non danno errori; l'allegato si apre; il menu "..." si
   apre; Rispondi apre la scrittura; infine Elimina manda il messaggio nel Cestino e chiude la finestra. Seconda prova,
   con un account che puoi riaggiungere: apri un suo messaggio in una finestra e rimuovi proprio quell'account. Nella
   finestra rimasta Già letto/Da leggere dà "Operazione non riuscita", con "... è stato chiuso: l'account è stato
   rimosso da Missiva ...".
4. **Scelta 8. Niente che si noti**: su un account IMAP, cercando di nuovo le stesse parole in tutte le cartelle dopo
   più di mezzo minuto, la ricerca interroga di nuovo il server (prima dopo più di un minuto). Non c'è un passo a mano
   utile: con Gmail e Microsoft non cambia nulla.
5. **Scelta 18. Niente con Gmail.** Con un account IMAP che scrive le cartelle con il punto (`INBOX.Clienti.Rossi`):
   metti fra i Preferiti una sottocartella e dalle un colore, rinomina la cartella che la contiene: Preferito e colore
   restano sulla sottocartella. Prima si perdevano.
6. **Scelta 36. Niente che si veda.** Inoltra da Gmail un messaggio con un allegato e salva la bozza: l'allegato c'è,
   come prima (è la prova 9 del riepilogo, "Rispondi e inoltra con un allegato").

### Scelte fatte qui, da rivedere

- **Scelta 3.**
  - "In ritardo" è oltre due minuti dalla sua ora. I giri sono ogni 30 secondi, ma con la rete appena tornata la
    lettura dei calendari può tenerne fermo uno: sotto i due minuti nulla cambia, nemmeno per una riunione finita.
  - "era alle ..." è l'ora in cui il promemoria doveva arrivare, in una terza riga; l'ora della riunione resta sopra,
    scritta al presente anche se la riunione è cominciata ("alle 08:30").
  - Il tetto guarda solo la fine della riunione: una riunione di tre giorni non finita fa suonare lunedì il promemoria
    di venerdì, con "era venerdì 16 ottobre alle 17:15". Un evento di tutto il giorno vale fino a mezzanotte.
  - Più promemoria in ritardo arrivano ancora uno per notifica, non raggruppati.
  - Una fine in un'ora che c'è due volte (la notte del 25 ottobre) vale come la seconda: quella notte un promemoria
    in ritardo può suonare per una riunione finita da meno di un'ora, mai il contrario.
- **Scelta 15.**
  - Un messaggio con un'azione non riuscita non viene spostato in quel giro, nemmeno se lo spostamento sarebbe
    riuscito: l'alternativa lo spostava senza il segno, o senza la copia, e non lo ritrovava più.
  - Dopo una copia non riuscita il resto va avanti al controllo dopo: il messaggio finisce spostato senza la copia, e
    lo dice solo il registro.
  - Uno spostamento non riuscito su un server senza MOVE, o per un errore che non è un rifiuto, non si riprova: il
    messaggio resta nella Posta in arrivo (com'era prima per tutti).
  - Tre tentativi; i non finiti stanno solo in memoria, quindi chiudendo Missiva non si riprovano più (le impronte in
    `RulesSeen` li danno per guardati, come prima).
  - Il messaggio non finito al primo controllo viene annunciato con la notifica (è nella Posta in arrivo, da leggere),
    e uno o due controlli dopo sparisce dall'elenco quando la regola riesce.
  - Se non si riesce a vedere quali messaggi ci sono ancora, quel controllo non li tocca e conta come un tentativo.
  - Con la rete che cade a metà ogni gruppo viene tentato lo stesso, uno dopo l'altro: non si ferma al primo errore
    di rete.
  - In "Esegui ora" premere di nuovo dopo un errore rifà tutto, copie comprese, come prima: lì non c'è memoria di che
    cosa è già stato fatto.
  - Gli errori nel registro sono fra virgolette nelle versioni di prova e nascosti in quella installata, come le
    azioni fatte: nel registro vero si legge quante azioni non sono riuscite, non quali.
- **Scelta 17.**
  - Il testo dell'errore di un provider chiuso è cambiato per tutte le finestre, non solo per questa.
  - Una finestra aperta sulla testa di una conversazione, dopo il ricarico, se l'elenco di adesso non ha quella riga
    lavora sul solo messaggio mostrato, non su tutta la conversazione.
  - Le altre finestre che possono tenere un provider di prima (l'anteprima di un allegato già aperta, un .eml aperto
    da un messaggio, la pagina Regole delle Impostazioni) non sono state guardate: danno ancora l'errore, ora con il
    testo giusto.
  - `ReadingView.provider` è una proprietà con il nome minuscolo, per non toccare la cinquantina di righe che la usano.
- **Scelta 8.** 30 per tutti e due, come proposto: su IMAP la stessa ricerca ripetuta fra 30 e 60 secondi costa una
  SEARCH per cartella in più.
- **Scelta 18.** Il separatore si legge dalle cartelle che Missiva ha in elenco, non si chiede al server: per un
  account senza nessuna sottocartella vale la barra, e lì non c'è nulla da far seguire.
- **Scelta 36.** Gmail ha ora la stessa regola degli account IMAP, senza eccezioni per le Bozze: da qui il comando in
  più alla prima ripresa di una bozza con allegati.

### Non verificato

- **Niente a finestra**: la notifica con tre righe (come Windows la mostra, se la terza riga si vede intera nel centro
  notifiche); la finestra del messaggio dopo un ricarico vero degli account, con il riquadro che carica dal provider
  nuovo; "Esegui ora" con un errore; le righe dell'elenco che cambiano quando una regola finisce in ritardo. I passi
  sono qui sopra.
- **Nessun server vero.** In particolare:
  - scelta 15: che Gmail e i server IMAP veri, rifiutando un `UID MOVE`, lascino davvero ogni messaggio spostato o
    intatto (è quello che chiede RFC 6851, non una misura); come rispondono davvero quando sono sovraccarichi; quanto
    dura un giro di regole con la connessione morta, ora che ogni gruppo viene tentato;
  - scelta 36: Gmail vero. Che per Gmail `\Recent` non conti (non lo usa) è quello che si sa, non una misura: se è
    così, per Gmail la prima divergenza è solo prudenza. Il comando in più alla ripresa di una bozza non è stato
    misurato in tempo su Gmail vero (un'apertura di cartella lì costa da 0,1 a 0,3 secondi, dalle misure di prima);
  - scelta 18: un server IMAP vero con il punto; che `ParentId` arrivi sempre come percorso della madre lo dice il
    codice di `ImapProvider.GetFoldersAsync`, letto.
- **Scelta 3**: `CheckRemindersAsync` a finestra con un calendario vero; la notte del 25 ottobre dal vero; un evento
  di Google di tutto il giorno (qui eventi costruiti a mano, con la fine a mezzanotte del giorno dopo).
- **Scelta 15**: due controlli dello stesso account insieme (il secondo parte se il primo è fermo da due minuti): i
  non finiti si prendono sotto lucchetto, letto e non provato. `App.CheckAccountAsync` a finestra.
- **Scelta 17**: il riquadro (qui il sorgente, e `ComposeWindow.Current` con i provider veri sul server finto); un
  ricarico degli account mentre una richiesta della finestra è in corso; la versione di prova con `TestingProvider`.
- **La Missiva installata e la versione finale di prova** non sono state eseguite; `prova-release` non è stata
  ricostruita. La suite è codice solo di Debug.

## Scelte del 12 ottobre, terzo gruppo: prove e guardie per chi sviluppa (scelte 26, 30, 33, 48)

Terzo dei quattro blocchi con cui si mettono in pratica le scelte che l'autore ha lasciato alla sessione che coordina il
lavoro ("fai tutto ciò che ritieni giusto"). Sul ramo `scelte`, albero di lavoro `C:\Progetti\Missiva\scelte`; la
cartella `Missiva-sorgente` resta ferma sul commit installato, `33aed26`.

Fatto il 13 ottobre 2026 su quattro scelte dell'elenco "3. Scelte per l'autore": 26 (T-28), 30 (C-37), 33 (C-65) e 48
(C-47, i primi due dell'elenco). "Prima" è il commit `ee2f7c2`, la fine del secondo gruppo. Commit: `0d46423` (C-65),
`ad08a1e` (C-47: le prove e il server IMAP finto), `593eb32` (un difetto trovato con le prove di C-47: Elimina
cartella), `35718f1` (C-37), `b872639` (T-28), più quello di questo esito. Tutte fatte; nessuna fermata.

Il compito era di sole prove e di due guardie per chi sviluppa: niente doveva cambiare nella versione che l'autore usa. Una
cosa cambia lo stesso, perché le prove nuove hanno trovato un difetto vero, corretto in un commit suo: **dopo Elimina
cartella non restano più il Preferito, il colore e le copie sul PC di quella cartella** (`593eb32`). Il resto non tocca
la versione installata: prove, il server finto delle prove, una riga del progetto che vale solo in Debug, e la
partenza delle sole versioni di prova.

Come si è provato:

- **Dopo**: `Choices3Tests` (`Core/ProvaBozze.Scelte3.cs`, da soli in tre secondi con `--prova-audit risultato.txt
  scelte3`), 39 controlli e tre righe di sola misura. Dati finti, i server IMAP finti e un server SMTP finto su
  127.0.0.1, gestori finti per Graph e per Google; nessuna finestra.
- **Visti cadere**: 24 dei 39.
  - Scelta 33: 7 su 8. Il primo tentativo, spegnere a mano una riga di `TestingProvider`, è stato rifiutato dal
    controllo dei permessi dello strumento con cui lavoro (lo ha preso per la rimozione di una protezione). Lo stesso
    si è visto senza toccare `TestingProvider`: un nome tolto dagli elenchi della prova (cade il primo, con il nome),
    una lettura messa fra le scritture (cade il secondo: "ARRIVANO AL PROVIDER VERO"), una scrittura messa fra le
    letture (cade il terzo), un file temporaneo che crea un `ImapProvider` (cade il quinto), e i tre sui server finti
    chiamando i provider senza `TestingProvider` (cadono con l'elenco dei comandi che scrivono: `UID STORE`, `CREATE`,
    `UID MOVE`, `APPEND`, i `PATCH` e i `DELETE` di Graph). Il quarto è il controllo che la prova veda.
  - Scelta 48: 14 su 28. Sono prove di codice che esiste, senza una modifica da spegnere, tranne i 4 del difetto
    corretto, visti cadere prima della correzione e con la correzione spenta a mano. Per gli altri si è guastato a
    mano il server finto: con un RENAME che dimentica le sottocartelle cade la rinomina; con un DELETE che risponde OK
    senza eliminare (com'era fino all'11 ottobre) cadono i 3 dell'eliminazione; con un `UID STORE` che non segna cadono
    i 6 di "segna tutti come letti" su IMAP e Gmail. Non visti cadere: nuova sottocartella (3), sposta (3), rifiuti del
    server (3), la rinomina sugli altri due server, la sottocartella su una connessione nuova, Graph, e il sorgente di
    "segna tutti come letti".
  - Scelta 30: l'unico, due volte: togliendo la riga dal progetto, e mettendo la condizione sulla costante DEBUG invece
    che sulla configurazione (allora si fermerebbe la versione finale con gli strumenti di prova).
  - Scelta 26: tutti e due, con l'eccezione di `bin\Debug` rimessa nella regola e con una riga di registro aggiunta
    alla chiusura. Con l'eccezione rimessa cade anche il controllo di T-28 del terzo gruppo della sicurezza.
- **Le finestre non si aprono.** Le azioni sulle cartelle e "segna tutti come letti" si provano con il provider vero
  sul server finto e poi, una per una, con le cose che la finestra fa dopo (`MigrateFolderKeys`, `ForgetFolderKeys`,
  le cartelle rilette in un `AccountItem`, la copia locale, il riconteggio dei non letti); il sorgente della finestra
  si legge senza i commenti (`Ch1Code`) per vedere che le chiami in quell'ordine.
- **Nessun eseguibile è stato avviato senza `MISSIVA_DATA`**: della scelta 26 si prova la funzione che decide.
- Ogni prova lunga gira in un `try` (`Ch3StepAsync`): se si ferma a metà cade un controllo che dice dove, e le altre
  girano lo stesso.

### Le scelte

- **Scelta 33, C-65** (una prova per `TestingProvider`). FATTA (`0d46423`). Nessun metodo che oggi scrive passa.
  - **L'elenco di oggi, letto metodo per metodo sui tre provider** (`GraphProvider`, `GmailProvider` con calendario e
    contatti, `ImapProvider`). `IMailProvider` ha 75 metodi con 71 nomi. `TestingProvider` ne passa al provider vero
    31 (30 nomi), e nessuno scrive:
    - Graph: solo richieste GET, più una POST che non scrive nulla (`getSchedule`, la disponibilità dei partecipanti).
      I token dei permessi in più si chiedono senza finestra (`interactive: false`);
    - Gmail e IMAP: `LIST`, `STATUS`, `SELECT` o `EXAMINE`, `FETCH`, `SEARCH`, `NOOP`, `IDLE`. Il corpo, gli allegati,
      le intestazioni e l'origine si leggono sempre con `BODY.PEEK` (MailKit non chiede mai `BODY[]`): aprire un
      messaggio non lo segna letto; lo segna la finestra con `SetReadAsync`, che `TestingProvider` ferma;
    - Google (calendario, contatti): GET, più la POST `freeBusy`, che legge soltanto;
    - `ResetConnectionsAsync` chiude solo le connessioni di Missiva; `GetCategoriesAsync` di IMAP legge le
      impostazioni; `KeptBody` e `SavedBody` leggono la memoria e la copia sul PC.
    - L'unico effetto sul server di una lettura: dove la cartella si apre con `SELECT` (l'elenco, la ricerca in una
      cartella, il corpo, l'allegato aperto dal riquadro) i suoi messaggi perdono `\Recent`, come con ogni programma di
      posta che la apre. Nient'altro.
  - Gli altri 44 (41 nomi) scrivono, inviano o aprono una finestra di consenso, e `TestingProvider` non li passa. Due
    chiedono al provider vero solo l'elenco delle cartelle (`EnsureTrashAsync`, `EnsureFolderAsync`).
  - **Il controllo, per riflessione**: ogni metodo dell'interfaccia deve stare in uno dei due elenchi della prova
    (`Ch3Reads`, `Ch3Writes`). Uno nuovo, che non sta in nessuno, fa cadere il controllo con il suo nome e con che
    cosa fare ("leggi che cosa fanno sui tre provider e mettili in..."). Poi ogni metodo si chiama su
    `TestingProvider` sopra un provider finto che scrive il nome di ogni chiamata che gli arriva (`Ch3Recorder`): al
    provider vero possono arrivare solo nomi dell'elenco delle letture, e ogni lettura deve arrivargli (una
    dimenticata risponderebbe con il valore di base dell'interfaccia, senza dirlo). Gli argomenti li costruisce la
    prova dal tipo (`Ch3Sample`): un tipo che non sa costruire fa cadere il controllo, non lo salta.
  - **Il controllo che la prova veda**: lo stesso giro su un provider che inoltra tutto trova 44 scritture su 44.
  - **I provider veri nascono in un posto solo** (`MainWindow.NewProvider`, che nella versione di prova li mette dentro
    `TestingProvider`): un controllo legge tutti i sorgenti. Un secondo posto li userebbe senza protezione.
  - **L'altra metà: l'elenco delle letture dice il vero?** La prova con il provider finto non lo può sapere. Tre
    controlli chiamano tutti i 75 metodi su `TestingProvider` sopra i tre provider veri, collegati ai server finti,
    con un messaggio e un allegato che esistono:
    - account IMAP: 86 comandi (nei giri guardati), solo `EXAMINE`, `FETCH`, `IDLE`, `LIST`, `NOOP`, `SELECT`,
      `STATUS`, `UID FETCH`, `UID SEARCH`; nessun `BODY[]` senza PEEK; messaggi, segni e cartelle come prima; al server
      SMTP finto nulla;
    - account Gmail: 71 comandi, gli stessi; a Google 3 richieste (calendari, contatti, `freeBusy`);
    - account Microsoft, con tutti i consensi dati: 43 richieste, tutte GET tranne `getSchedule`.
    Se domani una lettura segnasse letto il messaggio che apre, cadrebbe qui.
  - Controlli: 8.
- **Scelta 48, C-47** (copertura che manca: le cartelle, e "segna tutti come letti"). FATTA (`ad08a1e`), con un
  difetto trovato e corretto (`593eb32`).
  - **Il server IMAP finto** (`FakeImap`, lo stesso di `--imap-finto`):
    - RENAME vero: la cartella prende il nome nuovo con i suoi messaggi (stessi UID e segni) e con le sue
      sottocartelle; nella variante Gmail cambia anche l'etichetta sui messaggi delle altre cartelle. Un nome che c'è
      già e una cartella che non c'è ricevono NO;
    - DELETE vero: la cartella sparisce con i suoi messaggi;
    - resta BAD per quello che continua a non fare: rinominare la Posta in arrivo, eliminare una cartella che ne
      contiene altre, e i comandi che non conosce (`SUBSCRIBE`, `CLOSE`, `UNSELECT`);
    - nella variante Gmail una cartella creata ora compare in LIST: prima CREATE rispondeva OK e la cartella non si
      vedeva, quindi lì creare una cartella o un'etichetta non era provabile;
    - `ExactList`: LIST di un nome preciso risponde solo per quello, come un server vero.
  - **(a) Cartelle**, su tre server: IMAP con la barra, IMAP con il punto e le cartelle dentro INBOX, Gmail. Per
    ognuno sei controlli:
    - nuova sottocartella: dentro una cartella dell'utente e dentro la Posta in arrivo (in Gmail nasce in cima); gli
      spazi attorno al nome si tolgono; rilette le cartelle sta sotto la sua, e un messaggio arrivato lì si conta;
    - rinomina, con la cartella aperta: sul server stessi messaggi e sottocartella al seguito; in Missiva la cartella
      riletta ha gli stessi messaggi e gli stessi non letti (2 su 3), il corpo si riapre; Preferiti, colori, cartella
      chiusa e ordine scelto trascinando seguono lei e la sottocartella. In Gmail un messaggio della Posta in arrivo
      con quell'etichetta ha la categoria con il nome nuovo e il colore di prima;
    - sposta: dentro un'altra cartella con la sottocartella al seguito, poi la sottocartella al primo livello;
    - il server rifiuta: una rinomina verso un nome che c'è già e un'eliminazione rifiutata danno un errore, e nulla
      cambia;
    - elimina, con la cartella aperta e un messaggio da leggere: sparisce dal server e dall'elenco, le altre cartelle
      e i non letti della Posta in arrivo restano;
    - della cartella eliminata non resta nulla (il difetto corretto, qui sotto).
    E un controllo per tutti e tre: una sottocartella rinominata come prima cosa su una connessione nuova, con un
    server che a LIST risponde solo per il nome chiesto, resta dentro la sua cartella. È il caso che C-37 aveva lasciato
    "solo compilato": MailKit chiede da sé al server la cartella che la contiene (un LIST con il suo nome).
  - **Il difetto trovato: Elimina cartella lasciava quasi tutto** (`593eb32`).
    - Prima, misurato sui tre server: dopo Elimina uscivano dalle impostazioni solo "nascosta" e "chiusa". Restavano
      il Preferito, il colore, il posto nell'ordine, la copia della prima pagina dell'elenco e la copia del corpo dei
      messaggi aperti. Una cartella nuova con lo stesso nome nasceva fra i Preferiti e con il colore di prima; la
      ricerca senza rete trovava ancora il messaggio della cartella eliminata. Le copie dei corpi (cifrate) restavano
      sul disco fino a 30 giorni, quella dell'elenco finché l'account non usciva da Missiva.
    - Con gli account Microsoft la cartella eliminata resta dentro Posta eliminata con lo stesso identificativo:
      letto dal codice, restava anche fra i Preferiti. Non provato (le cartelle su Graph non sono in questo blocco).
    - Ora `MainWindow.ForgetFolderKeys`, chiamata dopo un'eliminazione riuscita, toglie Preferito, nascosta, chiusa,
      colore, posto nell'ordine, le copie dei corpi (`BodyCache.ForgetFolder`, come già faceva Svuota) e la copia della
      prima pagina (`MailCache.ForgetFolder`, nuova). Solo di quella cartella. Se il server rifiuta non si toglie
      nulla.
    - Controlli: 4 (i tre server, e il sorgente della finestra).
  - **(b) `MarkAllReadAsync`**, che nessun controllo chiamava.
    - IMAP e Gmail, tre controlli ciascuno. Una cartella con 2 da leggere su 3: una ricerca e un solo comando (`UID
      STORE 6:7 +FLAGS.SILENT (\Seen)`), le altre cartelle non cambiano; rilette le cartelle Missiva mostra 0 lì e 3
      nella Posta in arrivo. La Posta in arrivo con 3 da leggere su 5: un solo comando, i contrassegni restano. Una
      cartella senza nulla da leggere: una ricerca e nessun comando che scrive.
    - Microsoft, un controllo, su un Graph finto: 120 da leggere su 130 diventano letti con 120 PATCH, uno per
      messaggio e nessuno due volte, chiesti in 4 pagine (50, 50, 20, 0).
    - **Il conto che Missiva mostra per la Posta in arrivo arriva un attimo dopo**, per come sono fatti i non letti
      (`UnreadSync`): subito dopo una scrittura il numero del server non si prende, perché potrebbe essere quello di
      prima. Rilette le cartelle Missiva mostra ancora 3 (o 120); il riconteggio (`App.RecountSoon`, qui rifatto dalla
      prova) legge 0 dal server e lo scrive. Nella versione installata l'attesa è di circa un secondo; nella prova è
      accorciata. Per le altre cartelle il numero nuovo arriva subito, con le cartelle rilette.
    - Un controllo legge il sorgente: nella finestra "Contrassegna tutti come letti" fa proprio questi passi.
  - Controlli: 28. Sei per ognuno dei tre server (18), la sottocartella su una connessione nuova (1), il sorgente di
    Elimina cartella (1), "segna tutti come letti" (8). I 4 del difetto sono fra questi: uno per server, più il
    sorgente.
- **Scelta 30, C-37** (avvisi di nullità come errori, solo in Debug). FATTA (`35718f1`).
  - In `Missiva.csproj`, solo con la configurazione Debug: `WarningsAsErrors` con `nullable`. Un avviso CS86xx nuovo
    ferma `dotnet build`. La condizione è sulla configurazione, non sulla costante DEBUG.
  - **Le tre compilazioni vere**, con un avviso di nullità messo apposta in un file temporaneo (CS8603) e uno che non
    lo è (CS0219), poi tolti:

    | Compilazione | CS8603 | CS0219 | Esito |
    |---|---|---|---|
    | Debug (`dotnet build`) | errore | avviso | codice 1, 1 errore |
    | Release (`dotnet build -c Release`) | avviso | avviso | codice 0, 0 errori |
    | Release con `-p:MissivaProva=true -p:DefineConstants=DEBUG%3BTRACE` | avviso | avviso | codice 0, 0 errori |

    Le due Release sono state compilate (`dotnet build`, non `dotnet publish`: il compilatore è lo stesso) in cartelle
    di uscita del lavoro, poi cancellate.
  - **Che cosa dice MSBuild**, senza compilare: gli avvisi dati al compilatore come errori sono
    `;NU1605;nullable;SYSLIB0011` in Debug e `;NU1605;SYSLIB0011` nelle tre Release (quella che si installa, la
    versione finale di prova, quella con gli strumenti di prova). Prima erano `;NU1605;SYSLIB0011` dappertutto.
  - Tutto il codice di oggi, prove comprese, compila in Debug con la regola accesa: 0 errori.
  - Controlli: 1.
- **Scelta 26, T-28** (la Debug avviata senza `MISSIVA_DATA` si chiude sempre). FATTA (`b872639`).
  - **Prima, la ricerca: nessuno avvia la Debug senza `MISSIVA_DATA`.**
    - Nel repository: dei dodici script di `strumenti\` due avviano l'eseguibile, `prova.ps1` e `avvio.ps1`, e la
      impostano tutti e due; negli altri non c'è nessun avvio. `installa.ps1` e `disinstalla.ps1` non toccano la
      Debug. Non ci sono `launchSettings.json` né altre configurazioni di avvio.
    - In `loop\` (ricerca di testo in 74 `.ps1`, 7 `.py`, 24 `.cs`, 2 `.md` e 96 `.txt`, senza le cartelle `lampi-*`):
      18 script avviano l'eseguibile, e tutti impostano `MISSIVA_DATA` qualche riga prima; altri 24 passano da
      `prova.ps1`.
    - `CLAUDE.md` e `LEGGIMI.md`: nessuna istruzione che la avvii senza.
    - L'unica traccia è nel diario di `loop\`: il 28 settembre chi scrisse la prima regola annotò "Il Debug con doppio
      clic resta com'era (sviluppo)". Era una scelta di allora, non un uso: nessuno script la sfrutta.
  - Prima: la versione finale di prova si chiudeva sempre; la Debug solo se avviata da una sua notifica rimasta o da
    fuori `bin\Debug`. Dalla sua cartella, con un doppio clic o da uno script senza la variabile, partiva sui dati veri.
  - Ora `Program.ClosesWithoutData` guarda solo la variabile (mancante o vuota: la stessa regola di `Store.Dir`), non
    la cartella dell'eseguibile né gli argomenti. Si chiude sempre.
  - **A chi l'ha avviata così**: una riga sull'uscita degli errori, che arriva a uno script che la raccoglie e alla
    console da cui è stata avviata, se c'è: "Missiva di prova (DEBUG) non parte senza MISSIVA_DATA: lavorerebbe sui
    dati veri, quelli della Missiva installata. Avviala con strumenti\prova.ps1, oppure imposta MISSIVA_DATA con una
    cartella di prova. Eseguibile: ...". E il codice di uscita 2. Nessuna finestra (la avvia anche Windows, per una
    notifica) e nessuna riga nel registro, che senza la variabile starebbe fra i dati veri.
  - Non passano da questa regola, com'era: `--pdf-worker` e gli strumenti da riga di comando della Debug
    (`DevTools.TryRun`), che escono prima. Vedi "Trovato in più".
  - Controlli: 2. Il controllo di T-28 del terzo gruppo della sicurezza ora dice la cosa nuova.

### Trovato in più

- **Un tentativo di rete vera, fermato dal firewall.** Per vedere cadere i tre controlli sui server finti ho chiamato
  una volta tutti i metodi sui provider senza `TestingProvider`. Per l'account IMAP e per Graph tutto restava sul PC.
  Per l'account Gmail finto (`prova@example.com`, token di prova) l'invio non aveva un server SMTP finto, e
  `GmailProvider.SendAsync` ha provato a collegarsi a `smtp.gmail.com:587`: il collegamento non si è aperto
  (`SocketException`, "Tentativo di accesso al socket con modalità non consentite", cioè fermato dal firewall), quindi
  niente è partito, né credenziali né messaggio; può essere partita la sola richiesta DNS del nome. Non doveva
  succedere: le regole dicono solo 127.0.0.1. Da allora i due controlli hanno un server SMTP finto
  (`GmailProvider.TestSmtpPort`, e il server dell'account IMAP), e contano che non gli arrivi nulla: se un invio
  sfuggisse a `TestingProvider` arriverebbe lì.
- **Elimina cartella lasciava Preferito, colore e copie**: vedi la scelta 48.
- **La copia locale non segue una cartella rinominata o spostata** (misura, tre righe nel risultato). La copia della
  prima pagina e le copie dei corpi restano con l'identificativo di prima: i messaggi della cartella rinominata si
  riscaricano, e la ricerca senza rete trova quelli di prima sotto il nome di prima, fino a 30 giorni. Non corretto:
  spostare le copie vuol dire riscriverle (dentro c'è l'identificativo della cartella), e toglierle fa perdere la
  lettura senza rete di quei messaggi. È una scelta, non una riga.
- **Gli errori delle azioni sulle cartelle, per Gmail e IMAP, sono in inglese.** Un rifiuto del server arriva alla
  finestra "Operazione non riuscita" con il testo di MailKit: "The IMAP server replied to the 'RENAME' command with a
  'NO' response: ..." seguito da quello che ha scritto il server. Non toccato.
- **MailKit non manda CLOSE prima di rinominare o eliminare la cartella aperta.** `RenameFolderAsync` e
  `DeleteFolderAsync` chiamano `CloseAsync`, che senza l'estensione UNSELECT non manda nulla: al server arriva RENAME o
  DELETE con la cartella ancora aperta su quella connessione. Sul server finto va. Su un server vero non è provato:
  alcuni possono rifiutare.
- **Il ramo aggiunto da C-37 in Gmail** (la cartella madre chiesta al server quando MailKit non la conosce) in queste
  prove non viene mai raggiunto: MailKit la chiede da sé prima.
- **"Segna tutti come letti" con Microsoft ha un tetto** (letto, non provato): 400 pagine da 50, cioè 20.000 messaggi.
  Oltre, gli altri restano da leggere senza che nulla lo dica; un secondo clic li finisce.
- **Gli strumenti da riga di comando della Debug restano fuori dalla regola di `MISSIVA_DATA`** (letto, non provato).
  `--prova-bozze` e `--prova-audit` senza la variabile lavorano fra i file temporanei. Ma `--imap-finto` senza la
  variabile scrive le sue righe nel registro vero (`Log.Write`) e cerca `comandi.txt` nella cartella dei dati vera
  (`Store.Dir`). Gli altri (`--page`, `--page-thread`, `--testo`, `--scrivi-inviti`, `--scrivi-pec`) non sono stati
  letti fino in fondo: di sicuro un loro errore finirebbe nel registro vero. Tutti gli script che li avviano impostano
  la variabile. Vedi "Scelte per l'autore".
- **La Debug già compilata in `Missiva-sorgente\bin\Debug`** è quella di prima: finché non si ricompila (`prova.ps1
  -Build`), avviata senza `MISSIVA_DATA` parte ancora sui dati veri.
- **Il server di `--imap-finto`** è lo stesso server: nella versione di prova con i dati finti (`prova-finta.ps1`)
  rinominare ed eliminare una cartella ora funzionano; dal 12 ottobre davano un errore, e prima ancora fingevano.
- **Un controllo di prima è caduto una volta**: "T-23, un 307 del server a cui si caricano i pezzi" (terzo gruppo
  della sicurezza), in un giro del solo gruppo, con `SocketException` 10053 su una connessione fra due ascoltatori
  di 127.0.0.1. Nei sei giri del gruppo fatti subito dopo e nel giro intero finale non è più caduto, e non era caduto
  nei tre giri interi di prima. Quel codice non è stato toccato. Va aggiunto ai controlli instabili del riepilogo.
- Un heredoc di Bash con virgolette e barre rovesciate non è partito (errore della shell, nessun file toccato): quella
  modifica è stata fatta con Edit.

### Controlli e suite

- `Choices3Tests`, 39 controlli: scelta 33 (8), scelta 48 (28), scelta 30 (1), scelta 26 (2). Tre righe di sola
  misura: la copia locale dopo una rinomina, una per server.
- **Due controlli di prima dicono una cosa nuova**, nessuno è sparito:
  - "C-43, un comando che il server IMAP finto non ha riceve BAD..." (terzo gruppo del codice): provava il BAD proprio
    con RENAME e DELETE, che ora il server fa. Ora lo prova con `SUBSCRIBE` e con l'eliminazione di una cartella che
    ne contiene un'altra;
  - "T-28, senza MISSIVA_DATA un eseguibile con i ganci di prova..." (terzo gruppo della sicurezza): diceva "la Debug
    nella sua cartella no, com'era"; ora "si chiude da solo: sempre".
- Suite completa (`--prova-bozze`), sul codice di `b872639`: **1111 controlli, tutto bene, in 157 secondi**. Dopo quel
  giro sono cambiati solo i documenti. Giri interi prima: sul codice di `593eb32` 1108, tutto bene, in 162 secondi; sul
  codice di `ad08a1e` 1104, tutto bene, in 163. Prima di cominciare, sul codice di `ee2f7c2`: 1072, tutto bene, in 168.
  Sono 39 in più, quelli di `Choices3Tests`.
- I nomi dei controlli del primo e dell'ultimo giro sono stati confrontati con uno script, tolte le cifre: nessuno è
  sparito; 39 in più; cambiano i due qui sopra, tre righe che hanno dentro un pezzo casuale, e "S-..., l'ambiente del
  processo dei PDF", che elenca i nomi delle variabili d'ambiente come li scrive la shell che avvia la suite (il primo
  giro è partito da PowerShell, l'ultimo da Git Bash, che li scrive in maiuscolo).
- Compilazione da capo (`--no-incremental`) prima dell'ultimo giro: Release 0 errori e 0 avvisi, Debug 0 errori e 0
  avvisi (con la regola nuova accesa). La versione finale di prova non è stata ricostruita né pubblicata.

### Che cosa vede l'autore, e come provarlo

Niente di questo è stato provato a finestra.

1. **Scelta 33. Niente.** È una prova in più nella suite.
2. **Scelta 48. Dalle prove niente; dal difetto corretto: una cartella eliminata esce anche dai Preferiti, e una
   cartella nuova con lo stesso nome non ne eredita il colore.**
   Passi, su un account dove puoi creare e cancellare una cartella: tasto destro su una cartella, "Nuova
   sottocartella", chiamala "Prova di ottobre"; mettila fra i Preferiti e dalle un colore; spostaci un messaggio e
   aprilo. Poi tasto destro, Elimina. La cartella sparisce dall'albero e dai Preferiti. Crea di nuovo "Prova di
   ottobre" nello stesso posto: non è fra i Preferiti, non ha colore, è vuota. Con un account Microsoft: dopo Elimina
   la cartella sta dentro Posta eliminata, e non è più fra i Preferiti (prima ci restava).
   Con i dati finti (dopo l'unione in main e una compilazione in `Missiva-sorgente`; comandi scritti e mai eseguiti):
   `loop\bug-I\prova-finta.ps1`, poi sul primo account rinomina la cartella Clienti, spostala dentro un'altra,
   eliminala: tutte e tre riescono, senza errori.
3. **Scelta 30. Niente nell'uso.** Se una compilazione di prova (`prova.ps1 -Build`, `dotnet build`) si ferma con un
   errore CS86xx, è questo: un valore che può essere null usato come se non lo fosse. `installa.ps1` e `prova.ps1
   -Release -Build` non si fermano per questo.
4. **Scelta 26. La versione di prova si avvia solo con `prova.ps1`** (o con `MISSIVA_DATA` impostata): con un doppio
   clic su `bin\Debug\...\Missiva.exe` non succede nulla, si chiude subito. `prova.ps1` e gli strumenti di `loop\`
   funzionano come prima.
   Dopo l'unione in main: `strumenti\prova.ps1 -Build` una volta, perché la Debug già compilata in `Missiva-sorgente`
   è ancora quella di prima. Se vuoi vedere la riga, solo dopo quella compilazione, da PowerShell:
   `Remove-Item Env:MISSIVA_DATA -ErrorAction SilentlyContinue; $p = Start-Process
   C:\Progetti\Missiva\Missiva-sorgente\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe -PassThru -Wait
   -RedirectStandardError $env:TEMP\missiva-senza-dati.txt; $p.ExitCode; Get-Content $env:TEMP\missiva-senza-dati.txt`.
   Deve dare 2 e la riga "Missiva di prova (DEBUG) non parte senza MISSIVA_DATA...", senza nessuna finestra. Non è
   stato eseguito: qui era vietato avviare l'eseguibile senza la variabile.

### Scelte per l'autore

- **Gli strumenti da riga di comando della Debug senza `MISSIVA_DATA`**: `--imap-finto`, `--page`, `--page-thread`,
  `--testo`, `--scrivi-inviti` e `--scrivi-pec` devono chiudersi anche loro, come ora fa la finestra? Proposta: sì,
  tutti tranne la suite (che senza la variabile lavora fra i file temporanei): è spostare una riga in `Program.Main`
  più un controllo. Nessuno script di oggi li avvia senza.
- **La copia locale dopo una rinomina o uno spostamento di cartella**: lasciarla com'è (le copie di prima restano
  fino a 30 giorni e si trovano nella ricerca senza rete sotto il nome di prima), toglierla (come ora dopo Elimina), o
  farla seguire (riscrivere le copie)? Proposta: toglierla, se cerchi senza rete di rado; costa che i messaggi di
  quella cartella già aperti si riscaricano alla prima apertura, cosa che succede già.
- **Il tetto di "segna tutti come letti" con Microsoft** (20.000 messaggi): dirlo quando ne restano? Proposta: sì,
  una riga nell'avviso, quando capita di toccare quel codice.
- **Gli errori in inglese delle azioni sulle cartelle** (Gmail e IMAP): tradurli come quelli del collegamento
  (`ImapProvider.Explain`)? Proposta: sì, insieme alle altre azioni sulle cartelle per Graph (il terzo dell'elenco di
  C-47, non fatto).

### Scelte fatte qui, da rivedere

- **Scelta 33.**
  - Gli elenchi vanno per nome, non per firma: una seconda forma di un metodo già in elenco eredita il suo posto (oggi
    quattro nomi hanno due forme: `SetReadAsync`, `SetFlagAsync`, `MoveAsync`, `GetRecentAsync`).
  - I cinque consensi (`Allow...`) stanno fra le scritture: non toccano la casella, ma aprono una finestra di
    Microsoft o di Google.
  - `SELECT` conta fra i comandi che leggono, anche se toglie `\Recent`.
  - Le scritture che non passano da `IMailProvider` hanno la loro protezione (`MainWindow.Testing`: Annulla iscrizione
    con un clic, la revoca di Google) e questo controllo non le guarda.
  - Sui server finti i metodi si chiamano con argomenti qualsiasi: ogni metodo deve finire senza errori, ma non si
    guarda che cosa restituisce.
- **Scelta 48.**
  - `ForgetFolderKeys` vale per tutti i tipi di account, anche Microsoft, dove la cartella resta in Posta eliminata e
    si può riportare fuori: tornerà senza Preferito e senza colore.
  - Non tocca i colori delle categorie: in Gmail l'etichetta eliminata era anche una categoria, e il suo colore resta
    nelle impostazioni.
  - Nella versione di prova (`TestingProvider`) Elimina cartella va solo nel registro, ma la finestra toglie lo stesso
    Preferito, colore e copie dalla copia dei dati; così faceva già per "nascosta" e "chiusa".
  - Il server finto risponde BAD, non NO, a quello che non fa (eliminare una cartella con sottocartelle, rinominare
    la Posta in arrivo): chi lo prova lo vede come un errore del server, e sa che il server finto va completato.
  - Il controllo di C-43 è stato riscritto, non tolto.
  - Sul server con il punto una cartella portata "al primo livello" esce da INBOX (`Rossi`, non `INBOX.Rossi`): il
    server finto non dichiara uno spazio dei nomi, e un server vero di quel tipo può rifiutarla.
- **Scelta 30.** Solo gli avvisi di nullità, non tutti gli avvisi; solo la configurazione Debug.
- **Scelta 26.**
  - La regola ora è la sola variabile: sono usciti dalla funzione la cartella e gli argomenti, che servivano a
    distinguere la Debug nella sua cartella.
  - La riga va sull'uscita degli errori, dopo aver chiesto a Windows la console di chi l'ha avviata
    (`AttachConsole`). Con un doppio clic non c'è nessuna console e la riga si perde: resta il codice di uscita 2.
    Nessun file di registro: senza la variabile non c'è una cartella di prova dove metterlo.
  - Una variabile fatta di soli spazi vale come data, com'era per `Store.Dir`.

### Non verificato

- **Niente a finestra**: Elimina cartella con il Preferito che sparisce; "Contrassegna tutti come letti" con le righe
  dell'elenco che diventano lette e il numero sull'icona che scende dopo un secondo; rinomina e sposta con la cartella
  che si riapre. I passi sono qui sopra.
- **Nessun server vero.**
  - Scelta 33: Graph, Gmail e un server IMAP veri. Che i server veri non cambino nulla con i comandi di sola lettura
    è quello che dicono i protocolli, non una misura; che cosa scrivano i server finti in più o in meno di quelli
    veri non si vede da qui. Le letture di Google oltre l'elenco dei calendari, dei contatti e la disponibilità.
  - Scelta 48: RENAME e DELETE su Gmail e su un server IMAP veri, in particolare con la cartella ancora aperta sulla
    connessione (vedi "Trovato in più"), con i nomi che hanno lettere accentate, e su un server con lo spazio dei nomi
    `INBOX.`; "segna tutti come letti" su una cartella con migliaia di messaggi; Graph vero.
  - Le azioni sulle cartelle con gli account Microsoft: non sono in questo blocco. Di Graph qui c'è solo "segna tutti
    come letti".
- **Scelta 48**: `MainWindow.DeleteFolderAsync`, `RenameFolderAsync` e `MoveFolderAsync` a finestra (qui il loro
  sorgente e le funzioni che chiamano); la cartella dei dati vera.
- **Scelta 30**: la pubblicazione vera (`dotnet publish`, quella di `installa.ps1` e di `prova.ps1 -Release -Build`)
  con un avviso di nullità: qui `dotnet build` nelle stesse configurazioni e con le stesse proprietà.
- **Scelta 26**: l'eseguibile avviato davvero senza `MISSIVA_DATA`: che si chiuda, che la riga arrivi alla console o a
  chi la raccoglie, il codice di uscita. È letto dal sorgente e provato nella funzione che decide. `AttachConsole` non
  è mai stata eseguita.
- **La Missiva installata e la versione finale di prova** non sono state eseguite; `prova-release` non è stata
  ricostruita. La suite è codice solo di Debug.

## Scelte del 12 ottobre: che cosa è stato fatto e che cosa resta

Chiude i quattro blocchi con cui, fra il 12 e il 13 ottobre 2026, sono state messe in pratica le scelte del punto 3 del
riepilogo dell'audit ("Audit dell'11 ottobre: riepilogo, scelte per l'autore e prove a mano"). L'autore le aveva lette e aveva
risposto "fai tutto ciò che ritieni giusto"; quali fare e quali no l'ha deciso la sessione che coordina il lavoro. I
primi tre blocchi hanno le loro sezioni qui sopra; il quarto (la scelta 29, questo riepilogo e la prova della versione
che si installa) è in questa. Questa sezione raccoglie: dove dice una cosa e una sezione ne dice un'altra, vale la
sezione.

Tutto è sul ramo `scelte` (albero di lavoro `C:\Progetti\Missiva\scelte`), sopra main (`33aed26`, il commit installato):
nulla è unito in main, nulla è installato, nulla è stato inviato a GitHub.

### 1. Le 52 scelte

FATTA vuol dire fatta in questi blocchi, con il suo commit. CHIUSA vuol dire che la proposta era lasciare com'è, e così
resta: nessun lavoro e nessun commit. NON FATTA vuol dire che la sessione che coordina ha deciso di non farla ora, con
il motivo. RESTA APERTA vuol dire che nessuno l'ha decisa: vale la proposta di prima.

| N. | Punto | In breve | Esito |
|---|---|---|---|
| 1 | C-09 | Tempo scaduto di MailKit a 60 secondi per tutti | NON FATTA: va provata prima sulla casella vera, una ricerca lunga di Gmail potrebbe interrompersi (è la prova 33 del riepilogo) |
| 2 | C-61 | Invio interrotto mentre il server riceveva il messaggio | CHIUSA: si ferma e lo dice, com'è ora |
| 3 | C-56 | Promemoria del calendario in ritardo | FATTA (`c5c6f1d`) |
| 4 | T-02 | Immagini negli .eml allegati a un messaggio normale | FATTA (`de86bf2`) |
| 5 | T-10 | Pagine web allegate | FATTA (`9c914a1`) |
| 6 | C-63 | Grigio del testo secondario | NON FATTA: è una scelta d'aspetto in 24 punti, serve l'occhio dell'autore |
| 7 | C-52 | Una firma scelta a mano e poi un altro Da | CHIUSA: com'è ora, resta solo "Nessuna firma" |
| 8 | C-39 | Le due finestre della ricerca riusata | FATTA (`76ad92e`) |
| 9 | T-13 | Una frase in più nella conferma di Annulla iscrizione | CHIUSA: nessuna frase in più |
| 10 | T-06 | Mittente autenticato solo con DMARC superato | FATTA (`b370057`) |
| 11 | T-03 | Account IMAP: riavere dominio autenticato, loghi e clic singolo | NON FATTA: gli account dell'autore risultano Gmail e Outlook |
| 12 | T-08 | Seconda cartella di indesiderata su IMAP | NON FATTA: gli account dell'autore risultano Gmail e Outlook |
| 13 | T-26 | Azioni delle notifiche solo per i messaggi notificati in questa sessione | CHIUSA: valgono per tutte, com'è ora |
| 14 | C-32 | Revoca di Google non riuscita | FATTA (`ed30aea`) |
| 15 | C-51 | Regole di Missiva dopo un errore a metà | FATTA (`575042a`, `7c3f543`) |
| 16 | T-34 | Avviso per le regole di Outlook che inoltrano fuori | NON FATTA: funzione nuova che non si prova senza un account Outlook vero |
| 17 | X-1 | Un messaggio aperto in una finestra sua dopo un ricarico degli account | FATTA (`1261793`, `c466a4f`) |
| 18 | con C-47 | Sottocartelle sui server che separano con il punto | FATTA (`00acbd3`) |
| 19 | T-23 | Il caricamento dei pezzi limitato al loro server | NON FATTA: serve il nome che il registro vero scrive dopo un allegato grande |
| 20 | T-12 | La firma DKIM che copre le due intestazioni di Annulla iscrizione | CHIUSA: nessun lavoro in più |
| 21 | T-17 | Regole di una copia importate spente | CHIUSA: restano accese |
| 22 | T-17 | Rubrica dei destinatari in una copia | FATTA (`facc8b6`) |
| 23 | T-20 | File messi da parte dopo un'importazione | FATTA (`c8057fb`) |
| 24 | T-21 | Modelli che non si decifrano | FATTA (`4f2b7d3`) |
| 25 | T-17 | Voci larghe delle immagini tolte a ogni avvio | NON FATTA: potrebbero essere scelte dell'autore, e i suoi dati non si guardano |
| 26 | T-28 | La Debug avviata senza `MISSIVA_DATA` | FATTA (`b872639`) |
| 27 | C-70 | Windows 11 o anche Windows 10 | CHIUSA: resta "Windows 11" |
| 28 | C-70 | `installa.ps1` e Windows PowerShell 5.1 | CHIUSA: com'è ora finché installa solo l'autore; da fare prima di darlo ad altri |
| 29 | C-67 | `CLAUDE.md` breve | FATTA per il primo passo (`d80977f`, `d1177b1`, `b187bc8`), meno di quanto previsto: vedi sotto. Il secondo passo NON FATTO: dopo aver visto il primo |
| 30 | C-37 | Avvisi di nullità come errori, solo in Debug | FATTA (`35718f1`) |
| 31 | C-69 | Indirizzi di prova su domini che non sono di prova | RESTA APERTA: passarli a `.example` quando si toccano quelle prove |
| 32 | terzo gruppo del codice | I cinque commit che non compilano da soli | CHIUSA: inviati così il 12 ottobre (`origin/main` è `33aed26`) |
| 33 | C-65 | Una prova per `TestingProvider` | FATTA (`0d46423`) |
| 34 | C-01 | Il modello vecchio della riga | NON FATTA: lo usano tre script di `loop\strumenti` e l'autore non ha confermato che il confronto è finito |
| 35 | C-19..C-22 | La divisione dei quattro file grandi | NON FATTA: meglio dopo qualche giorno d'uso della versione installata, così un difetto nuovo si attribuisce bene |
| 36 | C-08, C-11 | La base comune di Gmail e IMAP | FATTA per le due divergenze (`9d4faee`, `87f07b4`). La base comune NON FATTA: come la 35 |
| 37 | C-13 | Gli altri consensi nell'interfaccia | NON FATTA: come la 35 |
| 38 | C-06 | La cifratura delle copie in chiaro a ogni avvio | RESTA APERTA: segnarla fatta con un file; non è urgente |
| 39 | C-03 | Le copie "di prima" tenute per i confronti | CHIUSA: restano, si tolgono quando si tocca uno di quegli algoritmi |
| 40 | C-15 | I nomi delle voci dei menu scritti due volte | RESTA APERTA: sì, 2-3 ore, con le foto dei menu prima e dopo |
| 41 | C-16 | Lo scheletro comune delle finestre | CHIUSA: non ora |
| 42 | C-18 | Nascondi e rimetti la posta | NON FATTA: come la 35, va con le divisioni |
| 43 | C-23 | I metodi lunghi | NON FATTA: come la 35 |
| 44 | C-05 | `MainWindow.QuickReply` | CHIUSA: resta per le tre prove |
| 45 | C-04 | I comandi di prova descritti solo in `CLAUDE.md` | CHIUSA: restano |
| 46 | C-28 | Il lucchetto dei token Microsoft | CHIUSA: uno solo, per ora |
| 47 | C-31 | Il token al collegamento IMAP | CHIUSA: non toccato |
| 48 | C-47 | Copertura che manca: le cartelle e "segna tutti come letti" | FATTA (`ad08a1e`); con quelle prove trovato e corretto un difetto di Elimina cartella (`593eb32`) |
| 49 | C-48 | Tempo della suite | RESTA APERTA: accorciare solo `SuggestIndexAuditTests` |
| 50 | C-46, C-49 | I due file grandi delle prove e gli aiuti comuni | NON FATTA: come la 35 |
| 51 | C-44 | Ganci di prova con `IDisposable` | CHIUSA: no, il riposo fra i gruppi basta |
| 52 | C-66 | Il codice di prova in un progetto a parte | CHIUSA: no |

- FATTE: 18 (3, 4, 5, 8, 10, 14, 15, 17, 18, 22, 23, 24, 26, 29, 30, 33, 36, 48), due solo per la prima parte (29 e 36).
- CHIUSE com'erano: 17 (2, 7, 9, 13, 20, 21, 27, 28, 32, 39, 41, 44, 45, 46, 47, 51, 52).
- NON FATTE: 13 (1, 6, 11, 12, 16, 19, 25, 34, 35, 37, 42, 43, 50), più le seconde parti della 29 e della 36.
- RESTANO APERTE: 4 (31, 38, 40, 49). Con loro quella in attesa da prima: che cosa si vede dopo un errore imprevisto
  dell'interfaccia (C-33).
- Sulla 34: il riepilogo dell'audit e `CLAUDE.md` contano due script che usano il modello vecchio, `vetrina-C.ps1` e
  `altezze-C.ps1`. In `loop\strumenti` sono tre: anche `stati-C.ps1`, che chiama `vetrina-C.ps1 -Vecchia`.
- Commit: 27 sopra main prima di questa sezione. 8 il primo blocco, 10 il secondo, 6 il terzo, 3 il quarto per la scelta
  29.

### 2. Scelte nuove nate da questi blocchi

Quello che i blocchi hanno deciso da soli, o trovato e lasciato. Per ognuna la proposta; fra parentesi da dove viene.
Prima quelle che aspettano una risposta, poi quelle già fatte in un modo, da confermare.

**Lasciate, da decidere**

1. **Gli strumenti da riga di comando della Debug senza `MISSIVA_DATA`** (terzo blocco, con la scelta 26):
   `--imap-finto`, `--page`, `--page-thread`, `--testo`, `--scrivi-inviti` e `--scrivi-pec` escono prima della regola
   che chiude la finestra; `--imap-finto` senza la variabile scrive nel registro vero e cerca `comandi.txt` nella
   cartella dei dati vera. Proposta: devono chiudersi anche loro, tutti tranne la suite, che senza la variabile lavora
   fra i file temporanei: è spostare una riga in `Program.Main` più un controllo. Nessuno script di oggi li avvia senza.
2. **La copia locale non segue una cartella rinominata o spostata** (terzo blocco, misura): le copie di prima restano
   fino a 30 giorni sotto il nome di prima, e i messaggi si riscaricano. Lasciarla, toglierla come ora dopo Elimina, o
   farla seguire? Proposta: toglierla, se cerchi senza rete di rado.
3. **"Segna tutti come letti" con Microsoft si ferma a 20.000 messaggi** (terzo blocco, letto e non provato): oltre, gli
   altri restano da leggere senza che nulla lo dica. Proposta: una riga nell'avviso, quando capita di toccare quel
   codice.
4. **Gli errori delle azioni sulle cartelle, per Gmail e IMAP, sono in inglese** (terzo blocco): un rifiuto del server
   arriva con il testo di MailKit. Proposta: tradurli come quelli del collegamento (`ImapProvider.Explain`), insieme
   alle prove delle azioni sulle cartelle per Graph, il terzo dell'elenco di C-47, non fatto.
5. **Le altre finestre che possono tenere un provider di prima** (secondo blocco, scelta 17): l'anteprima di un allegato
   già aperta, un .eml aperto da un messaggio, la pagina Regole delle Impostazioni non sono state guardate, e dopo un
   ricarico degli account danno ancora l'errore, ora con il testo giusto. Proposta: guardarle e farle passare da
   `ComposeWindow.Current`, al prossimo giro che tocca quelle finestre.
6. **I messaggi delle regole non finiti stanno solo in memoria** (secondo blocco, scelta 15): chiudendo Missiva non si
   riprovano più, e restano nella Posta in arrivo. Proposta: lasciare così; tenerli su disco solo se nel registro vero
   compaiono spesso le righe "non finiti, si riprovano".
7. **I controlli dei gruppi vecchi che leggono i sorgenti non tolgono i commenti** (primo blocco): una chiamata spenta
   con `//` li lascia passare; quanti sono non è stato contato. Proposta: passarli a `Ch1Code`, in un giro di sole
   prove.
8. **I file `importazione-illeggibile-...`** (primo blocco, scelta 23): un'importazione non riuscita li mette da parte
   con dentro il contenuto della copia, cifrato per l'utente di Windows, e le password IMAP se la copia le aveva.
   "Elimina i dati di prima" non li tocca e non hanno un pulsante. Proposta: aggiungerli allo stesso pulsante, con la
   loro riga.
9. **MailKit non manda CLOSE prima di rinominare o eliminare la cartella aperta** (terzo blocco): sul server finto va,
   su un server vero non è provato e qualcuno può rifiutare. Proposta: provarlo una volta con Gmail vero su una cartella
   di prova (è la prova 39 qui sotto); se rifiuta, aprire un'altra cartella prima.
10. **Le azioni sulle cartelle con gli account Microsoft non hanno prove** (terzo blocco): di Graph c'è solo "segna
    tutti come letti". Proposta: farle con la 4.
11. **Le nove voci "Audit" rimaste in `CLAUDE.md`** (quarto blocco, scelta 29): sono elenchi di regole e non sono state
    spostate; con loro il file scende del 14% e non di un terzo. Proposta: lasciarle fino al secondo passo, che le
    distribuisce per area; spostarle ora vorrebbe dire riscriverle.

**Fatte in un modo, da confermare**

12. **La rubrica di una copia importata azzerata più largamente della proposta** (scelta 22, `facc8b6`): a zero anche i
    messaggi ricevuti, e via le date e l'ora dell'ultima lettura della Posta inviata, perché anche con quelli un altro
    poteva dirottare il primo suggerimento. La conferma dell'importazione non dice che i conteggi non entrano. Proposta:
    tenere così, e aggiungere una riga alla conferma.
13. **Una copia con la rubrica rovinata si rifiuta tutta**, invece di entrare senza rubrica (scelta 22). Proposta:
    tenere così.
14. **Immagini bloccate per ogni .eml aperto**, anche `postacert.eml` aperto a mano da una busta di posta certificata
    (scelta 4). Proposta: tenere così.
15. **Pagine web allegate**: sette estensioni (non `.xht` né `.svgz`); la domanda a ogni apertura, anche per gli `.svg`
    che spesso sono loghi; "Apri la pagina" è il pulsante di base (scelta 5). Proposta: tenere così; se la domanda sugli
    `.svg` dà fastidio, toglierla solo a loro.
16. **I domini già segnati senza DMARC si rivedono una volta sola**, con le copie che ci sono sul PC; chi non ha copie
    tiene il segno (scelta 10). Proposta: tenere così.
17. **Il 400 con `invalid_token` alla revoca di Google non dà l'avviso**; l'indirizzo della pagina dei permessi è
    scritto nel testo, senza un pulsante (scelta 14). Non verificato con Google vero. Proposta: tenere così.
18. **"Elimina i dati di prima" cancella per sempre**, non manda nel Cestino (scelta 23). Proposta: tenere così, è lo
    scopo.
19. **I modelli illeggibili spariscono anche da "Rispondi con modello"**, e il pannello ha una nota (scelta 24).
    Proposta: tenere così.
20. **Promemoria in ritardo**: "in ritardo" è oltre due minuti dalla sua ora; una riunione di più giorni non finita fa
    suonare lunedì il promemoria di venerdì; più promemoria in ritardo arrivano uno per notifica, non raggruppati
    (scelta 3). Proposta: tenere così, e raggrupparli solo se il lunedì mattina sono ancora troppi.
21. **Regole dopo un rifiuto**: un messaggio con un'azione non riuscita non viene spostato in quel giro; dopo una copia
    non riuscita finisce spostato senza la copia, e lo dice solo il registro; uno spostamento non riuscito su un server
    senza MOVE non si riprova; il messaggio non finito viene annunciato con la notifica e uno o due controlli dopo
    sparisce dall'elenco; "Esegui ora" premuto di nuovo dopo un errore rifà tutto, copie comprese; nel registro vero si
    legge quante azioni non sono riuscite, non quali (scelta 15). Proposta: tenere così, e guardare il registro dopo
    qualche giorno.
22. **Il testo dell'errore di un provider chiuso è cambiato per tutte le finestre**; una finestra aperta sulla testa di
    una conversazione, dopo il ricarico, può lavorare sul solo messaggio mostrato (scelta 17). Proposta: tenere così.
23. **30 secondi per le due ricerche riusate**: su IMAP la stessa ricerca ripetuta fra 30 e 60 secondi costa una SEARCH
    per cartella in più (scelta 8). Proposta: tenere così.
24. **Il separatore delle cartelle si legge dalle cartelle in elenco**, non si chiede al server (scelta 18). Proposta:
    tenere così.
25. **Gmail ha un comando in più alla prima ripresa di una bozza con allegati** (scelta 36), non misurato su Gmail vero.
    Proposta: tenere così.
26. **Elimina cartella toglie Preferito e colore anche con gli account Microsoft**, dove la cartella resta in Posta
    eliminata e si può riportare fuori: torna senza. Non tocca i colori delle categorie (in Gmail l'etichetta eliminata
    era anche una categoria). Nella versione di prova li toglie lo stesso dalla copia dei dati (`593eb32`). Proposta:
    tenere così.
27. **La prova di `TestingProvider` va per nome, non per firma**: una seconda forma di un metodo già in elenco eredita
    il suo posto; i cinque consensi stanno fra le scritture (scelta 33). Proposta: tenere così.
28. **Solo gli avvisi di nullità sono errori, e solo in Debug** (scelta 30). Proposta: tenere così.
29. **La riga della Debug che si chiude va sull'uscita degli errori**: con un doppio clic non c'è una console e si
    perde, resta il codice di uscita 2 (scelta 26). Proposta: tenere così.
30. **In `CLAUDE.md` le parti spostate hanno al loro posto dei riassunti scritti ora**, 188 righe (scelta 29). Proposta:
    rileggerli quando si tocca quell'area; se un riassunto e il file di storia dicono due cose diverse, il riassunto va
    corretto.

### 3. Cambi di comportamento visibili fatti con queste scelte

Sono nel codice del ramo `scelte`. Ognuno ha il suo commit: se uno non va, si dice di no a quello. Prima quelli che si
notano di più.

1. **I promemoria del calendario delle riunioni già finite non arrivano più** quando riapri il PC o torna la rete;
   quelli delle riunioni non finite arrivano con una riga in più, "Promemoria in ritardo: era alle ..." (scelta 3,
   `c5c6f1d`).
2. **Qualche mittente perde il logo**: quelli il cui dominio non pubblica DMARC (nella copia di prova 4 domini su 114, 2
   loghi su 64). Al primo avvio i domini già segnati si rivedono una volta, in sottofondo. Annulla iscrizione con un
   clic chiede la stessa cosa: nella copia di prova nessun messaggio cambia (scelta 10, `b370057`).
3. **Un messaggio inoltrato come allegato (.eml) si apre senza immagini** anche se il mittente è fra quelli consentiti;
   "Mostra immagini" vale per quella volta e non ricorda nessuno (scelta 4, `de86bf2`).
4. **Aprendo un allegato `.html`, `.htm`, `.shtml`, `.xhtml`, `.svg`, `.mht` o `.mhtml` Missiva chiede prima conferma**,
   con un testo suo e senza il rosso; anche per una pagina dentro un `.p7m` (scelta 5, `9c914a1`).
5. **Dopo Elimina cartella non restano più il Preferito, il colore, il posto nell'ordine e le copie sul PC** di quella
   cartella: una cartella nuova con lo stesso nome non li eredita. Era un difetto, trovato dalle prove della scelta 48
   (`593eb32`).
6. **Regole di Missiva, con Gmail e gli account IMAP**: se il server rifiuta un'azione le altre vanno avanti, e il
   messaggio viene riprovato ai controlli dopo, fino a tre volte; prima restava nella Posta in arrivo per sempre. Il
   messaggio non finito viene annunciato con la notifica e poi può sparire dall'elenco quando la regola riesce. "Esegui
   ora" dice che cosa è riuscito e che cosa no (scelta 15, `575042a`; le righe del registro, `7c3f543`).
7. **Una riga già in elenco diventa letta quando una regola segna letto quel messaggio** (trovato con la scelta 15,
   nello stesso commit `575042a`; letto, non provato a finestra).
8. **Un messaggio aperto in una finestra sua continua a funzionare** mentre aggiungi un account o una casella condivisa,
   o ne rimuovi un altro; prima ogni comando dava un errore. Il testo dell'errore di un provider chiuso ora dice una
   causa sola, per tutte le finestre (scelta 17, `1261793`, `c466a4f`).
9. **Rimuovendo un account Google, se Google non conferma la revoca compare un avviso** con la pagina dove toglierla a
   mano; non quando l'accesso non valeva già più (scelta 14, `ed30aea`).
10. **Dopo l'importazione di una copia di sicurezza i suggerimenti dei destinatari ci sono tutti ma in ordine sparso**,
    finché Missiva non ha riletto la Posta inviata; una copia con la rubrica rovinata viene rifiutata (scelta 22,
    `facc8b6`).
11. **In Impostazioni, Generali, Copia di sicurezza compare "Dati di prima di un'importazione"**, con "Elimina i dati di
    prima…", solo quando quei file ci sono; l'avviso dopo il riavvio di un'importazione dice dove si eliminano (scelta
    23, `c8057fb`).
12. **I modelli che su questo PC non si decifrano non sono più nel pannello Modelli né sotto "Rispondi con modello"**;
    una nota dice quanti sono e che stanno nelle Impostazioni (scelta 24, `4f2b7d3`).
13. **Account IMAP: una ricerca in tutte le cartelle ripetuta dopo più di 30 secondi interroga di nuovo il server**;
    prima dopo più di 60 (scelta 8, `76ad92e`).
14. **Account IMAP che separano le cartelle con il punto: rinominando o spostando una cartella, Preferiti e colori delle
    sottocartelle restano**; una cartella con una barra nel nome non passa più per sottocartella (scelta 18, `00acbd3`).
15. **Gmail, niente che si veda**: gli allegati di un messaggio inoltrato si leggono con la cartella in sola lettura, e
    alla ripresa di un salvataggio la bozza si cerca fra i messaggi non segnati \Deleted (scelta 36, `9d4faee`,
    `87f07b4`).
16. **Per chi sviluppa**: la versione di prova avviata senza `MISSIVA_DATA` si chiude sempre, anche da `bin\Debug`
    (scelta 26, `b872639`); in Debug un avviso di nullità ferma la compilazione, in Release no (scelta 30, `35718f1`);
    nella versione di prova con i dati finti rinominare ed eliminare una cartella ora funzionano (`ad08a1e`);
    `CLAUDE.md` è più corto, e la storia sta in `docs/storia/` (scelta 29, `d80977f`, `d1177b1`, `b187bc8`).

### 4. Prove a mano nuove per l'autore

Si aggiungono alle 34 del punto 5 del riepilogo dell'audit, e continuano la numerazione. Niente di questo è stato
provato a finestra. In ordine di uso; i passi per esteso sono nelle parti "Che cosa vede l'autore, e come provarlo" dei tre
blocchi.

**Subito dopo l'unione in main e l'installazione**

35. **`strumenti\prova.ps1 -Build` una volta**, prima di ogni altra prova con la versione di prova: la Debug già
    compilata in `Missiva-sorgente\bin\Debug` è quella di prima, e avviata senza `MISSIVA_DATA` parte ancora sui dati
    veri (scelta 26).
36. **I loghi al primo avvio**: guarda l'elenco per una decina di secondi. I loghi ci sono come prima, tranne pochi che
    dopo due o tre secondi tornano iniziali; nel registro una riga "loghi: N copie di messaggi rilette ..., M domini
    autenticati senza DMARC superato non lo sono più". Se un mittente che ti scrive spesso ha perso il logo e lo rivuoi,
    dillo (scelta 10).

**Con l'uso normale, nei primi giorni**

37. **Un allegato `.html` e un `.svg`**: mandati un messaggio con `prova.html` e `logo.svg`. Le schede non sono rosse;
    un clic chiede "Aprire una pagina web arrivata per posta?", Annulla non apre nulla, "Apri la pagina" o "Apri il
    disegno" apre il browser; Salva con nome e "Salva tutti" non chiedono nulla; un PDF si apre come prima (scelta 5).
38. **Un messaggio inoltrato come allegato**: da un altro programma inoltra "come allegato" una newsletter di un
    mittente di cui Missiva mostra sempre le immagini, e in Missiva apri l'allegato. Le immagini sono bloccate, "Mostra
    immagini" dice "aperto da un allegato" e vale per quella volta; la stessa newsletter nella Posta in arrivo le mostra
    come prima (scelta 4).
39. **Elimina cartella**: su un account dove puoi farlo crea "Prova di ottobre", mettila fra i Preferiti, dalle un
    colore, spostaci un messaggio e aprilo; poi Elimina. Sparisce dall'albero e dai Preferiti; ricreata con lo stesso
    nome non è fra i Preferiti, non ha colore, è vuota. Con Gmail è anche la prima rinomina ed eliminazione su un server
    vero: prima di eliminarla rinominala, e guarda che non dia errori (`593eb32`, scelta 48, e la scelta nuova 9).
40. **"Contrassegna tutti come letti"** su una cartella con qualche messaggio da leggere: le righe dell'elenco diventano
    lette e il numero sull'icona scende dopo un secondo (scelta 48).
41. **Un promemoria in ritardo**: tre eventi di prova con il promemoria 15 minuti prima (uno fra 20 minuti e lungo
    un'ora, uno fra 20 minuti e lungo 10 minuti, uno fra un'ora e mezza); sospendi il PC prima dei primi due promemoria
    e riaprilo 40 minuti dopo. Arriva una notifica sola, quella del primo, con la riga "Promemoria in ritardo: era alle
    HH:MM"; del secondo nulla; il terzo arriva alla sua ora, com'era (scelta 3).
42. **Un messaggio in una finestra sua mentre gli account si ricaricano**: doppio clic su un messaggio di Gmail con un
    allegato; senza chiudere la finestra aggiungi una casella condivisa o un account; nella finestra Già letto,
    Contrassegna, l'allegato, Rispondi ed Elimina funzionano. Poi, con un account che puoi riaggiungere: apri un suo
    messaggio in una finestra e rimuovi quell'account; il comando dà "... l'account è stato rimosso da Missiva" (scelta
    17).
43. **Le regole di tutti i giorni**: una regola "sposta" su un messaggio nuovo funziona come prima. Dopo qualche giorno
    cerca nel registro "regole di Missiva": le righe con "azioni non riuscite", "non finiti, si riprovano" o "lasciati"
    dicono che un rifiuto c'è stato; "lasciati come sono dopo 3 tentativi" è da guardare (scelta 15).

**Quando capita**

44. **La copia di sicurezza**, insieme alla prova 26 del riepilogo: dopo il riavvio scrivi in un Nuovo messaggio le
    prime lettere di una persona a cui scrivi spesso: c'è, e dopo un minuto con la rete è la prima proposta (scelta 22).
    Poi in Impostazioni, Generali, Copia di sicurezza c'è in fondo "Dati di prima di un'importazione": la domanda ha il
    fuoco su Annulla; con Elimina la parte sparisce e nella cartella dei dati non ci sono più file con
    `prima-dell-importazione` nel nome. Elimina cancella davvero: fallo quando sei sicuro che l'importazione è andata
    bene (scelta 23).
45. **Rimuovi un account Google senza rete**, solo con un account che puoi riaggiungere: dopo la rimozione compare
    "Account rimosso, ma l'accesso di Google può restare", con la pagina dei permessi; con la rete nessun avviso (scelta
    14).
46. **I modelli**: il pannello Modelli e "Rispondi con modello" mostrano i modelli di sempre; sul tuo PC non dovrebbero
    essercene di illeggibili (scelta 24).
47. **Solo se aggiungi un account IMAP che scrive le cartelle con il punto**: metti fra i Preferiti una sottocartella,
    dalle un colore, rinomina la cartella che la contiene: restano (scelta 18).
48. **Rispondi e inoltra con un allegato da Gmail**: è la prova 9 del riepilogo, vale anche per la scelta 36.

**Per chi sviluppa, dopo la prova 35**

49. **La Debug senza la variabile**, da PowerShell, il comando scritto nella parte "Che cosa vede l'autore" del terzo blocco:
    deve dare il codice 2 e la riga "Missiva di prova (DEBUG) non parte senza MISSIVA_DATA...", senza nessuna finestra.
    `prova.ps1` e gli strumenti di `loop\` funzionano come prima (scelta 26).
50. **I dati finti** (`loop\bug-I\prova-finta.ps1`; comandi scritti e mai eseguiti): sul primo account rinomina la
    cartella Clienti, spostala dentro un'altra, eliminala: tutte e tre riescono (scelta 48). Con `-Server 1 -Do "rifiuta
    UID STORE"` e poi `-Do "arrivo Fattura di prova"`, e una regola "segna come letto e sposta" sull'oggetto Fattura: il
    messaggio compare da leggere e al controllo dopo esce dall'elenco, nella cartella della regola (scelta 15).

Non si possono provare a mano: un rifiuto vero del server a una regola (scelta 15); la revoca di Google che fallisce con
la rete accesa (scelta 14); le sottocartelle con il punto senza un account IMAP di quel tipo (scelta 18).

### 5. Cose andate storte, da sapere

- **Un tentativo di collegamento a un server di posta vero**, nel terzo blocco. Per vedere cadere tre controlli sono
  stati chiamati una volta tutti i metodi sui provider senza `TestingProvider`; per l'account Gmail finto (un indirizzo
  `example.com`, un token di prova) l'invio non aveva un server SMTP finto e ha provato a collegarsi al server SMTP di
  Gmail. Il firewall ha fermato il collegamento: nulla è partito, né credenziali né messaggio; può essere partita la
  sola richiesta DNS del nome. Da allora quei controlli hanno un server SMTP finto, e contano che non gli arrivi nulla.
- **Un controllo di prima è caduto una volta**, nel terzo blocco: "T-23, un 307 del server a cui si caricano i pezzi",
  in un giro del solo gruppo, con un errore di rete (10053) fra due ascoltatori di 127.0.0.1. Nei giri dopo non è più
  caduto. Quel codice non è stato toccato: va fra i controlli instabili del punto 6 del riepilogo dell'audit.
- **Una modifica di prova rifiutata**, nel terzo blocco: spegnere a mano una riga di `TestingProvider`, per vedere
  cadere il suo controllo, è stato rifiutato dal controllo dei permessi dello strumento di lavoro. Il controllo è stato
  visto cadere per altre strade.
- **Gli heredoc di Bash** su questo PC hanno tolto barre rovesciate in due script (secondo blocco) e uno non è partito
  (terzo): nessun file rovinato. Gli script con barre rovesciate si scrivono con Write.
- **La scelta 29 ha tolto meno del previsto**: il 14% di `CLAUDE.md`, non un terzo. Vedi qui sotto.

### La scelta 29: `CLAUDE.md` più corto, primo passo (C-67)

Fatto il 13 ottobre 2026. "Prima" è il commit `43e42d9`. Commit: `d80977f` (i conteggi della suite), `d1177b1` (lo
scorrimento), `b187bc8` (cinque voci "Audit"). Solo documenti: nessuna riga di codice.

- **I numeri** (contenuto nel repository, fine riga LF): `CLAUDE.md` da 332.397 byte e 2.834 righe a 285.407 byte e
  2.467 righe (285.059 e 2.464 al commit `b187bc8`; poi tre righe aggiunte dopo la prova della versione pubblicata).
  Sono 46.990 byte in meno, il 14,1%, circa 13 mila token a ogni sessione a 3,5 caratteri per token. In `docs/storia/`
  cinque file, 88.896 byte.
- **Che cosa è uscito**, parola per parola:
  - la storia dei conteggi della suite, da 258 a 1111 controlli: 186 righe della voce "Solo in Debug"
    (`docs/storia/suite-conteggi.md`);
  - le voci "Scatti dello scorrimento" e "Fluidità dello scorrimento, Loop 4": nel file di storia ci sono intere
    (`docs/storia/scorrimento.md`), e da `CLAUDE.md` ne escono 201 righe su 313;
  - le voci "Audit" dei blocchi 6, 7, 8 e 9 del 4 ottobre e del secondo gruppo della parte B del 7 ottobre: 172 righe
    (`docs/storia/audit-4-ottobre.md`, `docs/storia/audit-7-ottobre.md`).
- **Che cosa c'è al loro posto**: per ogni parte, in quale file sta, quando serve leggerlo e quello che serve sempre.
  Sono 188 righe nuove, circa 20 KB. Per i conteggi: il numero di oggi, come si lancia, il gruppo `caduta` solo a mano,
  la rete che torna dal vivo, le due variabili per le finestre. Per lo scorrimento: paragrafi che cominciano con
  "Riassunto" (gli strumenti, quello che da allora è nel codice, le cause già escluse, quello che è stato provato e non
  tenuto). Per le voci "Audit": le loro frasi che sono regole. In cima al file una riga dice che storia e misure stanno
  in `docs/storia/`; `docs/storia/LEGGIMI.md` è l'indice.
- **Che cosa è rimasto dov'era, e perché il file scende del 14% e non di un terzo.** Il compito diceva di spostare
  storia e misure e non regole, di non spostare mai una decisione dell'autore o un divieto, e nel dubbio di lasciare.
  Leggendole:
  - dello scorrimento restano com'erano 112 righe: i paragrafi che dicono come funziona il codice di oggi (`StallLog`,
    la riga disegnata da un solo elemento, `ScrollHitGuard`, il primo gruppo della parte B) e le decisioni dell'autore
    (DirectManipulation provato e tolto, il modello del touchpad scelto alla cieca, `ScrollHitGuard` rimesso dopo la sua
    prova, le righe più leggere), più la riga di comando della versione finale con gli strumenti di prova, che un
    controllo della suite cita ("come dice CLAUDE.md");
  - delle quattordici voci "Audit" ne restano nove: il blocco 10, i tre gruppi di sicurezza e i cinque del codice
    dell'11 ottobre, 29,5 KB. Non sono resoconti: sono elenchi di regole, e quasi ogni frase è un divieto o un "passa di
    lì" ("Mai scrivere `Owner` da un'altra finestra", "Mai `new FontFamily` su un testo scritto da altri", "Chi aggiunge
    un posto dove un'email può scrivere un indirizzo passa da `KindOf`", "DPAPI si chiama solo in `Store.cs`", "La
    riorganizzazione di questo file è una scelta dell'autore"). Spostarle lasciando le regole avrebbe voluto dire riscriverle
    quasi per intero, per 12-15 KB in meno;
  - le cinque voci "Audit" spostate sono quelle che descrivono per esteso una funzione o che cosa un blocco sulla
    velocità ha misurato e lasciato;
  - nel resto del file (206 KB, letto per intero da un agente di sola lettura) non c'è un blocco di sola cronaca che si
    possa spostare a righe intere: le misure stanno nelle stesse frasi che dicono come funziona il codice. L'unico
    candidato, la voce della pillola dei gruppi per data (1,6 KB), ha i valori in uso oggi e un'osservazione dell'autore: è
    rimasta.
- **Niente si è perso**: uno script fuori dal repository (`verifica-spostamento.py`, nello scratchpad del lavoro)
  confronta il `CLAUDE.md` di `43e42d9` con quello nuovo e con i file di `docs/storia/`, riga per riga, spazi compresi.
  Sul commit `b187bc8`: 2.825 righe non vuote di partenza; 2.269 rimaste identiche in `CLAUDE.md`, nello stesso ordine;
  556 spostate, cioè solo nei file di storia; 0 sparite; 117 stanno in tutti e due i posti; 185 nuove (188 con le tre
  aggiunte dopo, e gli altri numeri uguali). Le righe tolte dalle parti spostate sono 559: tre sono uguali a righe dei
  riassunti, e lo script le conta fra le rimaste. Nei file di storia le righe di partenza stanno in otto blocchi di
  righe consecutive; le altre 74 sono titoli, note e l'indice. Lo script è passato a ognuno dei tre commit.
- **Un secondo controllo, sui nomi**: ogni nome fra accenti gravi delle righe nuove c'era già nel file di partenza;
  nessuna variabile `MISSIVA_` e nessun argomento `--...` di Missiva è sparito da `CLAUDE.md`, tranne le sei variabili
  degli interruttori della sonda del cursore, tolti dal codice con C-02, e quattro argomenti di `dotnet-trace`: stanno
  nel file di storia.
- **I rimandi a `CLAUDE.md` per nome di sezione.**
  - `Core/ProvaBozze.Sicurezza3.cs` non legge `CLAUDE.md`: lo cita in un commento per la riga di comando della versione
    finale con gli strumenti di prova. Quella frase è rimasta dov'era.
  - Nel repository (`LEGGIMI.md`, `docs\`, `strumenti\`): i rapporti in `docs/audit/` citano "Scorrimento animato",
    "Prove", "Corpi scaricati in anticipo" e "Senza rete", che sono dove erano. Nessun rimando rotto, nessuno da
    aggiornare. I rapporti riportano anche numeri presi da `CLAUDE.md` al commit che hanno letto (il profilo di una riga
    nuova, il salto del cursore della barra): alcuni ora stanno in `docs/storia/scorrimento.md`.
  - Fuori dal repository, non toccati: `loop\LOOP4.md` cita `CLAUDE.md` per "Dove vanno i 3-4 ms di una riga nuova" e
    per le sezioni "Scatti dello scorrimento" e "Scorrimento animato". "Scorrimento animato" è dov'era; le altre due ora
    in `CLAUDE.md` sono un riassunto che rimanda a `docs/storia/scorrimento.md`: chi cerca quelle parole trova il
    rimando. `loop\strumenti\note-C.ps1` è uno script già usato, che cercava in `CLAUDE.md` di `Missiva-sorgente` una
    frase ora spostata: rilanciato si fermerebbe da solo ("ancora non trovata").
- **Da rivedere**: i riassunti sono scritti ora, da chi ha letto il testo e non ha eseguito nulla; i file di storia sono
  una fotografia del 13 ottobre e non si aggiornano, tranne la storia dei conteggi, che ha una parte per le righe nuove.
  La voce del quinto gruppo del codice in `CLAUDE.md` ora dice che il primo passo è fatto e che il secondo resta una
  scelta dell'autore.

### La versione che si installa, dopo le scelte

Rifatta il 13 ottobre 2026 la prova del quinto gruppo del codice ("La versione che si installa", nel riepilogo
dell'audit): da allora il progetto è cambiato (scelta 30, `35718f1`) e ci sono 27 commit nuovi. Fatta sul commit
`b187bc8`; il codice è quello di `b872639`, dopo il quale cambiano solo i documenti.

- **Come pubblica `installa.ps1`** (letto, mai eseguito): `dotnet publish Missiva.csproj -c Release -o` la cartella di
  installazione `--nologo -v q`, come il 12 ottobre. Pubblicata così in una cartella dello scratchpad, solo per
  guardarla, mai avviata: codice di uscita 0 e nessuna riga in uscita, cioè 0 errori e 0 avvisi (con `-v q` si
  vedrebbero), in 81 secondi; nella cartella un file solo, `Missiva.exe`, 266.820.301 byte (il 12 ottobre 266.586.829).
  Dentro il file il nome di `System.Security.Cryptography.ProtectedData.dll` compare tre volte, come quello di
  `MailKit.dll`; i nomi delle classi di prova (`ProvaBozze`, `TestingProvider`) non compaiono.
- **La versione finale con gli strumenti di prova**, gli stessi argomenti più `-p:MissivaProva=true
  -p:DefineConstants=DEBUG%3BTRACE`, in un'altra cartella dello scratchpad: codice di uscita 0 e nessuna riga in uscita,
  in 83 secondi; un file solo, `Missiva.exe`, 274.930.381 byte (il 12 ottobre 273.717.965). La regola della scelta 30
  non la ferma: è sulla configurazione Debug. La suite dice di sé "DEBUG PROVA" (`Program.Build`).
- **La suite intera in quella versione**, con `MISSIVA_DATA` in una cartella temporanea, due giri: **1071 controlli,
  tutto bene**, tutti e due in 140 secondi.
  - In Debug sono 1111: ne mancano 40. Sono i 26 del 12 ottobre più 14 dei tre blocchi delle scelte, e il motivo è lo
    stesso: leggono i sorgenti o chiedono a MSBuild le proprietà del progetto, cercano `Missiva.csproj` risalendo dalla
    cartella dell'eseguibile, non lo trovano e si saltano dicendolo (undici righe "sorgenti non trovati accanto
    all'eseguibile"). Non dipendono dalla Debug: dipendono da dove sta l'eseguibile.
  - I 26 di prima: la versione del manifesto e lo script di disinstallazione (blocco 5), S-21, S-22, S-27, T-08, T-11,
    T-28 (le costanti chieste a MSBuild), T-29 (due), T-36, C-13, C-25 (due), C-26, C-27, C-33, C-34, C-50, C-52, C-54,
    C-55, C-57, C-59, C-60, X-1.
  - I 14 nuovi. Primo blocco: scelta 4 (le chiamate del riquadro), scelta 5 (chi chiede la conferma delle pagine web),
    scelta 14 (l'ordine in Rimuovi account), scelta 23 (la pagina con "Elimina i dati di prima"), scelta 24 (chi usa
    l'elenco senza gli illeggibili). Secondo: scelta 3 (il giro dei promemoria), scelta 15 (il controllo dei nuovi
    messaggi), scelta 17 (la strada sola), scelta 8 (le due finestre con lo stesso numero). Terzo: scelta 33 (dove
    nascono i provider), scelta 48 (due: Elimina cartella e "Contrassegna tutti come letti" nella finestra), scelta 30
    (gli avvisi chiesti a MSBuild), scelta 26 (l'ordine in `Program.Main`).
  - I nomi dei controlli (i primi 60 caratteri, tolte le cifre) sono stati confrontati con il giro della Debug dello
    stesso codice, fatto subito dopo: mancano quei 40, nessuno è in più; cambia una riga che ha dentro un pezzo casuale.
- Le due cartelle pubblicate e i dati temporanei sono stati cancellati. `prova-release` non è stata toccata. La Missiva
  installata era aperta, e non è stata toccata.
- **Resta non provato**, come il 12 ottobre: l'eseguibile di `installa.ps1` in esecuzione; DPAPI sui dati veri (la prova
  2 del punto 5 del riepilogo: il primo avvio dopo l'installazione); tutto quello che chiede una finestra. I gruppi
  `copia`, `copia2` e `impostazioni` non sono stati lanciati da soli: sono passati dentro i due giri interi.

### L'ultimo giro

- Compilazione da capo (`dotnet build --no-incremental`) sul commit `b187bc8`: Release 0 errori e 0 avvisi, Debug 0
  errori e 0 avvisi (con la regola della scelta 30 accesa).
- Suite completa in Debug subito dopo, con `MISSIVA_DATA` in una cartella temporanea: **1111 controlli, tutto bene, in
  154 secondi**. I nomi sono gli stessi dell'ultimo giro del terzo blocco (1111): nessuno è sparito. Dopo quel giro sono
  cambiati solo i documenti: questa sezione, le due righe del riepilogo in cima a questo file e tre righe di
  `CLAUDE.md`.
- Nessuna caduta, in questo giro e nei due della versione pubblicata, dei controlli dati per instabili.
- Albero di lavoro pulito; 28 commit sopra main; nessun invio a GitHub, nessuna unione in main.

## Dopo l'installazione del 13 ottobre: Missiva non si apriva

Segnalazione dell'autore, 13 ottobre 2026 alle 10:52: "missiva non si apre". Aveva appena lanciato `installa.ps1` dal suo
terminale sul commit `9d738e0` (le scelte del 12 ottobre, unite in main alle 03:34).

### I dati

- L'eseguibile installato era delle 10:51:18, 266.779.341 byte. Da lì ogni avvio si chiudeva subito, senza finestra:
  sei volte fra le 10:51:25 e le 10:52:21.
- Registro eventi di Windows, Applicazione, a ogni avvio: `.NET Runtime` 1025, "The application requested process
  termination through System.Environment.FailFast. Message: MVID mismatch between loaded assembly 'Missiva' (MVID =
  {616eeb64-...}) and an assembly with the same simple name embedded in the native image 'Missiva.r2r.dll' (MVID =
  {f8aa2005-...})", poi `Application Error` 1000 con il codice 0x80131623. L'errore nasce mentre .NET carica Missiva,
  prima di `Program.Main`: nessuna riga nel registro di Missiva.
- Nei file intermedi della compilazione finale (`obj\Release\net10.0-windows10.0.19041.0\win-x64`):
  - `Missiva.dll`, il codice appena compilato: 13 ottobre 10:50:01, MVID `f8aa2005`;
  - `R2R\Missiva.r2r.dll`, il codice precompilato (ReadyToRun) di Missiva e di .NET in un file solo: 13 ottobre
    10:50:59, fatto dal codice nuovo;
  - `R2R\Missiva.dll`, il pezzo di Missiva che finisce dentro l'eseguibile: 12 ottobre 17:52:18, MVID `616eeb64`, cioè
    quello dell'installazione del giorno prima.
  In `R2R` 222 file erano del 12 ottobre alle 17:52, uno delle 10:50 e 20 delle 10:51. L'eseguibile conteneva quindi il
  codice precompilato nuovo e il pezzo di Missiva vecchio, e .NET si rifiuta di partire quando i due non sono della
  stessa compilazione.

### Che cosa è stato fatto subito

Cancellate `obj\Release` e `bin\Release` nella cartella principale e rilanciato `installa.ps1` da questa sessione alle
10:55: finito alle 10:56:54, eseguibile di 266.820.301 byte (la stessa misura della pubblicazione pulita che il quarto
blocco delle scelte aveva provato con la suite), i due MVID uguali (`f8aa2005`), i 243 file di `R2R` tutti delle 10:56,
nessun errore nuovo nel registro eventi. Missiva non l'ho avviata io: che si apra lo dice l'autore.

### La causa prima: non trovata

Perché quella compilazione abbia rifatto il codice precompilato e non i 222 pezzi non lo so. Rifatta qui la stessa cosa
(una riga cambiata in `Program.cs`, poi `dotnet publish -c Release` senza cancellare nulla, nell'albero di lavoro
`scelte`): i 243 file di `R2R` si riscrivono tutti, i due MVID sono uguali e l'eseguibile parte. Le differenze che
conosco fra le due compilazioni: quella dell'autore è partita dal suo terminale, le mie da dentro il pacchetto di Claude; e
nella cartella principale c'erano i file intermedi di compilazioni diverse fatte negli ultimi giorni (versione che si
installa, versione con gli strumenti di prova, compilazioni da capo degli agenti). Nessuna delle due è provata come
causa.

Perché non era stata vista: la "versione che si installa" provata dal quarto blocco delle scelte era pubblicata
dall'albero `scelte`, con i suoi file intermedi; quella della cartella principale, con i file intermedi che c'erano lì,
non l'aveva provata nessuno, e io avevo detto all'autore di reinstallare da lì.

### Corretto (`installa.ps1`, `Program.cs`)

- `installa.ps1` ora compila in una cartella a parte (`bin\installazione`), prova l'eseguibile, e solo dopo chiude
  Missiva e sostituisce i file. Prima svuotava la cartella di installazione e poi compilava: una compilazione non
  riuscita, o riuscita male come questa, lasciava l'autore senza programma. Ora in quei casi la Missiva installata resta
  dov'è, e lo script lo dice. Missiva resta anche aperta mentre si compila (circa un minuto e mezzo).
- I file intermedi del codice precompilato (`obj\Release\...\R2R`) si cancellano prima di ogni compilazione: sono
  quelli rimasti vecchi. Costa una ventina di secondi in più.
- La prova dell'eseguibile è `Missiva.exe --versione`: la prima riga di `Program.Main`, esce subito con il codice 0
  senza finestre, senza registro, senza mutex e senza toccare i dati. Arrivarci vuol dire che .NET ha caricato Missiva;
  un eseguibile come quello delle 10:51 esce con 0x80131623 e lo script si ferma con "L'eseguibile appena compilato non
  parte".
- `installa.ps1 -InCartella percorso` compila e prova allo stesso modo, mette i file in quella cartella e si ferma: non
  tocca la Missiva installata, né il menu Start né il registro. Serve a provare lo script.
- Il controllo della scelta 26 (`Choices3Tests`) ora vuole `--versione` per primo in `Program.Main`, prima di tutto
  ciò che apre i dati.

### Provato

- `installa.ps1 -InCartella` verso una cartella dello scratchpad, dall'albero `scelte`: compila in 80 secondi, la
  prova con `--versione` passa, `Missiva.exe` (266.820.301 byte) e `disinstalla.ps1` arrivano nella cartella, la
  cartella di passaggio non resta, l'eseguibile installato non cambia.
- `--versione` sulla Debug (codice 0, nessun file creato nella cartella dei dati di prova) e sull'eseguibile finale
  (codice 0).
- Suite completa in Debug: 1111 controlli, tutto bene, 180 secondi; dopo l'ultima modifica al controllo i gruppi
  `scelte3`, `sicurezza3` e `sicurezza` da soli, tutto bene.

### Non provato

- Che `--versione` fermi davvero un eseguibile guasto: quello delle 10:51 era già stato sostituito, e qui il guasto non
  si è riprodotto. Il codice 0x80131623 nasce prima di `Main`, quindi quell'eseguibile non può uscire con 0.
- L'installazione vera con lo script nuovo (chiusura di Missiva, spostamento dei file nella cartella di installazione,
  menu Start, registro): la farà l'autore alla prossima installazione.
- La causa prima, come detto sopra.

## Revisione del 13 ottobre: sei consigli sul modulo Core (K-01..K-06)

Sei consigli di un revisore esterno che ha letto i sorgenti di `Core` (senza le prove, senza `UI`) e non ha eseguito
nulla: il suo testo è in `sicurezza-core.md`. Verificati il 13 e il 14 ottobre 2026 sul ramo `auditor`, albero di lavoro
`C:\Progetti\Missiva\auditor`; la cartella `Missiva-sorgente` resta ferma sul commit di main, `e120133`, che è anche il
"prima" di questa sezione.

Commit: `1c6f5c6` (K-01, con il rapporto), `e1c6ef9` (K-02), `941e9e2` e `f014b12` (K-03), `945cb20` (K-04), `5e33b88`
(K-05), `65c396b` (K-06), più quello di questo esito.

Su sei punti: quattro confermati e corretti (K-01, K-03, K-04, K-06), due confermati in parte (K-02 e K-05). In tre la
correzione non è quella proposta, perché quella proposta non chiudeva il difetto o toglieva qualcosa che deve restare
(K-03, K-04, e K-02 dove il rapporto si ferma a due suffissi).

| Punto | In breve | Esito | Commit |
|---|---|---|---|
| K-01 | `SafeFileName`: il taglio a 150 dopo il controllo dei nomi riservati | CONFERMATO, CORRETTO (nessun effetto su questo Windows per i nomi riservati) | `1c6f5c6` |
| K-02 | `gov.it` e `edu.it` contati come un dominio solo | CONFERMATO per i loghi, FALSO per "Mostra immagini" e per i link; trovati in più i nomi geografici, veri per tutti e tre. CORRETTO | `e1c6ef9` |
| K-03 | Identificativi nei percorsi di Graph | CONFERMATO, CORRETTO in un altro modo (la codifica proposta non ferma `..`) | `941e9e2`, `f014b12` |
| K-04 | HTML dell'originale citato com'è nelle risposte e negli inoltri | CONFERMATO, CORRETTO in un altro modo (non con `MailHtml.Prepare`) | `945cb20` |
| K-05 | Schema dei link in `FlowToHtml`; `bcc=` nei link mailto: | (a) CONFERMATO per l'RTF, CORRETTO; (b) FALSO: il Ccn non entra in silenzio | `5e33b88` |
| K-06 | `P7m.Check` guarda solo la prima firma | CONFERMATO, CORRETTO; lo stesso in `Pec.Verify` | `65c396b` |

Come si è provato:

- **Prima**: ogni controllo è stato scritto prima della correzione e fatto girare sul codice di prima. I numeri di "prima"
  qui sotto vengono da quei giri.
- **Dopo**: `CoreReviewTests` (`Core/ProvaBozze.Core.cs`, da soli in tre secondi con `--prova-audit risultato.txt core`),
  26 controlli: K-01 (2), K-02 (7), K-03 (3), K-04 (6), K-05 (4), K-06 (4). Solo dati finti; Graph è il gestore HTTP
  finto; nessuna finestra, nessuna rete.
- **Visti cadere**: 23 dei 26.
  - 17 sul codice di prima: K-01 (2), K-02 (5), K-03 (2), K-04 (3), K-05 (2), K-06 (3). Quelli di K-01 e di K-06 anche
    una seconda volta, con i file di prima rimessi a mano (uno di K-06 era stato ritoccato dopo il primo giro: vedi
    "Trovato in più").
  - 6 con un guasto fatto a mano e poi tolto:
    - K-02, i nomi normali: con `sky` aggiunto all'elenco dei suffissi italiani;
    - K-03, le richieste byte per byte: con `Segment` sostituito dalla proposta del rapporto (`Uri.EscapeDataString` per
      tutti). Cadono tutte e 21;
    - K-04, i messaggi normali: con la pulizia che riscrive sempre l'HTML dall'albero. Cambiano tutti e 14;
    - K-04, lo script di dati (JSON-LD) e "una volta sola per originale": con la pulizia spenta e senza memoria (5 volte su
      5 salvataggi);
    - K-05, il Ccn: con la finestra di scrittura che apre i campi solo per il Cc.
  - Non visti cadere, perché dicono com'era già e la correzione non li tocca: "per Mostra immagini e per i link gov.it
    e edu.it erano già suffissi" (K-02), "all'ingresso lo schema si controllava già" (K-05), "con una firma sola la
    fascia è quella di prima" (K-06).
- **Le finestre non si aprono.** Di K-05 (b) e di K-06 si prova quello che la finestra mostra (`Mailto.Parse`,
  `P7m.Band`) e si legge il sorgente della finestra di scrittura senza i commenti (`Ch1Code`).
- **I dati veri non si guardano**: le impostazioni di prima di K-02 sono scritte e rilette in una cartella di prova.

### Punti del rapporto

- **K-01** (`Safety.SafeFileName`: il taglio dopo il controllo dei nomi riservati). CONFERMATO, CORRETTO (`1c6f5c6`).
  - Il taglio tiene gli ultimi 150 caratteri, per non perdere l'estensione. Veniva dopo il controllo dei nomi riservati,
    e nulla ripuliva quello che lasciava davanti.
  - Prima, dieci nomi costruiti apposta, tutti e dieci sbagliati:
    - dieci `a`, poi `NUL.` e il resto: usciva `NUL.xxx...` (150 caratteri). Lo stesso con `con.`, `COM1 .` e `LPT¹.`;
    - `NUL.` più 146 caratteri, lungo giusto 150: prendeva il `_` davanti, diventava 151, e il taglio toglieva proprio
      il `_`;
    - un nome che dopo il taglio comincia con uno spazio: usciva con lo spazio, e ripassato dalla stessa funzione lo
      perdeva. Il nome mostrato (`CleanAttachmentName`, calcolato alla nascita dell'allegato) e quello del salvataggio
      erano due stringhe diverse, che è quello che S-09 aveva tolto;
    - il taglio in mezzo a un carattere fatto di due metà (un'emoji): restava mezza. Il file del risultato delle prove,
      che è UTF-8, non l'ha accettata e la prova si è chiusa: vedi "Trovato in più".
  - Su 4.000 nomi fatti a pezzi con un generatore sempre uguale, 22 cambiavano al secondo passaggio.
  - Ora: prima il taglio, poi via quello che lascia davanti (spazi di ogni tipo, punti, mezzo carattere), poi il nome
    riservato. Un nome riservato lungo 150 perde il primo carattere per far posto al `_`, non l'ultimo: l'estensione,
    che decide `IsDangerous`, resta. In fondo gli spazi e i punti si tolgono finché ce ne sono (uno spazio unificatore
    seguito da un punto restava, e al secondo passaggio spariva).
  - Dopo: i dieci nomi escono senza dispositivi, senza spazi o punti davanti, con la stessa estensione, e
    `SafeFileName` ripassata sul suo risultato non lo cambia. Dei 4.000, 0 diversi al secondo passaggio; i 3.961 che la
    regola di prima lasciava a posto escono uguali.
  - Non `TrimStart('.', ' ')` per tutti, come si legge nel rapporto: un nome corto che comincia con un punto
    (`.gitignore`) resta com'è, com'era. I punti davanti si tolgono solo dopo un taglio.
  - **L'effetto su questo Windows per i nomi riservati: nessuno**, come per T-09: qui `NUL.txt` dentro una cartella è un
    file normale. Resta vero su un Windows più vecchio, non provato. L'effetto che c'è qui è il nome mostrato diverso
    da quello salvato.
- **K-02** (`gov.it` e `edu.it`). CONFERMATO per i loghi, FALSO per "Mostra immagini" e per i link. Trovati in più i nomi
  geografici. CORRETTO (`e1c6ef9`).
  - **Le regole sono tre, non una**, ognuna con il suo elenco. Il rapporto le dà tutte per rette da
    `SenderLogos.TwoLevel`:
    - `SenderLogos.DomainOf` (`TwoLevel`, 24 suffissi scritti uno per uno): il dominio del logo, del segno
      "autenticato" e della firma che vale per l'annullamento dell'iscrizione con un clic;
    - `Settings.ImageKey` (`SecondLevels`: un secondo livello come `co`, `gov`, `edu` sotto un dominio nazionale di due
      lettere): "Mostra immagini", e il confronto di `SenderCheck.NameAddress`;
    - `Safety.BaseDomain` (un altro `SecondLevels`, più lungo): gli avvisi sui link.
    - `SenderCheck.SameOrganization` non usa nessun elenco: confronta il dominio intero, e due enti sotto `gov.it` per
      lei erano già due.
  - **Loghi, CONFERMATO.** Prima, undici indirizzi di enti finti: `protocollo@ente-uno.gov.it` e `info@ente-due.gov.it`
    davano tutti e due `gov.it`, e così `edu.it`. Dopo un messaggio autenticato del primo ente, il secondo aveva logo e
    segno "autenticato" (`gov.it`). La firma DKIM di una scuola valeva per l'annullamento con un clic di un messaggio
    di un'altra.
  - **"Mostra immagini" e link, FALSO per `gov.it` e `edu.it`.** La regola generale li divideva dal primo giorno
    (`e7f1e25`): "Mostra immagini" su un ente ricordava `ente-uno.gov.it`, e un link che mostra un ente e porta a un
    altro aveva l'avviso. Un controllo lo dice, uguale prima e dopo.
  - **Quindi nessuna versione di Missiva ha mai scritto `gov.it` o `edu.it` nelle impostazioni premendo "Mostra
    immagini" su un ente.** Quella voce può esserci solo se scritta a mano, arrivata con una copia importata prima di
    T-17, o messa premendo "Mostra immagini" su un messaggio che arriva proprio da `x@gov.it`.
  - **I nomi geografici, CONFERMATO per tutte e tre.** Sotto `.it` il registro riserva i nomi delle regioni e delle
    province, con il nome e con la sigla, e lì sotto ognuno registra il suo: `comune.paese.pd.it` e `ditta.pd.it` non
    sono la stessa organizzazione. Prima:
    - "Mostra immagini" su un comune sotto `pd.it`, su un ente sotto `veneto.it` e su uno sotto `padova.it` ricordava
      `pd.it`, `veneto.it`, `padova.it`: le immagini di chiunque scrivesse da lì sotto si caricavano da sole (per tutti
      e tre gli altri mittenti provati sotto quei nomi);
    - il logo e il segno "autenticato" del comune passavano alla ditta;
    - un link che mostra il comune e porta a un altro nome della provincia non aveva l'avviso;
    - il nome del mittente scritto come l'indirizzo del comune, da un altro nome della provincia, non era segnalato.
  - **Corretto.** Un elenco solo, `Safety.IsItalianSuffix`, che le tre regole consultano. 229 nomi: `gov`, `edu`; le 20
    regioni con il nome italiano, più `fvg`, `taa` e `vda` (le sigle che le Regioni usano); le 110 sigle delle province,
    comprese le quattro sarde soppresse; 94 nomi di province, quelli di una parola.
  - **Quelli che mancano.** L'elenco è scritto a memoria, dal regolamento del registro .it e dalla lista pubblica dei
    suffissi, senza poterla leggere (niente rete). Ci sono solo i nomi di cui sono certo. Mancano:
    - i nomi composti delle province, nelle loro varianti con e senza trattino: `reggio-emilia`, `reggio-calabria`,
      `la-spezia`, `massa-carrara`, `pesaro-urbino`, `forli-cesena`, `ascoli-piceno`, `vibo-valentia`, `monza-brianza` e
      `monza`, `laquila` e `aquila`, `verbania`, `barletta-andria-trani`, e quelli delle province sarde soppresse;
    - i nomi in altre lingue (`rome`, `milan`, `venice`, `florence`, `naples`, `turin`, `padua`, `genoa`; i nomi
      tedeschi e ladini di Bolzano e del Trentino);
    - le altre sigle a tre lettere delle regioni (`ven`, `lom`, `tos`, `laz`...), e le varianti dei nomi delle regioni
      (`emiliaromagna`, `trentino`, `alto-adige`, `valledaosta`);
    - `su` (Sud Sardegna): non ricordo se è nella lista.
    Vedi "Scelte per l'autore".
  - **Chi scrive proprio da un suffisso** (`x@gov.it`, `x@pd.it`, `x@co.uk`): "Mostra immagini" ricordava il suffisso,
    per tutti. Ora ricorda quell'indirizzo.
  - **Le impostazioni di chi usa già Missiva.** All'avvio (`UpgradeImageSenders`) escono le voci che sono un suffisso
    italiano intero: `pd.it`, `veneto.it`, `padova.it`, e anche `gov.it` e `edu.it` se ci sono. Non valgono più per
    tutti gli enti lì sotto. **Chi le aveva deve premere di nuovo "Mostra immagini" sul messaggio del suo ente**, e da
    allora vale per quell'ente. Il registro lo dice all'avvio: "immagini: N voci di "Mostra immagini" valevano per un
    intero suffisso italiano (...) e sono uscite".
    - Provato con impostazioni finte scritte e rilette da una cartella: di dieci voci escono le cinque che sono suffissi
      italiani, restano le altre (`ente-uno.gov.it`, `co.uk`, un'azienda, un indirizzo di Gmail, una ditta sotto
      `pd.it`).
    - `co.uk` e un servizio di posta intero restano, come ha deciso la scelta 25 del 12 ottobre: quelle Missiva non le
      ha mai scritte, e possono essere una scelta dell'autore. I nomi geografici sì che li scriveva Missiva, da sola,
      da un clic su un solo ente.
    - Da una copia di sicurezza importata (`DropBroadImageSenders`, T-17) ora non entrano nemmeno queste: prima usciva
      solo `gov.it`.
  - Nomi normali: undici indirizzi (`.it`, `.com`, `.co.uk`, posta personale, nomi che somigliano a un suffisso come
    `governo-prova.it` o `pd.negozio-prova.it`) hanno il dominio e la voce di prima, e i link gli avvisi di prima.
  - Controlli: 7.
- **K-03** (identificativi nei percorsi di Graph). CONFERMATO, CORRETTO in un altro modo (`941e9e2`, `f014b12`).
  - Prima, con il gestore HTTP finto, un identificativo che non è di Graph al posto di quello di un messaggio, di una
    cartella o di un allegato. Su 16 prove, tutte e 16 hanno fatto partire richieste con il token dell'account verso
    un altro percorso:
    - il ritorno di un posposto con `../mailFolders/archive` (la chiave di un posposto sta nelle impostazioni, e una
      copia importata la porta): `PATCH me/mailFolders/archive`, poi `POST me/mailFolders/archive/move` con la Posta in
      arrivo come destinazione. Una cartella spostata;
    - un'eliminazione con lo stesso identificativo: `DELETE me/mailFolders/archive`. Anche con le barre rovesciate,
      che .NET legge come barre;
    - `?` e `#`: il resto del percorso spariva (`POST me/messages/m1` senza `/move`);
    - `..` da solo: `DELETE me/`;
    - una cartella `../../users/altra-casella/mailFolders/inbox`: `GET users/altra-casella/mailFolders/inbox/messages`,
      la posta di un'altra casella letta come quella cartella; un allegato, lo stesso;
    - una cartella `inbox/../..` data a Svuota: la richiesta dell'elenco andava a `me/messages`, cioè a tutta la
      casella.
  - **La proposta del rapporto, `Uri.EscapeDataString` per tutti, provata**: `..` non ha nulla da codificare, e parte
    ancora (`DELETE me/` per un messaggio, un contatto, un evento; `DELETE me/mailFolders/inbox/` per una regola); uno
    vuoto anche (`DELETE me/messages/`). E ogni richiesta vera cambia: gli identificativi di Outlook finiscono con `=`,
    che diventerebbe `%3D`. Che Graph lo accetti per messaggi e cartelle qui non si può provare.
  - **Ora un punto solo**, `GraphProvider.Segment`, con `MessageUrl` e `FolderUrl` che lo chiamano. 61 sostituzioni in
    57 righe; prima gli identificativi stavano dentro le stringhe, uno per uno.
    - Messaggi, cartelle, allegati: un identificativo passa **com'è** se è fatto di ASCII visibile senza `/`, `\`, `?`,
      `#`, `%`, e non è vuoto né di soli punti. Quelli di Outlook (lettere, cifre, `-`, `_`, `=` in fondo) e gli alias
      (`inbox`) passano byte per byte.
    - Gli altri: nessuna richiesta, e chi la chiedeva riceve "non c'è" (`NotFoundException`), come per un 404. Posponi
      dà il messaggio per eliminato, la coda toglie l'azione. Una riga nel registro, con la sola lunghezza.
    - Rifiutare invece di codificare è la forma più prudente: un identificativo con uno di quei caratteri **oggi non
      funzionava già** (il percorso si rompeva), quindi il rifiuto non toglie nulla che andasse. Anche un carattere
      non ASCII si rifiuta: .NET toglie alcuni caratteri invisibili dagli indirizzi, e `.`, un carattere invisibile,
      `.` poteva tornare `..`.
    - Contatti, eventi, calendari e regole si codificavano già (`Uri.EscapeDataString`): restano codificati allo stesso
      modo, perché un identificativo di regola può avere `+` e `/`. In più il rifiuto per quelli vuoti, di soli punti o
      non ASCII. Tre punti che non codificavano (`me/calendars/{id}/calendarView`, il messaggio di un invito,
      l'evento di un invito) ora passano di lì.
    - `users/{indirizzo}/` delle caselle condivise non è toccato.
  - Dopo: 18 identificativi che non sono di Graph, 18 fermati, nessuna richiesta. 21 richieste con identificativi fatti
    come i veri (anche in una casella condivisa) partono agli stessi indirizzi di prima, byte per byte. Un controllo
    legge il sorgente: nessun valore entra in un percorso se non da `Segment`.
  - Controlli: 3.
- **K-04** (HTML citato nelle risposte e negli inoltri). CONFERMATO, CORRETTO in un altro modo (`945cb20`).
  - Con Gmail, IMAP e i file .eml la citazione la fa Missiva (`MimeOptions.BuildAsync`, `EmlProvider.Unlink`); con Graph
    la fa il server (`createReply`, `createForward`). Missiva ci metteva `MessageBody.Html` com'era arrivato.
  - Prima, nel messaggio MIME vero che partirebbe, per una risposta, un inoltro e un .eml inoltrato: `<script>`, il
    modulo con i suoi campi e il pulsante, `<iframe>`, `<object>`, `<embed>`, `onload`, `onclick`, `onerror`, i link
    `javascript:`, `vbscript:` e `data:text/html`, il `meta` che manda a un'altra pagina. Tutti e 17 i segni che
    l'originale di prova aveva.
  - **Non `MailHtml.Prepare`, come propone il rapporto.** Quella è la pulizia del riquadro: blocca le immagini esterne,
    riscrive stili e colori per il tema scuro, mette tutto in un `div` suo. Un inoltro deve arrivare al destinatario
    com'era l'originale.
  - Ora `MailHtml.Outgoing`, una pulizia per l'uscita. Toglie quello che esegue o invia:
    - con il contenuto: `script`, `iframe`, `frame`, `frameset`, `noframes`, `object`, `embed`, `noembed`, `applet`,
      `portal`, e i campi di un modulo (`input`, `select`, `textarea`);
    - lasciando il contenuto: `form` e `button`. Un messaggio scritto tutto dentro un modulo resta leggibile;
    - il `meta` con `http-equiv="refresh"` (gli altri `meta` restano: quello del tipo di contenuto c'è in quasi ogni
      messaggio);
    - gli attributi che cominciano con `on`;
    - negli attributi che portano un indirizzo (`href`, `src`, `action`, `background`, `poster`, `srcset` e altri),
      quelli che non sono http, https, mailto, tel, cid o un'immagine `data:`. Letti come li legge un motore
      (`MailHtml.KindOf`, quello di T-01): `java[tab]script:` è `javascript:`. Un indirizzo senza schema resta;
    - le animazioni dei disegni che cambiano un indirizzo.
  - **Le immagini dell'originale restano**, anche quelle esterne e quelle di un pixel: i "tracker" che il rapporto
    nomina sono immagini dell'originale, e in un inoltro restano con ogni programma di posta. Restano anche stili,
    tabelle, commenti per Outlook, `base`, fogli di stile esterni.
  - **I messaggi normali ripartono uguali, byte per byte.** Se non c'è nulla da togliere `Outgoing` torna la stessa
    stringa. Solo quando c'è qualcosa da togliere l'HTML si riscrive dall'albero. Provato su 14 messaggi di prova: una
    risposta breve, una catena di risposte, una newsletter con tabelle, immagini, pixel, commenti per Outlook e
    `tel:`, un messaggio scritto con Word, solo testo, immagini `cid:` e `data:`, un pixel dentro `noscript`, un testo
    che parla di script, segni di minore, tag non chiusi, maiuscole senza virgolette, link di ogni tipo, un disegno,
    vuoto.
  - **`tel:` resta**, in più rispetto all'elenco del compito (http, https, mailto, cid, immagini `data:`): sta nelle
    firme dei messaggi normali, non esegue e non invia, e toglierlo avrebbe fatto riscrivere la citazione di molti
    messaggi normali. Vedi "Scelte fatte qui".
  - **Un messaggio normale che cambia**: una conferma d'ordine con i dati per Gmail in uno script (JSON-LD). Lo script
    non riparte e la citazione si riscrive dall'albero; testo, tabella, stile, immagine e link restano (un controllo).
  - **I tetti di `ParseGuard` valgono anche qui**: l'analisi è la stessa (`MailHtml.Parse`). Un originale oltre i tetti
    (600 livelli, con uno script in fondo) entra nella citazione come solo testo, come lo mostra il riquadro, in 4 ms
    nella suite intera e in 15-30 con il gruppo da solo. Prima entrava com'era.
  - **Una volta sola per originale** (`MimeOptions.QuoteHtml`, come `QuoteText`): cinque salvataggi della bozza, una
    pulizia. Costo, per una newsletter di 438 KB con 2.500 righe: con il gruppo da solo (a freddo) circa 85 ms al
    primo salvataggio, che in tutto passa da 89 a 184-189 ms; nella suite intera (a caldo) 33-55 ms su 64-66. Ai
    salvataggi successivi 0-4 ms.
  - Controlli: 6.
- **K-05** (`FlowToHtml` e `Mailto`). (a) CONFERMATO per l'RTF, CORRETTO. (b) FALSO. (`5e33b88`)
  - **(a) Da dove entra un link.** Tre strade:
    - Inserisci collegamento (Ctrl+K, `LinkWindow.Parse`): solo http, https, mailto. Controllato all'ingresso;
    - una bozza ripresa (`HtmlToFlow`): solo http, https, mailto. Controllato all'ingresso;
    - un RTF: incollato da un documento scritto da altri, o di una firma o di un modello (anche arrivati con una copia
      di sicurezza). WPF fa un link per ogni campo `HYPERLINK`, con l'indirizzo che ha. **Nessun controllo.**
  - Prima: un RTF con tre campi (`javascript:`, `file://server/...`, `https:`) dava tre link, e `FlowToHtml` li spediva
    tutti e tre. Dodici indirizzi messi in un documento: tutti nell'HTML come link, più uno relativo e uno vuoto.
  - Ora, all'uscita, parte come link solo http, https, mailto e `tel:`. Degli altri resta il testo.
  - **(b) Il Ccn di un link mailto:, FALSO.** `FillFromMailto` mette il Ccn nel campo Ccn e, se il link ha un Cc o un
    Ccn, chiama `OnShowCcBcc`, che rende visibili i campi Cc e Ccn; nessuno li richiude. Chi scrive vede il campo Ccn
    aperto con l'indirizzo. È la seconda delle due forme che il compito accetta ("entra con il campo Ccn aperto e in
    vista"), ed è già così: **niente da cambiare**.
    - Letto dal sorgente, non visto a finestra. Un controllo lo tiene fermo: se un giorno il campo restasse chiuso,
      cade.
    - "Annulla iscrizione" toglie già Cc e Ccn dal mailto: del mittente (`Unsubscribe.CleanMailto`).
    - Il mailto: che Missiva costruisce da sé ("Scrivi all'amministratore") ha solo oggetto e testo: non cambia.
  - Controlli: 4.
- **K-06** (`P7m.Check` con più firme). CONFERMATO, CORRETTO (`65c396b`).
  - L'integrità era già controllata per tutte le firme (`SignedCms.CheckSignature`). Il certificato solo per la prima
    (`SignerInfos[0]`): catena, scadenza, scopo. Il nome mostrato era il suo.
  - **Le buste di prova.** In una busta le firme parallele stanno in un insieme, che la codifica mette in ordine: fra
    due firme con lo stesso emittente decide il numero di serie del certificato. Con certificati di prova dal numero di
    serie scelto (`K6Keys`) si costruiscono i due ordini; la prova li verifica rileggendo il file.
  - Prima, una firma buona (Maria Rossi) e una con il certificato scaduto (Mario Rossi):
    - buona per prima: "Firmato da Maria Rossi il 26/09/2026 alle 10:30: firma valida, certificato riconosciuto da
      Windows." Verde. Di Mario Rossi nessuna traccia;
    - buona per ultima: "Firmato da Mario Rossi ...: il documento è integro, ma il certificato non è fra quelli
      riconosciuti da Windows". Gialla. Di Maria Rossi nessuna traccia.
    Lo stesso con il certificato di un sito web al posto di quello scaduto: verde o rossa secondo l'ordine.
  - Prima, una busta dentro l'altra (fuori Maria Rossi, dentro il certificato di un sito web): lo stato era già il
    peggiore, ma la fascia dava la colpa a chi non l'aveva: "il certificato di Maria Rossi non serve a firmare
    documenti".
  - Ora ogni firma di ogni busta si controlla per sé. Vale la peggiore, alla pari la prima. `Result.Signer` è il suo
    firmatario, `Result.Signers` tutti. La fascia, con più firmatari:
    - "Firmato da Maria Rossi e Mario Rossi: il documento è integro, ma il certificato di Mario Rossi non è fra quelli
      riconosciuti da Windows (...)";
    - "Firma non valida: il certificato di sito-prova.example non serve a firmare documenti (...). Non fidarti del
      contenuto. Le firme sono di Maria Rossi e sito-prova.example.";
    - "Firmato da Maria Rossi e Ada Bianchi: firme valide, certificati riconosciuti da Windows."
    Uguale nei due ordini. Con più firmatari non c'è l'ora: ogni firma ha la sua.
  - Con una firma sola, e con la stessa persona che firma due volte, la fascia è quella di prima, parola per parola.
  - **Un certificato scaduto risulta "non riconosciuto da Windows"** (gialla), non "scaduto": è la frase che c'era.
    Vedi "Scelte per l'autore".
  - **`Pec.Verify`, le firme S/MIME: il caso esiste.** Prendeva la prima firma (`Verify(context).FirstOrDefault()`).
    Con un messaggio firmato due volte (costruito rifacendo la parte della firma con una busta staccata sugli stessi
    byte): firma buona per prima "verificata", per ultima "non riconosciuta". Ora vale la peggiore, con il suo
    firmatario. La busta di un gestore PEC ha una firma sola: lì non cambia nulla.
  - Controlli: 4.

### Trovato in più

- **I nomi geografici di `.it`**: vedi K-02. È la parte più grande di questo lavoro, e il rapporto non la nomina.
- **`MailAutoconfig.BaseDomain` è una quarta regola**, con un elenco suo ancora più corto (`co`, `com`, `net`, `org`).
  Serve a cercare il gestore dal nome del server di posta; un dominio sbagliato lì vuol dire solo non trovarlo. Non
  toccata.
- **Mezzo carattere in un nome chiude la suite.** `File.WriteAllLines` rifiuta una metà di carattere, e il primo giro
  di K-01 è finito con un'eccezione non gestita mentre scriveva il risultato. Corretto nella prova (la metà diventa
  `?` nel testo del controllo). Lo stesso potrebbe succedere a chiunque scriva quel nome in un file UTF-8: ora
  `SafeFileName` non ne lascia.
- **Svuota con un identificativo di cartella costruito apposta** leggeva l'elenco di tutta la casella (K-03): con una
  risposta piena avrebbe eliminato per sempre quei messaggi. Chiuso da `Segment`.
- **`EmlProvider.Unlink` non porta il mittente scelto** (`From`, un alias o "Invia come"), né `Cancel` e `Progress`,
  nel messaggio che passa all'account. Letto, non provato, non toccato: non è di questa revisione.
- **Le API di Google** (calendario, contatti) codificano già gli identificativi nei percorsi. Non guardate oltre: un
  identificativo di soli punti lì non è stato provato.
- **Una cartella dei dati di un giro riuscito è rimasta** (`giro-...` del primo giro intero, "tutto bene"), contro la
  regola di C-49. Non cercato perché; fra un giorno la toglie il primo giro che parte.
- **Un controllo cambia testo con la data**: "V-..., un messaggio nuovo in un gruppo che c'è già" scrive i gruppi
  dell'elenco ("Questo mese", "Settimana scorsa"), e fra il giro del 13 e quello del 14 ottobre il testo è diverso.
- **Due errori nelle prove nuove, corretti nelle prove.**
  - K-04: un originale scritto con i soli a capo di Unix parte con gli a capo di rete (li mette MimeKit scrivendo il
    messaggio, da sempre), e il confronto byte per byte dava "cambiato" anche sul codice di prima. Il messaggio di
    prova ora ha gli a capo di rete.
  - K-06: due certificati di prova con lo stesso emittente e lo stesso numero di serie non si distinguono (una firma
    indica il suo certificato così), e la busta con tre firme risultava "non valida" per questo. Ora ognuno ha il suo
    numero. Il controllo, ritoccato, è stato rivisto cadere con i file di prima.

### Controlli e suite

- `CoreReviewTests`, 26 controlli, gruppo `core` in `Core/ProvaBozze.Gruppi.cs`.
- Nessun controllo di prima è stato toccato, e nessuno è sparito. I nomi del primo e dell'ultimo giro confrontati con
  uno script, tolte le cifre: 26 in più; diversi solo otto che hanno dentro un pezzo casuale o la data.
- Suite completa (`--prova-bozze`), sul codice di `f014b12`: **1137 controlli, tutto bene, in 155 secondi**. Dopo quel
  giro sono cambiati solo i documenti.
- Giri interi prima: sul codice di `65c396b` 1137, tutto bene, in 156 secondi; dopo K-04 1129, in 153; dopo K-03 1123, in
  171; dopo K-02 1120, in 175. Prima di cominciare, sul codice di `e120133`: 1111, tutto bene, in 194 secondi.
- Compilazione da capo (`--no-incremental`) prima dell'ultimo giro: Debug 0 errori e 0 avvisi, Release 0 errori e 0
  avvisi. La versione che si installa non è stata pubblicata né provata.
- `privato\pubblica.ps1 -SoloControlli`: "Controlli passati".

### Che cosa cambia per chi usa Missiva

Niente di questo è stato provato a finestra.

1. **K-01. Quasi nulla.** Un allegato con un nome più lungo di 150 caratteri si salva con il nome che si vede.
2. **K-02. Tre cose.**
   - Per i mittenti che stanno sotto un nome geografico (un comune sotto `pd.it`, un ente sotto `veneto.it` o
     `padova.it`), **se avevi premuto "Mostra immagini" le immagini tornano bloccate**: premi di nuovo sul messaggio di
     quell'ente, e da allora vale per lui soltanto. Il registro dice quante voci sono uscite, al primo avvio.
   - Il logo di un ente sotto `gov.it`, `edu.it` o un nome geografico non è più quello di tutto il suffisso: sparisce,
     e torna (il suo) al primo suo messaggio autenticato.
   - Un link che mostra un ente e porta a un altro sotto lo stesso nome geografico ha l'avviso, e così un nome del
     mittente scritto come l'indirizzo di un altro ente.
3. **K-03. Niente**, se tutto va come previsto. Se un'azione su un account Microsoft dicesse "L'identificativo non è
   di Microsoft Graph", è questo: dimmelo, con la riga del registro.
4. **K-04.** Rispondendo o inoltrando da Gmail o da un account IMAP (o da un .eml) un messaggio con script o moduli, la
   citazione parte senza. Dei messaggi normali non cambia nulla, immagini comprese. La prima volta che si salva la
   bozza di una risposta a una newsletter molto grande costa fino a un decimo di secondo in più.
5. **K-05.** Un link incollato da Word che porta a un file su un server (`file://`), o con un altro schema, parte come
   testo. I link http, https, mailto e `tel:` di firme e modelli partono come prima. Il Ccn di un link mailto: si
   vede nel campo Ccn aperto, com'era.
6. **K-06.** Un file .p7m con più firme nomina tutti i firmatari, ed è verde solo se tutte le firme lo sono.

### Scelte fatte qui, da rivedere

- **K-01.** Dopo un taglio si tolgono anche i punti davanti; senza taglio un punto davanti resta.
- **K-02.**
  - Le tre regole restano tre: non le ho unite, ognuna ha le sue prove. Hanno in comune solo l'elenco italiano.
  - Le voci che sono un suffisso italiano escono a ogni avvio, anche `gov.it` e `edu.it`, che Missiva non ha mai
    scritto da un ente: lo chiedeva il compito. Le altre voci larghe (`co.uk`) restano, per la scelta 25.
  - Nessun avviso a finestra: solo la riga nel registro, e le immagini bloccate con la loro fascia.
  - I loghi salvati sotto i nomi di prima (`gov.it.png`) restano sul disco, inutili.
- **K-03.**
  - Rifiuto invece di codifica per messaggi, cartelle e allegati; un carattere non ASCII è un rifiuto.
  - Un identificativo rifiutato vale come "non c'è" (`NotFoundException`), non come un errore: Posponi e la coda lo
    lasciano andare invece di riprovare.
  - Contatti, eventi, calendari e regole restano codificati: lì `/` diventa `%2F` e la richiesta parte.
- **K-04.**
  - `tel:` resta, in più rispetto al compito; `sms:`, `callto:`, `sip:`, `ftp:` e gli altri si tolgono.
  - Il modulo e il pulsante lasciano il contenuto; i campi si tolgono.
  - `base`, i fogli di stile esterni e l'attributo `ping` restano: non eseguono, e caricano come un'immagine.
  - Gli stili non si toccano (`style`, `<style>`): un `url(javascript:...)` in un foglio di stile resta scritto. I
    motori di oggi non lo eseguono.
  - Oltre i tetti la citazione è solo testo, anche per un messaggio che di pericoloso non ha nulla.
  - La parte di solo testo della citazione (`QuoteText`) non cambia.
- **K-05.** `tel:` parte, anche qui in più rispetto al compito, per le firme. Il Ccn di un mailto: resta com'è.
- **K-06.**
  - Con più firmatari la fascia non dice l'ora.
  - Una busta senza firme dentro una firmata dà "Una delle buste del file non ha una firma" (prima "Il file non ha una
    firma").
  - Per le firme S/MIME, con più firme tutte buone si nomina solo la prima.

### Scelte per l'autore

- **K-02, l'elenco dei suffissi italiani**: confrontarlo con la lista pubblica dei suffissi e aggiungere le varianti
  che mancano? Proposta: sì, alla prima sessione che può leggerla; sono una cinquantina di righe. In alternativa
  portare in Missiva tutta la lista (migliaia di righe, per tutti i paesi) e togliere i tre elenchi: è un lavoro a sé.
  - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".
- **K-02, le voci larghe che restano** (`co.uk`, un servizio di posta intero): toglierle anche loro a ogni avvio? È la
  scelta 25, ancora aperta. Proposta: sì, ora che il registro dice che cosa esce.
  - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".
- **K-02, dirlo a finestra** quando all'avvio escono delle voci? Proposta: no, basta la fascia delle immagini bloccate.
- **K-04 e K-05, `tel:`**: tenerlo, com'è ora, o toglierlo come dice l'elenco di partenza? Proposta: tenerlo.
- **K-05, il Ccn di un link mailto:**: lasciarlo entrare nel campo aperto, com'è, o non farlo entrare affatto?
  Proposta: com'è, come Outlook e Thunderbird.
- **K-06, il certificato scaduto**: una frase sua ("il certificato di ... è scaduto") invece di "non riconosciuto da
  Windows"? Proposta: sì, ma va distinto il certificato scaduto oggi da quello che era valido alla data della firma,
  e per quello serve una marca temporale, che Missiva non legge.
- **K-06, le firme S/MIME**: nominare tutti i firmatari anche lì? Proposta: no, finché non capita un messaggio vero.
- **Le API di Google**: lo stesso punto solo di K-03 per i percorsi di calendario e contatti? Proposta: sì, quando si
  tocca quel codice.

### Non verificato

- **Niente a finestra**: la fascia di un .p7m con più firme, il campo Ccn aperto da un link mailto:, "Mostra immagini"
  da premere di nuovo, un link incollato da Word.
- **Nessun server vero.**
  - K-03: Graph vero. Che i suoi identificativi di messaggi, cartelle e allegati siano sempre ASCII senza `/`, `\`,
    `?`, `#`, `%` è quello che so di come sono fatti, non una misura. Come Graph tratta `%2F` nell'identificativo di un
    contatto, di un evento o di una regola.
  - K-04: come i programmi di chi riceve mostrano una citazione riscritta dall'albero (Outlook, Gmail). Nessun
    messaggio vero: i 14 normali sono scritti a mano.
- **K-01**: un Windows dove i nomi riservati dentro una cartella sono dispositivi.
- **K-02**: la lista pubblica dei suffissi (niente rete). Le impostazioni vere: se dentro ci siano voci che usciranno
  non l'ho guardato; lo dirà il registro al primo avvio. Un sito vero a `gov.it` o a `pd.it`, e che logo desse.
- **K-05**: gli appunti veri (Ctrl+V da Word): qui l'RTF è letto da `RichEditor.BlocksFromRtf`, la stessa funzione di
  firme e modelli.
- **K-06**: un file .p7m vero con più firme, fatto da un programma di firma (con le controfirme, con i certificati
  dell'elenco AgID); una firma con la marca temporale.
- **La Missiva installata e la versione finale** non sono state eseguite né pubblicate. La suite è codice solo di
  Debug.

### Prove a mano per l'autore

Dopo l'unione in main e l'installazione.

1. **K-02.** Al primo avvio cerca nel registro la riga "immagini: ... voci di "Mostra immagini" valevano per un intero
   suffisso italiano". Se c'è, apri un messaggio di uno di quei mittenti (un comune, la Regione): le immagini sono
   bloccate; premi "Mostra immagini"; apri un altro suo messaggio: si vedono. Un messaggio di un altro ente sotto lo
   stesso nome resta bloccato.
2. **K-03.** Con un account Microsoft, l'uso di sempre: apri un messaggio con un allegato e aprilo, sposta, elimina,
   posponi e fai tornare, segna come letto, rinomina una cartella. Tutto come prima. Nel registro non deve comparire
   "non è di Graph".
3. **K-04.** Da un account Gmail inoltra a te stesso una newsletter con immagini e una conferma d'ordine: nel messaggio
   arrivato le immagini ci sono e l'aspetto è quello dell'originale.
4. **K-05.** In una scrittura incolla da Word una riga con un collegamento a un sito e una con un collegamento a un
   file: inviato a te stesso, il primo è un link, il secondo è testo. La tua firma, se ha link o un numero di telefono
   collegato, arriva come prima.
5. **K-06.** Se hai un file .p7m firmato da due persone, aprilo: la fascia nomina tutte e due.

## Revisione del 14 ottobre: cinque consigli sull'interfaccia (U-01..U-05)

Cinque consigli di un revisore esterno che ha letto i sorgenti dell'interfaccia (`Program.cs`, `UI`, i due script) e non
ha eseguito nulla: il suo testo è in `sicurezza-ui.md`. Verificati il 14 ottobre 2026 sul ramo `auditor`, albero di
lavoro `C:\Progetti\Missiva\auditor`; il "prima" di questa sezione è il commit `df0cd97`.

Commit: `0e97cd1` (U-01, con il rapporto), `edecd0a` (U-02), `1a4a134` (U-03), `070812f` (U-04), `0fa90ad` (U-05), più
quello di questo esito.

Su cinque punti uno è un difetto vero, trovato con un'email che lo sfrutta (U-01); gli altri sono indurimento, con dentro
due casi veri piccoli (la pulizia dei temporanei dietro una giunzione in U-03, il nome di Esplora file senza percorso in
U-05). In due la correzione non è quella proposta, perché quella proposta non chiudeva il difetto (U-01) o avrebbe
peggiorato qualcosa (U-03, il nome a 128 bit e la scrittura con file temporaneo e rinomina).

| Punto | In breve | Esito | Chi ci guadagnava | Commit |
|---|---|---|---|---|
| U-01 | Uscire dalla shadow root dell'email | CONFERMATO come difetto, con 20 email che escono. CORRETTO in un altro modo | Chi manda un'email | `0e97cd1` |
| U-02 a | Conferma per gli inoltri verso fuori già presenti o riaccesi | CONFERMATO per la casella dell'elenco e per gli indirizzi nascosti, FALSO per la finestra (non accende). CORRETTO | Quasi nessuno: chi è già entrato nella casella, se poi l'utente accende lui la regola | `edecd0a` |
| U-02 b | "Esegui ora" e il Cestino | GIÀ DETTO in due pezzi; ora in una frase | Nessuno | `edecd0a` |
| U-03 | Nome della cartella temporanea a 128 bit | Indurimento. FATTO con una via di mezzo: 32 cifre dove il percorso lo permette, e la cartella creata solo se non c'è | Nessuno | `1a4a134` |
| U-03 | Collegamenti nella cartella temporanea | FALSO per una giunzione dentro (la pulizia toglieva già solo il collegamento), CONFERMATO se la cartella stessa è una giunzione. CORRETTO | Nessun altro: solo un programma dell'utente può crearla. Il danno però era vero | `1a4a134` |
| U-03 | Salvataggi scelti dall'utente con `SafeFile.Write` | LASCIATO. "Salva tutti" non sovrascriveva già | Nessuno | `1a4a134` |
| U-03 | Il file con il nome finale prima della domanda sul marchio web | CONFERMATO, CORRETTO | Nessuno, in pratica | `1a4a134` |
| U-04 | Codifica in `ReadingPage.Empty` | Indurimento, FATTO come serve a quel posto (CSS, non HTML) | Nessuno | `070812f` |
| U-05 | `explorer.exe` senza percorso | CONFERMATO (basso), CORRETTO | Chi può mettere un file nella cartella dove si salva la copia di sicurezza | `0fa90ad` |
| U-05 | Schema dei link in `AccountWindow.OnLink` | Rischio nullo (due link fissi https). Controllo messo | Nessuno | `0fa90ad` |

Come si è provato:

- **Prima**: ogni controllo è nato prima della correzione, o è stato rivisto cadere con la correzione spenta a mano.
- **Dopo**: `UiReviewTests` (`Core/ProvaBozze.Interfaccia.cs`, da soli in sette secondi con `--prova-audit risultato.txt
  interfaccia`), 31 controlli: U-01 (12), U-02 (6), U-03 (7), U-04 (3), U-05 (3). Solo dati finti; nessuna finestra,
  nessuna rete.
- **Il motore vero** (U-01): il WebView2 senza finestra di T-01 (`Sec2Engine`), con la pagina vera di
  `ReadingPage.Build` e una fascia rossa vera. Per ogni email di prova uno script legge il DOM fuori dalla shadow root
  (deve essere quello della pagina dello stesso messaggio con un corpo innocuo, carattere per carattere), il colore
  calcolato della fascia, e conta in tutta la pagina gli elementi con un attributo di evento, che la pulizia toglie a
  tutti quelli che vede.
- **Visti cadere**: 28 dei 31.
  - Sul codice di prima: i due del motore (20 email escono, 20 portano elementi mai puliti) e, in una prima forma,
    quello della pulizia dietro una giunzione (U-03).
  - Con la correzione spenta a mano e poi rimessa, senza commit:
    - U-01: la guardia tolta (2); il contenuto grezzo rimesso, con la guardia accesa (3: nessuna email esce, ma 20
      portano ancora elementi mai puliti); i commenti rimessi, `template` tolto dagli elementi che non si scrivono,
      l'oggetto scritto senza codifica (4). In tutto 9 dei 12.
    - U-02: la regola di prima (solo gli indirizzi nuovi, nessuna domanda accendendo) e la frase di prima di "Esegui
      ora" (4); la domanda tolta dalle due strade (in più quello che legge i sorgenti); la domanda sempre (in più
      "nessuna domanda quando nessun inoltro diventa attivo"). Tutti e 6.
    - U-03: tutte e sette le correzioni spente in un giro (7).
    - U-04: i nomi dei caratteri non filtrati e un'interpolazione nuova senza codifica (2); una virgola e uno spazio fra
      i nomi (1).
    - U-05: il nome senza percorso, il controllo dello schema tolto, `"dotnet"` senza percorso in una prova (3).
  - Non visti cadere, perché dicono com'è il motore o misurano: "un xmp scritto come pre si vede uguale", "il costo
    della guardia", "il template di una shadow root si scrive in un punto solo", e "nessuna email esce" con la sola
    guardia spenta (resta vero: lo tiene la correzione vera).
- **Le finestre non si aprono.** Le domande di U-02 si provano dalla funzione che ne decide il testo
  (`Rules.ForwardQuestion`, `RulesView.RunQuestion`) e dalla funzione che accende e spegne (`RulesView.EnableAsync`) con
  il gestore HTTP finto di Graph.

### Punti del rapporto

- **U-01** (uscire dalla shadow root dichiarativa). CONFERMATO come difetto, CORRETTO in un altro modo (`0e97cd1`).
  - **Il rapporto è una lettura**: indica i commenti e il ramo di `template`, e dice "se per qualsiasi discrepanza".
    Nessuno dei due è la strada. Su 239 email costruite per uscire, nel motore vero, **20 escono**, e sono tutte della
    stessa famiglia:
    - `<math><mtext><table><mglyph><xmp>...`: l'analisi (AngleSharp, come vuole lo standard) mette `mglyph` fuori dalla
      tabella, dentro `mtext`, e lì è un elemento dell'HTML; riletto dal motore in quel posto è un elemento della
      formula, con tutto quello che contiene. Il contenuto grezzo di `xmp`, che la pulizia aveva scritto com'era,
      diventa elementi: il primo è `</template>`, che chiude la shadow root, e il resto dell'email finisce nella pagina.
    - Lo stesso con `malignmark`, con `mi`, `mo`, `mn`, `ms` al posto di `mtext`, con `noembed` e `noframes` al posto di
      `xmp`, con `mglyph` in una riga, dopo una didascalia, in un `colgroup`, dentro `foreignObject` o dentro un'altra
      tabella (16 email).
    - Con un `svg` sotto `mglyph`, nei due versi (4 email). In un verso come sopra, con `xmp` o `noembed` dentro
      `foreignObject` o `desc`. Nell'altro: `<math><mtext><table><mglyph><svg><mtext><xmp><a title="</xmp>...">`. Qui
      `xmp` è un elemento del disegno e la pulizia vede i suoi figli; riletto, `svg` sotto `mglyph` è della formula,
      `mtext` torna un punto dove comincia l'HTML, `xmp` è quello dell'HTML e si chiude a metà dell'attributo.
  - **Che cosa si vede prima**: fuori dalla shadow root compare l'elemento con il segno e il suo attributo di evento, e la
    fascia rossa vera passa dal suo colore, `rgb(164, 38, 44)`, a quello scritto dall'email, `rgb(1, 2, 3)`.
  - **Chi ci guadagna**: chiunque mandi un'email. Può nascondere la fascia rossa "mittente non verificato", disegnarne
    una verde finta, coprire i pulsanti veri del riquadro con i suoi. Non può eseguire codice (JavaScript è spento e la
    pagina lo vieta), e le sue immagini partono solo se la pagina le lascia già passare per un altro messaggio.
  - **I candidati che non escono, prima** (219): i commenti in tutte le forme (`--!>`, `<!-->`, `<!--->`, annidati,
    mai chiusi, condizionali di Outlook, `<!x`, `<?xml`, `</ `, CDATA nell'HTML: AngleSharp 1.8.2 li chiude come il
    motore); commenti e CDATA dentro `svg` e `math`; `svg style` in cinque forme; `annotation-xml` con una dozzina di
    forme di `encoding`, nei due versi; `foreignObject`, `desc`, `title`, `template`, `script`, `plaintext` dentro
    `svg`; `</p>` e `</br>` dentro `svg` e `math` (AngleSharp esce dal disegno come il motore); `xmp`, `noembed`,
    `noframes`, `listing`, `textarea`, `title`, `noscript`, `iframe`, `script`, `plaintext` da soli, in una tabella,
    dopo un `frameset`; `mglyph` senza tabella, dentro `b`, dentro due moduli, dentro due link; gli attributi (fra virgolette,
    fra apici, senza, con entità, il nome di un attributo con `<`, lo stile, il link, classe e colori del corpo); i
    fogli di stile con `</template` in undici forme; `template` e `</template>` in una tabella, in una cella, in un
    `colgroup`, in un `select`, in un `frameset`, dopo `</body>` e `</html>`; dieci caratteri fra il nome di un elemento
    o di un attributo e quello che segue (avanzamento di pagina, ritorno a capo da solo, carattere nullo...); la strada
    del solo testo, in cinque modi. L'elenco intero, con l'esito di ognuno prima e dopo, lo scrive la prova con
    `MISSIVA_PROVA_USCITE=file`.
  - **Le altre cose che la pagina prende dall'email senza passare dalla pulizia**: oggetto, nome e indirizzo del
    mittente, anteprima di un messaggio chiuso, destinatari, Rispondi a, categorie, nomi degli allegati, righe delle
    fasce, account. Passano tutte dalla codifica (`E`). Con dentro la chiusura dei contenitori e del template la pagina
    ha gli stessi 106 elementi di quella con testi innocui, e il motore legge l'oggetto com'è scritto. Erano già a
    posto.
  - **La correzione proposta non bastava.** Togliere i commenti, togliere il ramo di `template` e mettere la guardia su
    `</template` ferma l'uscita dalla shadow root (provato: con la sola guardia nessuna delle 239 esce), ma le 20 email
    portano ancora dentro la shadow root elementi che la pulizia non ha mai visto: un'immagine mai controllata, un link
    mai controllato.
  - **Corretto alla radice**: nella pagina del riquadro non si scrive più contenuto grezzo.
    - `xmp` dell'HTML si scrive come `pre`, con il testo scritto come testo e i suoi attributi. Per il motore si vede
      uguale: stessi blocco, carattere, dimensione, spazi e margini (misurato).
    - `noembed` e `noframes` non si scrivono: il motore non li mostrava (`display: none`, misurato).
    - Un `xmp` dentro un disegno o una formula non si scrive.
    - In più, in nessuno spazio dei nomi: `template`, `script`, `style`, `title`, `textarea`, `iframe`, `noscript`,
      `plaintext` (`MailHtml.NeverWritten`). Erano già tolti a monte; ora chi scrive la pagina non li scrive comunque.
    Così ogni tag che il motore legge è uno scritto dalla pulizia per un elemento che ha visto, in qualunque contesto lo
    rilegga, e tutto il resto è testo.
  - **I commenti** (punto 2 del compito): nella pagina del riquadro non si scrivono più. `InnerHtml` ha due chiamanti:
    - `MailHtml.Clean`, la pagina del riquadro: senza commenti, senza contenuto grezzo, mai `template`;
    - `MailHtml.Outgoing`, l'HTML che riparte in una risposta o in un inoltro (K-04): com'era. I commenti condizionali
      di Outlook restano tutti (un controllo: l'originale riscritto dall'albero è uguale carattere per carattere), e
      resta il contenuto grezzo di `xmp`.
  - **Il ramo di `IHtmlTemplateElement`** (punto 3): nel riquadro era codice morto, perché `template` è fra gli
    elementi tolti, in ogni spazio dei nomi. Ci si arriva solo da `Outgoing`, quando qualcos'altro fa riscrivere la
    citazione: lì resta, e scrive il contenuto del `template` com'era. Vedi "Trovato in più".
  - **La guardia finale** (punto 4): `MailHtml.Sealed`, dentro `MailHtml.Result`.
    - Sta dove nasce il frammento, non dove la pagina lo scrive: `Result.Fragment` passa dalla guardia nel costruttore
      e non si riassegna. Qualunque strada porti a un `Result` (la pulizia, il solo testo, il ripiego), nella pagina
      non entra un frammento che non ci sia passato; si fa una volta per corpo e non a ogni disegno; e in
      `ReadingPage` il template di una shadow root si scrive in un punto solo, con dentro solo quel frammento (un
      controllo legge il sorgente).
    - `</template`, maiuscolo o minuscolo, diventa `&lt;/template`. Dopo la correzione qui sopra la sequenza può stare
      solo nel valore di un attributo, dove si legge uguale: il motore rilegge lo stesso valore (provato, anche in
      maiuscolo).
    - Un frammento senza la sequenza torna la stessa stringa. Sui 526 testi HTML che le altre prove hanno già, puliti
      per il tema chiaro e per lo scuro (1052 frammenti), la guardia non ne tocca nessuno.
    - Costo: 0,08-0,12 ms su un frammento di 976 KB.
  - **`</style` e la chiusura dei contenitori**: non servono. Il foglio di stile dell'email non ha nessun `</` (diventa
    `\3C/` sull'intero foglio, come ultimo passo), e sta in testa al template, dove il motore lo legge di sicuro come
    foglio di stile. `</div>` e `</article>` dentro un template non chiudono nulla fuori: per il motore il template è
    un confine, e la pulizia scrive solo chiusure di elementi che ha aperto.
  - **Che cosa cambia nella pagina dei messaggi normali**: dei 1046 frammenti dei testi di prova di prima, 1022 sono
    uguali byte per byte; 20 perdono solo i commenti; 4 (lo stesso testo) hanno un `xmp` che diventa `pre`.
  - **Due controlli di prima cambiano**, perché si aspettavano commenti e `xmp` nella pagina:
    - "160 "<" sciolti nel testo: la stessa pagina e lo stesso testo; commenti, attributi e contenuti grezzi non si
      toccano" (blocco 1): stesso nome. Il frammento atteso non ha più il commento e ha `pre` al posto di `xmp`; che
      l'analisi non tocchi commento e contenuto grezzo ora si legge anche dall'HTML in uscita, dove restano.
    - "messaggio con foglio di stile, tabella, link, pixel, script, modulo, commento e pre: pagina chiara e scura, link e
      testo uguali a prima" (blocco 1): il frammento atteso non ha più `<!-- commento -->`, e il nome finisce con "(dal 14
      ottobre, U-01, senza il commento)".
  - **Costo**: la pulizia di un messaggio costruito con 48.000 elementi e 8.000 commenti (1 MB) costa 200-290 ms in
    Debug con le regole nuove e 210-265 senza: dentro il rumore.
  - Controlli: 12.
- **U-02** (conferma sugli inoltri verso fuori; "Esegui ora"). (a) CONFERMATO in parte, CORRETTO. (b) GIÀ DETTO, reso
  esplicito. (`edecd0a`)
  - **Da dove arriva un inoltro già presente**: solo dalle regole di Outlook lette dal server, create altrove. Le regole
    di Missiva (Gmail, IMAP) non inoltrano: la finestra non ha il campo, e i tre elenchi non si salvano nemmeno.
    `Rules.ExternalForwards` conta tutti e tre: inoltra, reindirizza, inoltra come allegato.
  - **La finestra della regola non accende e non spegne**: `RuleWindow` non ha la casella, e `Read` copia `Enabled` com'è.
    Il caso del rapporto, "regola spenta, modificata e salvata", salva una regola che resta spenta: nessun inoltro
    diventa attivo. Per questo lì non si chiede di nuovo.
  - **La casella dell'elenco sì, e non chiedeva nulla** (`RulesView.SetEnabledAsync`): una regola spenta che
    reindirizza fuori si riaccendeva con un clic. È il caso del titolo del rapporto, che le sue correzioni non
    nominano.
  - **Gli indirizzi nascosti**: con un indirizzo nuovo aggiunto nella finestra, la conferma nominava solo quello. Un
    reindirizzamento o un inoltro come allegato già presenti, che la finestra non mostra nei campi, non comparivano.
  - **La regola, ora in un punto solo** (`Rules.ForwardQuestion`, usata dalla finestra e dalla casella): si chiede ogni
    volta che un inoltro verso fuori diventa attivo e prima non lo era, cioè regola nuova, indirizzo esterno nuovo,
    regola spenta che si accende. La domanda nomina **tutti** gli indirizzi esterni della regola. Non si chiede: una
    regola già accesa salvata con gli stessi inoltri; una spenta che resta spenta con gli stessi; spegnere; un inoltro
    dentro l'organizzazione.
  - **Una differenza dalla regola del compito, non meno prudente**: un indirizzo nuovo in una regola spenta si chiede
    lo stesso, com'era prima, dicendo "La regola è spenta: quando la accendi...". Toglierlo sarebbe stato chiedere
    meno di prima.
  - Accendendo: "Con la regola "Archivio" accesa, tutti i messaggi che arrivano verranno inoltrati a ..., fuori dalla tua
    organizzazione, anche quando non guardi. Chi entra di nascosto in una casella lascia proprio regole così: accendila
    solo se l'hai creata tu e ...", con il pulsante "Accendi la regola". Detto di no, la casella torna spenta e a Outlook
    non parte nessuna richiesta (provato con Graph finto: 0 richieste; con il sì, un PATCH con il solo `isEnabled`).
  - Per un account personale il testo dice "fuori dalla tua casella" (prima "organizzazione" per tutti).
  - **Chi ci guadagna**: poco. Serve qualcuno che sia già entrato nella casella e abbia lasciato una regola spenta, e
    poi l'utente che la accende lui, in un elenco che gliela mostra già in rosso con gli indirizzi.
  - **T-34 e la scelta 16 sono un'altra cosa**: lì si tratta di accorgersi che una regola che inoltra fuori è
    comparsa, senza aprire le Impostazioni (leggere le regole in sottofondo e avvisare). Qui si tratta del momento in
    cui l'utente ne rende attiva una da Missiva. La scelta 16 resta com'è: non fatta, dell'autore.
  - **(b) "Esegui ora"**: la conferma diceva già quanti messaggi la regola cambia ("La regola cambia 3 dei 4 messaggi
    di ...") e, nella descrizione sotto, "elimina (nel Cestino)". Con una regola sola che elimina, i messaggi che cambia
    sono quelli che vanno nel Cestino: il numero era già quello. Ora la prima frase lo dice: "La regola mette nel
    Cestino 3 dei 4 messaggi di ...: nel Cestino, non per sempre, da lì si riprendono." Le altre regole hanno la frase
    di prima. Elimina delle regole è sempre uno spostamento nel Cestino (`Rules.RunAsync`), mai un'eliminazione per
    sempre; sul server IMAP finto nel Cestino ne arrivano 3.
  - Controlli: 6.
- **U-03** (allegati nella cartella temporanea, salvataggi). (`1a4a134`)
  - **Chi può scrivere in `%TEMP%\Missiva`**: l'utente e i programmi che girano come lui. Un altro utente del PC no.
    Chi può scriverci può già leggere la posta e sostituire Missiva: per i quattro sotto-punti non c'è un attaccante
    che ci guadagni. Uno di loro era però un danno vero.
  - **La pulizia e i collegamenti**, misurato con giunzioni verso cartelle di prova:
    - una giunzione fra le sottocartelle, o più dentro: FALSO. `Directory.Delete` di .NET toglie il collegamento e non
      tocca dove porta; i file della cartella di arrivo restano. Dopo una giunzione trovata più dentro lascia la
      cartella vuota, che se ne va al giro dopo;
    - **la cartella degli allegati è lei una giunzione**: CONFERMATO. Le sue "sottocartelle" sono quelle della cartella
      di arrivo, e la pulizia le cancellava con tutto quello che contenevano: alla chiusura di Missiva tutte, ogni
      minuto quelle ferme da un'ora. Ora la pulizia non parte, e un allegato non ci si scrive ("è un collegamento a
      un'altra cartella: Missiva non ci scrive");
    - l'età di una sottocartella non conta più i file dietro un collegamento.
    `%TEMP%` e le cartelle sopra non si guardano: possono essere collegamenti per buoni motivi.
  - **La cartella di un allegato, nuova davvero** (`Safety.NewTempFolder`): si crea con una chiamata che non riesce se
    il nome c'è già. `Directory.CreateDirectory` non lo dice, e prima il file finiva nella cartella che c'era. È questa
    la proprietà che conta, più della lunghezza del nome.
  - **Il nome a 128 bit, con una via di mezzo.** Chi conosce la forma del nome: nessuno, la pulizia elenca le
    sottocartelle. Il percorso invece conta: Excel apre fino a 218 caratteri fra cartella e nome (Word e PowerPoint
    259). Su questo PC la cartella degli allegati è lunga 41 caratteri; un allegato dal nome di 150 caratteri sta a
    201 con 8 cifre, com'era, a 209 con 16 e a **225 con 32**: con 32 Excel non lo aprirebbe più. Quindi 32 cifre
    finché il percorso resta entro 218 (qui per i nomi fino a 143 caratteri), poi 16, poi 8 come prima: mai un
    percorso oltre 218 dove prima ci stava.
  - **Il file prende il suo nome solo quando si apre** (`Safety.WriteTemp`): si scrive e si marca come `nome.parte`, e
    si rinomina dopo la domanda. Il marchio web segue il file. Prima il file nasceva con il nome finale, e quando il
    marchio non si scriveva restava lì, senza marchio, per tutto il tempo della domanda "Aprire un file senza il
    controllo di Windows?". Il marchio non si scrive quando la cartella temporanea sta su un disco senza flussi
    alternativi (FAT, exFAT, certe cartelle di rete): qui è NTFS, e si scrive.
  - **I salvataggi scelti dall'utente** ("Salva con nome"): LASCIATO. `SafeFile.Write` è file temporaneo più rinomina:
    su un file che c'è già cambierebbe permessi e flussi alternativi, non funziona dove si può modificare un file ma
    non crearne, e se il nome scelto è un collegamento l'ha scelto l'utente. La finestra di Windows chiede già prima
    di sovrascrivere.
  - **"Salva tutti" non sovrascriveva già**: con un file dello stesso nome prendeva " (2)". FALSO il sospetto. Ora il
    file si crea solo se non esiste, in un passo (`Safety.WriteNewAsync`), e un nome che è di una cartella o di una
    giunzione si salta.
  - Un controllo di prima cambia: "scelta 5 (T-10), Apri chiede la conferma delle pagine web prima di scrivere il file
    e di passarlo a Windows" cerca nel sorgente `Safety.WriteTemp` al posto di `File.WriteAllBytes`. Stesso nome.
  - Controlli: 7.
- **U-04** (`ReadingPage.Empty`). Indurimento, FATTO (`070812f`).
  - **Da dove vengono i tre valori**: dal segnaposto di WPF (`ReadingView.ShowBlank`). Il carattere è
    `FontFamily.Source` del testo, i due colori sono pennelli del tema mescolati al fondo. Nessuno viene da un'email
    né dalle impostazioni: nessuno ci guadagna.
  - **La codifica dell'HTML lì non è la cosa giusta**: il nome del carattere sta dentro `style`, fra apici. `E(...)`
    protegge il bordo dell'attributo ma non il CSS, e avrebbe cambiato la pagina (gli apici diventano `&#39;`). Ora:
    - il valore di `font-family` lo scrive la pagina (`ReadingPage.CssFonts`), e ci entra solo un nome di carattere
      (`HtmlToFlow.FontName`: lettere, cifre, spazi, trattini; mai un indirizzo);
    - i due colori sono `Color`, non testi: nella pagina non entra una stringa fatta altrove.
  - **La pagina è quella di prima**, carattere per carattere: quattro casi, tema chiaro e scuro, con i caratteri e i
    colori di sempre.
  - **"Sono gli unici parametri non passati per E(...)": FALSO alla lettera, vero nella sostanza.** In `ReadingPage.cs`
    le interpolazioni senza `E(...)` sono 89. Lette una per una: numeri, indici, colori calcolati, indirizzi delle
    azioni (origine, gettone esadecimale, percorso fisso), classi e testi fissi, pezzi già codificati, il frammento già
    pulito. Nessuna porta com'è un testo di un'email.
  - Una portava un valore delle impostazioni di Windows: la data della fascia "Non in linea: copia del ...", scritta
    con il separatore di data e ora dell'utente. Ora passa da `E`.
  - Un controllo tiene fermo l'elenco: un'interpolazione nuova senza `E(...)` lo fa cadere con il suo nome, e va letta
    e aggiunta, o codificata. È un inciampo, non una dimostrazione: nomi come `label` o `html` valgono dovunque.
  - Controlli: 3.
- **U-05** (`explorer.exe`, `AccountWindow.OnLink`). (`0fa90ad`)
  - **Com'è Windows, misurato**: un programma avviato con il solo nome si cerca nella cartella di chi lo avvia, poi
    nella **cartella corrente**, e solo dopo in quelle di sistema. Con una copia di `sort.exe` in una cartella di prova
    fatta corrente, `sort.exe` senza percorso avvia quella copia e non quella di System32 (processo creato sospeso e
    chiuso: non esegue nulla). Con la variabile `NoDefaultCurrentDirectoryInExePath` nell'ambiente la cartella
    corrente si salta.
  - **La cartella corrente di Missiva**: dal menu Start è la cartella di installazione (`installa.ps1` la scrive nel
    collegamento). Negli altri avvii (con Windows, da un link mailto:, da una notifica) non la decide Missiva: non
    misurato. Poi può cambiare: le finestre di Windows per scegliere un file o una cartella possono spostarla dove si
    è appena salvato (lo dice la documentazione; non misurato, serve una finestra).
  - **Chi ci guadagna**: nella cartella di installazione scrive solo l'utente. Ma "Apri la cartella" compare proprio
    dopo aver scelto dove salvare la copia di sicurezza: un file chiamato `explorer.exe` in quella cartella (una
    chiavetta, una cartella condivisa) sarebbe partito al posto di Esplora file. Improbabile, non impossibile.
  - Ora `Safety.ShowInFolder`: il percorso intero, dalla cartella di Windows. Un percorso con una virgoletta non si
    passa (Windows non ne ammette).
  - **Tutti gli avvii del programma**, letti uno per uno (12, più `CreateProcessW` in `Sandbox`, che ha già il percorso
    intero di Missiva):

    | Dove | Che cosa | Shell | Da dove viene |
    |---|---|---|---|
    | `Safety.ShowInFolder` (copia di sicurezza) | Esplora file | no | percorso intero; il file l'ha scelto l'utente |
    | `App.OnExit` (riavvio dopo un'importazione) | Missiva | no | `Environment.ProcessPath` |
    | `AccountWindow.OnLink` | pagina web | sì | due link fissi del XAML, ora solo https |
    | `CalendarView.Join` | riunione | sì | solo https; conferma se non è Teams, Zoom, Meet, Webex |
    | `ReadingView.OpenLink` | link di un'email | sì | solo http e https, con l'avviso dei link ingannevoli |
    | `ReadingView` (Runtime di WebView2) | pagina web | sì | indirizzo fisso |
    | `PeopleView.OpenExternal` | tel:, Google Contatti, sito | sì | `ContactLinks.IsAllowed` |
    | `GoogleAuth` | accesso a Google | sì | indirizzo fisso, parametri codificati |
    | `App.OpenDataFolder`, menu del registro | cartella dei dati | sì | `Store.Dir` |
    | `ReadingView`, `P7mWindow` | allegato | sì | file scritto da `Safety.WriteTemp`, dopo le conferme |
  - Nelle prove due avvii usavano `"dotnet"` senza percorso: ora `DotnetExe()`, il percorso intero.
  - **`OnLink`**: ci arrivano solo i due link del XAML, `https://entra.microsoft.com` e
    `https://console.cloud.google.com/auth/clients`. È l'unico gestore di `RequestNavigate`. Rischio nullo; il
    controllo dello schema c'è, come difesa in più.
  - Un controllo legge tutti i sorgenti: nessun avvio con il nome di un programma scritto fra virgolette, prove
    comprese; nel programma ogni avvio dice se passa dalla shell, e quello che avvia è fra quelli letti.
  - Controlli: 3.

### Trovato in più

- **La correzione vera di U-01 non è nel rapporto**: vedi sopra. Senza il motore vero non si trovava.
- **Nell'HTML che riparte, il contenuto di un `template` non si pulisce** (`MailHtml.Outgoing`, K-04): la visita non
  entra nel contenuto, che poi si scrive com'era (un `onclick` lì dentro resta). Lì dentro è inerte per chi riceve.
  Non toccato: vedi "Scelte per l'autore".
- **AngleSharp 1.8.2 e il motore leggono allo stesso modo** tutto il resto provato: commenti, CDATA, punti di
  integrazione, nomi e separatori. Le due analisi divergono solo dove lo standard stesso cambia risposta con il
  contesto.
- **Una prova ha aperto un avviso di Windows sullo schermo**, il 14 ottobre fra le 2:00 e le 2:11. Per misurare dove
  Windows cerca un programma avevo messo nella cartella corrente un file di testo chiamato `hostname.exe`: Windows l'ha
  preso, non è riuscito ad avviarlo, e l'avvio non è più tornato (con ogni probabilità il suo avviso per i programmi
  che non può eseguire). Ho chiuso il mio processo di prova; dopo, nessuna finestra con quel titolo. La misura è
  stata rifatta a mano con un programma vero creato sospeso, e nella suite non c'è: **una prova non avvia mai un
  programma che potrebbe non partire**.
- **La shell da cui girano le prove ha `NoDefaultCurrentDirectoryInExePath=1`**: lì la cartella corrente non si
  guarda. La Missiva avviata dal menu Start non ce l'ha (non è fra le variabili dell'utente né della macchina).
- **Da PowerShell `$null` passato a .NET diventa un testo vuoto**, e da .NET 9 un testo vuoto non toglie una variabile
  d'ambiente: la lascia vuota. La prima misura a mano era sbagliata per questo.

### Controlli e suite

- `UiReviewTests`, 31 controlli, gruppo `interfaccia` in `Core/ProvaBozze.Gruppi.cs`.
- Tre controlli di prima sono stati toccati, detti sopra per nome: due del blocco 1 (U-01), uno delle scelte del 12
  ottobre (U-03). In più, in due prove di prima `"dotnet"` è diventato `DotnetExe()` (U-05): stessi controlli.
- Suite completa (`--prova-bozze`), sul codice di `0fa90ad`: **1167 controlli, tutto bene, in 214 secondi**. Dopo quel
  giro sono cambiati solo i documenti.
- **1167 e non 1168** (1137 più 31). Il giro di prima, sul codice di `df0cd97`, aveva 1137 controlli, tutto bene: non
  l'ho fatto girare io, è il file del risultato lasciato dalla verifica di ieri. I nomi di prima e di dopo confrontati
  con uno script, tolte le cifre: 31 in più; uno con il nome cambiato (quello del blocco 1 detto sopra); dodici diversi
  solo per un pezzo casuale; **uno in meno**: "T-31, nel motore di WPF: un Run con il carattere ..., disposto, fa
  arrivare all'ascoltatore ...". È un controllo che misura solo se Windows prova i percorsi di rete via WebDAV, cioè
  con il servizio WebClient acceso: ieri lo era, oggi è fermo (avvio manuale), e al suo posto il risultato scrive "su
  questo PC la prova non vede (il servizio WebClient è spento?), non misurato". Era così anche nel gruppo `sicurezza3`
  da solo, prima di toccare qualunque cosa lo riguardi. Non l'ho acceso: è un servizio di Windows.
- Compilazione da capo (`--no-incremental`) prima dell'ultimo giro: Debug 0 errori e 0 avvisi, Release 0 errori e 0
  avvisi. La versione che si installa non è stata pubblicata né provata.
- `privato\pubblica.ps1 -SoloControlli`: "Controlli passati".

### Che cosa cambia per chi usa Missiva

Niente di questo è stato provato a finestra.

1. **U-01. Niente che si veda.** Un'email non può più uscire dal suo riquadro. I commenti nascosti delle email non
   stanno più nella pagina; un testo dentro `xmp` si vede uguale.
2. **U-02.** Accendendo dall'elenco una regola di Outlook che manda la posta fuori compare una conferma con tutti gli
   indirizzi. Aggiungendo un indirizzo a una regola che ne ha già di nascosti, la conferma li nomina tutti. "Esegui
   ora" con una regola che elimina dice subito quanti messaggi vanno nel Cestino.
3. **U-03.** Aprendo un allegato con un altro programma non cambia nulla. Se la cartella temporanea di Missiva fosse un
   collegamento a un'altra cartella, l'allegato non si apre e l'avviso lo dice.
4. **U-04. Niente.**
5. **U-05. Niente**: "Apri la cartella" dopo la copia di sicurezza apre Esplora file come prima.

### Scelte fatte qui, da rivedere

- **U-01.**
  - `xmp` diventa `pre`: un foglio di stile dell'email che nomina `xmp` non vale più per lui.
  - `noembed`, `noframes` e un `xmp` dentro un disegno o una formula non si scrivono.
  - La guardia sta nel costruttore di `MailHtml.Result`, non in `ReadingPage`.
  - La guardia scrive `&lt;`: in un attributo è lo stesso carattere; in un foglio di stile non lo sarebbe, ma lì la
    sequenza non può arrivare.
- **U-02.**
  - Un indirizzo nuovo in una regola spenta chiede lo stesso, come prima.
  - Le conferme hanno il pulsante che conferma come predefinito, com'era quella di prima.
  - "fuori dalla tua casella" per gli account personali.
- **U-03.**
  - 218 caratteri come misura: è il limite di Excel, preso da quello che so, non provato qui.
  - Il nome neutro è `nome.parte`.
  - Un collegamento fra le sottocartelle della cartella temporanea si toglie subito, senza guardarne l'età.
  - Prima di rinominare il file si riprova tre volte, a un decimo di secondo, se un altro programma lo tiene aperto.
- **U-04.** Un nome di carattere con altri segni si salta, non si corregge.
- **U-05.** La misura di come Windows cerca un programma non è nella suite.

### Scelte per l'autore

- **U-01, il `template` nell'HTML che riparte**: toglierlo dalla citazione di risposte e inoltri, con il contenuto?
  Proposta: sì, una parola in `NotQuoted`; nessuna email normale ne ha.
  - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".
- **U-02, il pulsante predefinito delle conferme sugli inoltri**: Annulla al posto di "Inoltra a..." e "Accendi la
  regola"? Proposta: sì (`Dialog.AskCancelFirst`, come per Elimina di un contatto): oggi Invio due volte salva e
  conferma.
  - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".
- **U-02, una regola accesa che inoltra fuori a cui si tolgono tutte le condizioni**: oggi chiede "vale per tutti i
  messaggi", senza ricordare l'inoltro. Aggiungerlo a quella domanda? Proposta: sì, una frase.
  - Fatta il 14 ottobre: vedi "Scelte del 14 ottobre".
- **U-03, "Salva con nome"**: resta com'è. Proposta: sì.
- **La scelta 16** (avviso quando compare una regola di Outlook che inoltra fuori, T-34): resta aperta, com'era.

### Non verificato

- **Niente a finestra**: la conferma accendendo una regola e la casella che torna spenta; "Esegui ora" con la frase
  nuova; un allegato aperto con il nome che cambia da `.parte`; "Salva tutti" accanto a un file che c'è; "Apri la
  cartella" dopo la copia di sicurezza; il riquadro vuoto.
- **U-01**: nessuna email vera. I 526 testi sono quelli delle prove. Una newsletter vera con `xmp` non l'ho mai vista.
  Il motore è quello di questo PC (WebView2 154): un motore più nuovo si rilegge con lo stesso gruppo.
- **U-02**: Outlook vero. Le regole di prova sono fatte come le dà Graph, a mano.
- **U-03**:
  - un collegamento simbolico a un file con il nome di un allegato, in "Salva tutti": su questo PC non se ne creano
    senza permessi in più. Provato con una giunzione;
  - una cartella temporanea su un disco senza flussi alternativi, dove il marchio web non si scrive;
  - i 218 caratteri di Excel, e un antivirus che tiene aperto il file mentre cambia nome.
- **U-05**: la cartella corrente di Missiva negli avvii diversi dal menu Start, e dopo una finestra di scelta dei
  file.
- **La Missiva installata e la versione finale** non sono state eseguite né pubblicate.

### Prove a mano per l'autore

Dopo l'unione in main e l'installazione.

1. **U-01.** Apri una decina di messaggi di tutti i giorni, fra cui una newsletter con immagini e una conversazione:
   si vedono come prima.
2. **U-02.** In Impostazioni, Posta, Regole, con un account Outlook: crea una regola che inoltra a un tuo indirizzo
   esterno (chiede, come prima), spegnila, riaccendila dalla casella: compare la conferma con l'indirizzo; con Annulla
   la casella resta spenta. Poi "Esegui ora" con una regola che elimina, su una cartella di prova: la prima frase dice
   quanti messaggi vanno nel Cestino.
3. **U-03.** Apri un allegato di Word e uno di Excel con un altro programma: si aprono, in Visualizzazione protetta.
   "Salva tutti" due volte nella stessa cartella: la seconda volta i file hanno " (2)".
4. **U-05.** Esporta una copia di sicurezza e premi "Apri la cartella": si apre Esplora file con il file scelto.

## Scelte del 14 ottobre: che cosa è stato fatto delle scelte delle due revisioni

Dopo le revisioni del 13 e del 14 ottobre l'autore ha lasciato le loro "Scelte per l'autore" alla sessione che
coordina il lavoro ("fai ciò che ritieni opportuno"): quella ne ha scelte cinque, fatte qui il 14 ottobre 2026 sul ramo
`auditor` (albero di lavoro `C:\Progetti\Missiva\auditor`). "Prima" è il commit `d13c8cb`, uguale a main. Commit, uno
per punto: `9aca877`, `a4cedb9`, `be115ba`, `7be9873`, `c9ea4ee`, più quello di questo esito. Tutte e cinque fatte.

| Punto | Scelta | Commit |
|---|---|---|
| K-02 | L'elenco dei suffissi di .it dalla lista pubblica dei suffissi | `9aca877` |
| Scelta 25 (T-17, K-02) | Le voci larghe di "Mostra immagini" escono a ogni avvio, non solo da una copia importata | `a4cedb9` |
| U-02 | Annulla come pulsante predefinito nelle conferme sugli inoltri | `be115ba` |
| U-02 | Una regola senza condizioni che manda la posta fuori: una domanda sola, che lo dice | `7be9873` |
| U-01 | Niente `template` nella citazione di risposte e inoltri | `c9ea4ee` |

Come si è provato: 8 controlli nuovi nei due gruppi che c'erano, 4 in `core` (`Core/ProvaBozze.Core.cs`) e 4 in
`interfaccia` (`Core/ProvaBozze.Interfaccia.cs`), in fondo, sotto la riga "Scelte del 14 ottobre". Solo dati finti,
nessuna finestra, nessuna rete. **Visti cadere tutti e 8** con la modifica spenta a mano, senza commit (i dettagli
punto per punto).

### I punti

- **K-02, l'elenco dei suffissi di .it** (`9aca877`).
  - La lista pubblica dei suffissi, sezione ICANN, l'ha letta il 14 ottobre la sessione che coordina (qui non c'è
    rete): 407 secondi livelli di .it, tutti `nome.it`. I 229 di prima ci sono tutti; i nuovi sono 178.
  - `Safety.ItalianSecondLevels` ora è quella: `edu` e `gov`; 111 nomi di regioni con le varianti (uniti o con il
    trattino, in altre lingue come `lombardy` e `tuscany`, sigle a tre lettere come `lom` e `ven`); 111 sigle di
    province (in più dei 110 di prima `su`, Sud Sardegna: il dubbio del 13 ottobre è sciolto); 183 nomi di province con
    le varianti (`reggio-emilia`, `la-spezia`, `monza-e-della-brianza`, `bozen`, `florence`, le province sarde
    soppresse...). In ordine alfabetico dentro i gruppi.
  - 20 nomi hanno lettere accentate (`trentino-süd-tirol`, `vallée-aoste`, `südtirol`, `forlì-cesena`...): ci sono
    come nei nomi di dominio, `xn--...`. Riletti con le funzioni di .NET: ognuno torna lo stesso, e il nome che dà è
    quello atteso.
  - I domini della sezione privata (servizi di una ditta, come blogspot.it) non ci sono, apposta: il commento lo dice.
  - Costo: lo stesso `HashSet`, con 178 nomi in più. Non misurato.
  - I controlli di ieri su K-02 passano tutti: nessuno nominava un nome che mancava. Con l'elenco di prima rimesso a mano
    passano anche loro, e cade solo quello nuovo.
  - `Safety.BaseDomain` ora è `internal`: un controllo la chiama da sola, perché per un nome con `xn--` gli avvisi sui
    link si fermano prima (avvisano sempre, "caratteri non latini") e non arrivano a lei.
  - Controlli: 2. Nove nomi nuovi (due composti, due in altre lingue, la sigla di una regione, quella del Sud Sardegna,
    una variante del Trentino, due accentati) danno l'ente per le tre regole (`SenderLogos.DomainOf`,
    `Settings.ImageKey`, `Safety.BaseDomain`), con "Mostra immagini" e un link di prova: caduto con l'elenco di prima
    (tutti e nove davano il suffisso). Sette indirizzi normali sotto .it e nomi che somigliano ai suffissi nuovi
    (`ditta-prova.it`, anche con un sottodominio `bozen.`, `reggio-emilia-prova.it`, `bozen.example`) restano com'erano:
    caduto con `ditta-prova` aggiunto all'elenco per prova.
- **Scelta 25, le voci larghe di "Mostra immagini" a ogni avvio** (`a4cedb9`).
  - Prima all'avvio (`Settings.UpgradeImageSenders`, da `Store.Load`) uscivano solo i suffissi italiani interi; le altre
    voci larghe solo da una copia importata (`DropBroadImageSenders`, T-17).
  - Ora una funzione sola decide che cosa è largo, `Settings.IsBroadImageKey`, e la usano i due posti: un suffisso da
    solo, italiano o di un altro paese (`pd.it`, `gov.it`, `co.uk`, `com.au`), un servizio di posta personale intero
    (`gmail.com`, `libero.it`), un nome senza punto. È la regola che `DropBroadImageSenders` aveva già, uguale.
  - Non è mai larga una voce con la @ (un indirizzo intero, anche `x@co.uk`), né un'organizzazione (`amazon.it`,
    `unibo.it`, `amazon.co.uk`, un comune o una ditta sotto `pd.it`).
  - Una voce che non si decifra resta com'è, e il suo cifrato torna nel file. L'eccezione è scritta apposta, anche se
    oggi non servirebbe: il segnaposto ("(testo cifrato non leggibile su questo PC, rif. ...)") ha un punto, e due
    pezzi che non sono un suffisso. Provato: con il segnaposto senza il punto e senza l'eccezione cadono il controllo
    nuovo e due di S-26; con l'eccezione passano.
  - La riga del registro c'era, allargata: "immagini: 6 voci di "Mostra immagini" (co.uk, gmail.com, libero.it, com.au,
    pd.it, gov.it) valevano per chiunque scriva da un intero servizio di posta o da un intero suffisso e sono uscite;
    per quei mittenti le immagini restano bloccate finché non si preme di nuovo "Mostra immagini"" (al singolare con
    una voce sola; `Store.BroadImagesLine`). Nomina solo servizi e suffissi, mai indirizzi.
  - Da dove possono venire voci così: `gmail.com` e gli altri servizi Missiva non li ha mai scritti (l'elenco dei
    servizi di posta personale è lo stesso dal 26 settembre); `co.uk` sì, premendo "Mostra immagini" su un messaggio di
    `x@co.uk` prima di K-02; e poi a mano, o da una copia importata prima di T-17.
  - Un controllo di prima cambia: "K-02, impostazioni di prima: le voci che sono un suffisso italiano intero ...
    escono all'avvio": `co.uk` ora esce anche lui; il nome lo dice.
  - Controlli: 2. Impostazioni finte con 14 voci scritte e rilette da una cartella di prova, come all'avvio: escono le 6
    larghe, restano le altre 8, la riga è quella, all'avvio dopo non esce nulla, "Mostra immagini" premuto di nuovo su
    un mittente di Gmail ricorda il suo indirizzo. E la stessa regola dall'avvio e da una copia importata, con l'elenco
    che non si decifra. Caduti tutti e due con la regola di prima all'avvio (con loro il controllo di K-02 cambiato).
- **U-02, Annulla nelle conferme sugli inoltri** (`be115ba`).
  - `Dialog.AskCancelFirst` (lo usavano Elimina di un contatto di Google e una domanda della copia di sicurezza): Annulla
    è il pulsante di Invio e quello di Esc, e ha il fuoco quando la finestra si apre; il pulsante che conferma non è
    predefinito. Per confermare si sceglie il pulsante (con il mouse, o Tab e poi Invio o Spazio).
  - Solo le due conferme di `Rules.ForwardQuestion`: nella finestra della regola (`RuleWindow.SaveAsync`) e nella
    casella dell'elenco che accende una regola (`RulesView.SetEnabledAsync`). Le altre domande delle regole ("vale per
    tutti i messaggi", Elimina, Esegui ora) restano com'erano.
  - Nessun controllo leggeva quale pulsante è predefinito, e senza aprire le finestre non c'è un modo migliore che
    leggere i sorgenti senza commenti (`Ch1Code`): `Dialog.Build` (Annulla con `IsCancel` e `IsDefault`, il fuoco, il
    pulsante principale non predefinito) e le due chiamate.
  - Controlli: 1. Caduto con la finestra rimessa a `Dialog.Ask`, e di nuovo con la casella rimessa a `Dialog.Ask`.
- **U-02, l'inoltro ricordato quando la regola vale per tutti i messaggi** (`7be9873`).
  - Prima la finestra chiedeva "La regola vale per tutti i messaggi" e poi, se un inoltro verso fuori diventava attivo,
    anche la domanda sugli inoltri: due domande di fila per la stessa regola. E una regola accesa che inoltrava già
    fuori, a cui si toglievano le condizioni, aveva solo la prima, senza l'inoltro.
  - Ora al massimo una domanda. La forma scelta:
    - se c'è da chiedere per gli inoltri (`Rules.ForwardQuestion`), solo quella, e senza condizioni dice tutte e due
      le cose. Titolo "La regola vale per tutti i messaggi e li manda fuori"; testo "Non hai scelto condizioni: la regola
      agirà su ogni messaggio che arriva nella Posta in arrivo, e tutti verranno inoltrati a ..., fuori dalla tua
      organizzazione, anche quando non guardi. Chi entra di nascosto ...", con Annulla predefinito. Spenta: "La regola è
      spenta: quando la accendi, agirà su ogni messaggio che arriva nella Posta in arrivo (non hai scelto condizioni), e
      tutti verranno inoltrati a ...";
    - altrimenti, senza condizioni, "La regola vale per tutti i messaggi" (`Rules.AllMessagesQuestion`), che se la regola
      manda la posta fuori lo dice: "Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta
      in arrivo, e lo manderà a ..., fuori dalla tua organizzazione. Salvarla lo stesso?" ("dalla tua casella" per un
      account personale). Senza inoltri verso fuori il testo è quello di prima, parola per parola, e "Salva" resta il
      pulsante predefinito, com'era.
  - La domanda sugli inoltri ora viene per prima; la casella dell'elenco (accendere) non cambia.
  - Un controllo di prima cambia: "U-02, un indirizzo esterno nuovo in una regola che ne ha già di nascosti ...": la
    regola dell'account personale non ha condizioni, e la domanda ora comincia con "Non hai scelto condizioni"; il nome
    lo dice.
  - Controlli: 2. I testi (accesa con gli inoltri di prima, inoltro nuovo accesa e spenta, account personale, senza
    inoltri, inoltro dentro l'organizzazione, con condizioni anche nascoste): caduto con la domanda sulle condizioni
    senza gli inoltri, e con la domanda sugli inoltri senza la parte sulle condizioni. L'ordine nella finestra, letto
    dal sorgente: caduto con la finestra che fa tutte e due le domande di fila.
- **U-01, il `template` nella citazione** (`c9ea4ee`).
  - `template` è fra gli elementi che `MailHtml.Outgoing` toglie con il contenuto (`NotQuoted`). Prima, sul codice di
    `7be9873`: un originale con un template che conteneva uno script, un `onclick` e un'immagine con `onerror` ripartiva
    com'era (fuori non c'era nulla da togliere, e la visita non entra nel contenuto di un template); con un attributo di
    evento fuori, si riscriveva dall'albero con il template com'era. Ora non riparte nulla di tutto quello, nel corpo, in
    testa e in una tabella.
  - **Il ramo di `IHtmlTemplateElement` in `InnerHtml` è tolto**, perché nessuna strada ci arriva più. `InnerHtml` ha
    due chiamanti. `Clean` (la pagina del riquadro) salta `template` prima di quel ramo (`NeverWritten`). `Outgoing`
    visita tutti gli elementi che `InnerHtml` può scrivere (gli stessi figli dallo stesso documento, compresi quelli di
    un modulo o di un pulsante di cui resta il contenuto), e ogni elemento che si chiama `template`, in qualunque spazio
    dei nomi, va fra quelli tolti. Un template dentro un altro sta nel contenuto del primo, a cui si arrivava solo da
    quel ramo. Se un giorno uno ci arrivasse, si scriverebbe vuoto.
  - I messaggi normali della serie di K-04 ripartono uguali byte per byte: il controllo di K-04 (14 messaggi) passa.
  - Un controllo di prima cambia: "U-01, template: nella pagina del riquadro non si scrive mai ...; nell'HTML che
    riparte resta com'era": ora si aspetta che nell'HTML che riparte non ci sia; il nome lo dice.
  - Controlli: 1. Caduto sul codice di prima, e di nuovo con la sola parola tolta da `NotQuoted` (con lui il controllo
    di U-01 cambiato).

### Controlli e suite

- 8 controlli nuovi: `core` da 26 a 30, `interfaccia` da 31 a 35. Tre controlli di prima cambiati, detti sopra.
- Suite completa (`--prova-bozze`) sul codice di `c9ea4ee`: **1176 controlli, tutto bene, in 226 secondi**. Dopo quel
  giro sono cambiati solo i documenti.
- Prima, sul codice di `d13c8cb`, all'inizio di questo lavoro: 1168, tutto bene, in 167 secondi. 1168 e non 1167:
  questa volta il servizio WebClient di Windows era acceso (non l'ho toccato), e il controllo di T-31 ha misurato e
  contato, nei due giri.
- I nomi dei due giri confrontati con uno script, tolte le cifre: 8 nuovi; 3 con il nome cambiato (quelli detti
  sopra); 10 diversi solo per un pezzo casuale o per un numero scritto con il separatore; nessuno sparito.
- Compilazione da capo (`--no-incremental`) prima dell'ultimo giro: Release 0 errori e 0 avvisi, Debug 0 errori e 0
  avvisi. La versione che si installa non è stata pubblicata né provata.

### Che cosa vede chi usa Missiva

Niente di questo è stato provato a finestra.

1. **K-02.** Per chi scrive da sotto uno dei nomi che mancavano (`bozen.it`, `suedtirol.it`, `reggio-emilia.it`,
   `la-spezia.it`, `florence.it`, `lom.it`...) logo, "Mostra immagini" e avvisi sui link valgono per il suo ente, come
   già per `pd.it` o `veneto.it`.
2. **Scelta 25.** Se nelle impostazioni c'è una voce come `gmail.com` o `co.uk` (o, dopo il punto 1, `bozen.it`), al
   primo avvio esce e il registro lo dice: per quei mittenti le immagini tornano bloccate finché non si preme di nuovo
   "Mostra immagini", che da allora ricorda l'indirizzo o l'ente. Se non ce n'è, nulla cambia.
3. **U-02.** Nelle conferme di una regola che manda la posta fuori Invio annulla: per confermare si preme il pulsante
   "Inoltra a ..." o "Accendi la regola".
4. **U-02.** Salvando una regola senza condizioni che manda la posta fuori arriva una domanda sola, che dice tutte e due
   le cose.
5. **U-01. Niente che si veda.**

### Scelte fatte qui, da rivedere

- **K-02.** Solo i nomi accentati in `xn--`, non scritti con le lettere accentate (vedi "Non verificato"). I loghi
  salvati sotto un suffisso di prima (`bozen.it.png`) restano sul disco, inutili, come quelli di K-02.
- **Scelta 25.** Larga è anche una voce come `aruba.it` o `tim.it` (il primo pezzo è un servizio di posta personale):
  "Mostra immagini" per loro ricorda l'indirizzo dal 26 settembre, quindi una voce così può essere solo scritta a mano o
  importata. Esce, come già da una copia importata.
- **U-02, la forma della domanda sola**: quella sugli inoltri prima e, se c'è, da sola; "vale per tutti i messaggi"
  tiene "Salva" come pulsante predefinito anche quando nomina gli inoltri (Annulla solo nelle due conferme sugli
  inoltri, come chiedeva la scelta).
- **U-01.** Il template si toglie in ogni spazio dei nomi, anche un `template` dentro un disegno, che per l'HTML non è un
  template.

### Non verificato

- **Niente a finestra**: le due conferme con Invio ed Esc (il comportamento è letto dal codice di `Dialog.Build`, non
  premuto), la domanda sola nella finestra della regola, la riga del registro al primo avvio.
- **La lista pubblica dei suffissi**: qui non c'è rete, l'ha letta la sessione che coordina; il file è usato com'era.
- **Un indirizzo con il dominio scritto con le lettere accentate** (`x@comune.südtirol.it`, non in `xn--`):
  `Settings.ImageKey` gli dà il suffisso (`südtirol.it`), `SenderLogos.DomainOf` nessun dominio, com'era. Come Gmail,
  IMAP (MimeKit) e Graph danno un indirizzo così non l'ho guardato.
- **Le impostazioni vere**: se dentro ci siano voci che usciranno non l'ho guardato; lo dirà il registro al primo avvio.
- **La Missiva installata e la versione finale** non sono state eseguite né pubblicate.

### Prove a mano per l'autore

Dopo l'unione in main e l'installazione.

1. **Scelta 25 e K-02.** Al primo avvio cerca nel registro la riga che comincia con "immagini:" (la prova 1 della
   revisione del 13 ottobre cercava "valevano per un intero suffisso italiano": ora la riga è questa, per tutte le voci
   larghe). Se c'è, apri un messaggio di uno di quei mittenti: le immagini sono bloccate; premi "Mostra immagini" e
   riapri: si vedono. Se non c'è, non è uscito nulla.
2. **U-02.** In Impostazioni, Posta, Regole, con un account Outlook: crea una regola senza condizioni che inoltra a un
   tuo indirizzo esterno. Compare una domanda sola, "La regola vale per tutti i messaggi e li manda fuori": premi Invio
   e la regola non si salva; premi "Inoltra a ..." e si salva. Spegnila e riaccendila dalla casella: Invio lascia la
   casella spenta. Poi a una regola accesa che inoltra fuori con una condizione togli la condizione e salva: la domanda
   "La regola vale per tutti i messaggi" nomina l'indirizzo. Alla fine elimina la regola di prova.
3. **U-01.** Da un account Gmail rispondi o inoltra a te stesso una newsletter normale: il messaggio arrivato è come
   prima.

## Audit di affidabilità del 25 ottobre 2026

La richiesta dell'autore: "ogni tanto sembra che le mail arrivano tardi, che non si aggiorni in tempo, che i messaggi letti
o non letti ogni tanto si perdano per strada". Tre rapporti, ognuno con l'esito delle sue correzioni in fondo, nella
sezione "Esito delle correzioni":

- `docs/audit/affidabilita-arrivo.md` (A-01..A-10): l'arrivo dei messaggi nuovi. Blocco 1 delle correzioni, risveglio e
  connessioni (ramo `correzioni-arrivo`): Missiva si accorge della ripresa del PC e dei cambi di rete, rifà le
  connessioni, controlla tutti gli account insieme, e il controllo di riserva è ogni minuto per Gmail e IMAP. Gruppo
  `affidabilita-arrivo`, 35 controlli.
- `docs/audit/affidabilita-letti.md` (L-01..L-10): letti e non letti. Blocco 2 (ramo `correzioni-letti`): un segno non
  riuscito resta in attesa e si ripete, la riapertura dall'area di notifica non segna letto, un segno nuovo vince su uno
  vecchio in attesa, i cambi fatti sul telefono non si ignorano più. Gruppo `affidabilita-letti`, 23 controlli. L-10 è
  passato al blocco 3.
- `docs/audit/affidabilita-elenco.md` (E-01..E-15): l'elenco, le cartelle e i numeri. Blocco 3 (ramo `correzioni-elenco`):
  l'elenco si rilegge dopo un errore o un risveglio e se resta vecchio lo dice, i numeri delle altre cartelle si
  aggiornano con il controllo di riserva, due correzioni piccole su Outlook.com, e i punti minori. Gruppo
  `affidabilita-elenco`, 30 controlli.

La suite: 1335 controlli prima dell'audit (22 ottobre), 1393 all'unione dei blocchi 1 e 2 (main `5d53818`, "tutto bene"),
1423 con il blocco 3 (26 ottobre 2026, ramo `correzioni-elenco` al commit `e089cf7`, "tutto bene", in 286 s). Le scelte lasciate
all'autore e quello che non si è potuto provare sono nelle sezioni di esito dei tre rapporti.
