# Audit di velocità di Missiva

- Commit letto: `59ed8bafa7b59942f04bef8c084bbb5d3455bca9` (ramo main, "Dati di prova senza indirizzi e nomi veri; CLAUDE.md: sezione per le sessioni online").
- Data: 3 ottobre 2026. Sessione online (Claude Code nel cloud), sola lettura: nessun file del codice è stato modificato.
- Ambiente delle misure (parte A): Linux, Intel Xeon a 2,10 GHz (macchina virtuale KVM), 4 core logici, 15 GB di RAM, .NET 10.0.12 (SDK 10.0.112), GC workstation concorrente, cultura it-IT dove conta. Missiva non si esegue qui (WPF, WebView2 e DPAPI non esistono su Linux): le misure riguardano solo copie delle funzioni pure di `Core`, compilate in un banco fuori dal repository (codice in appendice).

## Come leggere questo rapporto

- Ogni punto dice se è **MISURATO** (eseguito davvero qui, con i numeri), **CONFERMATO** (letto nel codice, con file e riga) o **SOSPETTO** (e cosa manca per confermarlo). I millisecondi dei punti CONFERMATO e SOSPETTO sono stime fatte leggendo il codice, e lo dicono.
- I numeri misurati qui sono di Linux su una macchina virtuale: non si confrontano con quelli di CLAUDE.md (PC dell'autore, Windows 11, 120 Hz). Valgono per i confronti relativi (prima e dopo una correzione, chiaro e scuro) e per l'andamento al crescere dei dati.
- La parte B è stata letta in parallelo da sei agenti, ognuno su un gruppo di punti (5 e 11 e 17; 6, 12 e 13; 7 e 8; 9 e 14; 10; 15 e 16). Ho ricontrollato nel codice le righe dei punti con gravità Alta e Media; le altre sono riportate come le hanno lette gli agenti.
- Già deciso o già provato, e quindi non riproposto: DirectManipulation, la riga dell'elenco disegnata da un solo elemento (qui si guardano solo i suoi costi interni), CacheLength 0, ScrollHitGuard, il modello 3 del touchpad, file unico senza compressione e con ReadyToRun, il servizio di rete di WebView2 in un processo suo, il controllo di sicurezza ogni due minuti, l'alone delle schede, il riscaldamento dei caratteri, BackForwardCache, GCConserveMemory, GC non concorrente, TieredPGO.

## Sintesi

Da fare per primi:

1. **V-01** Un'email di 50-70 KB con qualche migliaio di tag non chiusi chiude Missiva: overflow dello stack in `MailHtml.ToText` (Rispondi, Inoltra, regole sul testo, salvataggio delle risposte) e nel testo dei link di `Prepare` (all'apertura). MISURATO: con uno stack da 1 MB overflow a 8.000 livelli. La correzione è piccola (testo raccolto con una pila) e dà un'uscita identica su tutti gli ingressi del banco.
2. **V-04, V-02, V-03** La pulizia dell'HTML ha costi quadratici che email di 30-140 KB fanno esplodere: regex su stili e attributi (1,3-6,8 s), i "<" che non aprono un tag in AngleSharp (1,8 s e 9,5 GB per 100 KB), l'annidamento profondo nel tema scuro (38,5 s). MISURATO, e misurate anche le correzioni: da secondi a millisecondi per le regex e per i "<" (100.000 "<": da 1,8 s a 5-8 ms), da 38,5 a 5,5 s per l'annidamento nel tema scuro (il resto è l'analisi di AngleSharp, che toglie solo un limite di profondità), con la stessa uscita.
3. **V-05** Segna letto o da leggere su 500 messaggi ridisegna icona, area di notifica e registro 500 volte sul thread dell'interfaccia (CONFERMATO; stima 0,5-3 s). Anche ogni messaggio non letto aperto fa lo stesso lavoro prima che la pagina sia servita.
4. **V-19** I corpi scaricati in anticipo si puliscono solo al clic, e la pulizia vale per un'istanza: freccia giù su un messaggio già in memoria aspetta comunque `MailHtml.Prepare` (MISURATO qui 7-12 ms per una newsletter da 200 KB, 41-61 per 1 MB), e il sole, Mostra immagini e il cambio di tema la rifanno sul thread dell'interfaccia (CONFERMATO).
5. **V-06** Account IMAP: ogni salvataggio automatico di una bozza con allegati li riscarica e li ricarica tutti (CONFERMATO).

Poi i tempi di tutti i giorni: codice dei provider che riprende sul thread dell'interfaccia (V-17), elenco rifatto a ogni messaggio nuovo (V-12), suggerimenti dei destinatari che rifanno tutta la rubrica a ogni lettera (V-15, MISURATO: 10-15 ms e 18 MB per lettera con 5.000 voci e 4.500 contatti), `Store.Save` sincrono e completo (V-10), Graph senza limite di richieste per casella, compressione e HTTP/2 (V-24, V-23), notifica che aspetta il conteggio dei non letti (V-26), Invio che rifà la ricerca dei suggerimenti (V-27). Nel tema scuro ogni parola che non è un colore è una FormatException (V-07, MISURATO: da 146 a 24.000 per email; togliendole un foglio di 2.000 regole passa da 84,6 a 14,9 ms, le newsletter normali cambiano poco).

## Parte A: misure

### A.0 Metodo

- Banco: progetto console `net10.0` in una cartella temporanea, compilato in Release. Compila copie invariate di `Core/MailHtml.cs`, `Core/Invitation.cs`, `Core/SearchTerms.cs`, `Core/Mailto.cs`, `Core/ContactLinks.cs`, `Core/SenderCheck.cs`, della parte vCard e CSV di `Core/Contacts.cs` (con la classe `Contact`), di `Address` e `MessageSummary` da `Core/Models.cs`, di `Settings.ImageKey` da `Core/Store.cs` e, per la A.5, di `Entry`, `Wanted`, `Suggest` e `Rank` da `Core/AddressBook.cs`. Finti: `Log` (conta le righe), `MailAccount`, `MessageBody`, `OutgoingMessage`, `MailException`, `AccountTypes` (copiati `DomainOf` e `LooksValid`), e `System.Windows.Media.Color` e `ColorConverter` (vedi l'appendice). Pacchetti come Missiva: AngleSharp 1.8.2, MailKit 4.18.0.
- Ingressi generati dal banco, con soli nomi e indirizzi finti e domini `.example`: una newsletter come quelle vere (tabella da 600 pixel, un foglio di stile con 40 regole e una media query, per ogni articolo immagine esterna, titolo, paragrafo, pulsante colorato, `<font color>`, pixel di tracciamento) ripetuta fino a 50 KB, 200 KB, 1 MB e 5 MB; tabelle annidate a profondità 10, 50, 200 e 1000; 5.000 elementi con uno `style`; un `<style>` con 2.000 regole con `url()` esterni; quattro `data:` URI da 1 MB; i casi patologici chiesti più quelli che mettono in difficoltà le regex trovate nella A.4.
- Misura: un primo giro (colonna "primo giro", con il JIT), due giri di riscaldamento, poi 20 giri (100 se un giro dura meno di 5 ms; 3 se il primo giro supera 4 s, e la colonna "giri" lo dice). Mediana e 95° percentile del tempo, MB allocati dal thread (mediana), eccezioni per giro. Prima di ogni giro una raccolta completa; prima di tutto 60 giri di riscaldamento delle funzioni misurate. Tutto su un thread con stack da 512 MB, così i casi profondi danno un tempo invece di un overflow (quanto stack serve lo dice A.7).
- Ripetibilità: fra una prima esecuzione senza il riscaldamento globale e quella riportata qui, le mediane dei casi da 1 MB in su e dei casi patologici sono rimaste entro il 10%; quelle dei casi piccoli erano più alte prima (codice non ancora ottimizzato dal JIT), da cui il riscaldamento. La macchina virtuale è condivisa: i 95° percentili hanno qualche picco isolato.

### A.1 MailHtml.Prepare (immagini non consentite, tema scuro e chiaro)

Cosa dice la tabella:

- Andamento sulle newsletter: l'analisi di AngleSharp cresce come l'ingresso (50 KB → 5 MB: 1,04 → 3,47 → 16,7 → 78,6 ms); `Prepare` un po' di più, soprattutto da 1 a 5 MB (chiaro 40,9 → 307 ms, ×7,5 per ×5; scuro 61 → 446 ms, ×7,3), e alloca 241 MB (chiaro) e 313 MB (scuro) per l'email da 5 MB (V-35).
- Il tema scuro costa 1,5-2 volte il chiaro sulle newsletter e molto di più con molti stili: `<style>` con 2.000 regole 84,6 contro 4,1 ms, 5.000 stili negli elementi 126 contro 50 ms; la colonna delle eccezioni dice perché (V-07).
- Tabelle annidate: normali fino a 200 livelli nel tema chiaro (2,4 ms), ma il tema scuro cresce col quadrato della profondità (200 livelli 7,4 ms, 1000 livelli 93 ms contro 17,4 nel chiaro; V-03).
- Casi patologici, tutti con email da 30 a 140 KB: 100.000 "<" 1,7-1,8 s e 9,5 GB allocati per giro, quasi tutto in AngleSharp (V-02); 20.000 `<div>` non chiusi analisi 4,1 s e `Prepare` scuro 7,7 s; 20.000 `<b><i>` analisi 5,6 s e `Prepare` scuro 38,5 s (V-03); "&lt;meta " ripetuto 10.000 volte 1,3 s nel tema scuro (la regex `OwnDarkMode` gira solo lì) e 1,5 ms nel chiaro; "url(" ripetuto 10.000 volte in uno stile 6,7-6,8 s in tutti e due i temi; "pixel" ripetuto 20.000 volte nell'indirizzo di un'immagine 0,66-0,83 s; "url(" e 30.000 spazi in un attributo 1,8 s; una parola di 30.000 lettere e una "\" in uno stile 2,4-2,5 s (V-04).
- A posto: attributo `title` da 1 MB (8 ms), quattro `data:` URI da 1 MB (67-73 ms, 130 MB allocati: le stringhe base64 copiate più volte), attributo `style` da 1 MB nel tema chiaro (8,7 ms; nel tema scuro 202 ms: sono 75.000 colori da adattare).

| gruppo | caso | ingresso KB (caratteri/1024) | mediana ms | 95° percentile ms | MB allocati (mediana) | eccezioni per giro | giri | primo giro ms |
|---|---|---|---|---|---|---|---|---|
| solo analisi AngleSharp | newsletter 50 KB | 51,2 | 1,04 | 1,28 | 0,6 | 0 | 100 | 1,1 |
| Prepare | newsletter 50 KB, scuro | 51,2 | 3,36 | 3,54 | 3,4 | 146 | 20 | 5,2 |
| Prepare | newsletter 50 KB, chiaro | 51,2 | 1,91 | 2,31 | 2,5 | 0 | 100 | 2,3 |
| solo analisi AngleSharp | newsletter 200 KB | 201 | 3,47 | 3,71 | 2,3 | 0 | 100 | 3,8 |
| Prepare | newsletter 200 KB, scuro | 201 | 11,57 | 12,20 | 12,3 | 348 | 20 | 15,1 |
| Prepare | newsletter 200 KB, chiaro | 201 | 7,23 | 7,83 | 9,2 | 0 | 20 | 7,4 |
| solo analisi AngleSharp | newsletter 1 MB | 1025,2 | 16,74 | 17,72 | 13,3 | 0 | 20 | 17,7 |
| Prepare | newsletter 1 MB, scuro | 1025,2 | 61,41 | 64,65 | 63,6 | 1456 | 20 | 88,8 |
| Prepare | newsletter 1 MB, chiaro | 1025,2 | 40,90 | 44,57 | 49,0 | 0 | 20 | 43,1 |
| solo analisi AngleSharp | newsletter 5 MB | 5121 | 78,58 | 98,30 | 62,4 | 0 | 20 | 90,8 |
| Prepare | newsletter 5 MB, scuro | 5121 | 445,57 | 471,50 | 312,7 | 6944 | 20 | 519,1 |
| Prepare | newsletter 5 MB, chiaro | 5121 | 307,19 | 338,53 | 240,7 | 0 | 20 | 334,4 |
| solo analisi AngleSharp | newsletter 200 KB con tema scuro proprio | 201,1 | 3,56 | 4,15 | 2,3 | 0 | 100 | 3,5 |
| Prepare | newsletter 200 KB con tema scuro proprio, scuro | 201,1 | 9,75 | 17,95 | 12,0 | 268 | 20 | 16,2 |
| Prepare | newsletter 200 KB con tema scuro proprio, chiaro | 201,1 | 7,29 | 7,91 | 9,2 | 0 | 20 | 11,3 |
| solo analisi AngleSharp | tabelle annidate 10 | 1,8 | 0,16 | 0,20 | 0,1 | 0 | 100 | 0,9 |
| Prepare | tabelle annidate 10, scuro | 1,8 | 0,46 | 0,53 | 0,2 | 20 | 100 | 1,0 |
| Prepare | tabelle annidate 10, chiaro | 1,8 | 0,26 | 0,32 | 0,1 | 0 | 100 | 0,2 |
| solo analisi AngleSharp | tabelle annidate 50 | 9,2 | 0,41 | 0,46 | 0,2 | 0 | 100 | 0,4 |
| Prepare | tabelle annidate 50, scuro | 9,2 | 1,56 | 1,65 | 1,0 | 100 | 100 | 2,1 |
| Prepare | tabelle annidate 50, chiaro | 9,2 | 0,68 | 0,74 | 0,5 | 0 | 100 | 0,7 |
| solo analisi AngleSharp | tabelle annidate 200 | 36,9 | 1,46 | 1,59 | 0,8 | 0 | 100 | 1,6 |
| Prepare | tabelle annidate 200, scuro | 36,9 | 7,43 | 7,83 | 3,9 | 400 | 20 | 8,5 |
| Prepare | tabelle annidate 200, chiaro | 36,9 | 2,40 | 3,68 | 2,0 | 0 | 100 | 1,9 |
| solo analisi AngleSharp | tabelle annidate 1000 | 185,4 | 9,43 | 9,85 | 3,5 | 0 | 20 | 9,8 |
| Prepare | tabelle annidate 1000, scuro | 185,4 | 93,14 | 101,27 | 18,8 | 2000 | 20 | 81,2 |
| Prepare | tabelle annidate 1000, chiaro | 185,4 | 17,39 | 18,24 | 9,2 | 0 | 20 | 18,2 |
| solo analisi AngleSharp | 5000 stili negli elementi | 628,8 | 11,98 | 12,76 | 8,1 | 0 | 20 | 13,2 |
| Prepare | 5000 stili negli elementi, scuro | 628,8 | 126,24 | 128,99 | 66,5 | 10000 | 20 | 153,6 |
| Prepare | 5000 stili negli elementi, chiaro | 628,8 | 50,13 | 61,21 | 21,6 | 0 | 20 | 62,6 |
| solo analisi AngleSharp | &lt;style> con 2000 regole | 332,3 | 1,15 | 1,23 | 1,7 | 0 | 100 | 2,2 |
| Prepare | &lt;style> con 2000 regole, scuro | 332,3 | 84,57 | 85,83 | 35,6 | 24000 | 20 | 78,5 |
| Prepare | &lt;style> con 2000 regole, chiaro | 332,3 | 4,06 | 4,38 | 6,9 | 0 | 100 | 4,6 |
| solo analisi AngleSharp | data: URI 4 x 1 MB | 4096,3 | 39,26 | 43,98 | 60,2 | 0 | 20 | 42,6 |
| Prepare | data: URI 4 x 1 MB, scuro | 4096,3 | 67,13 | 83,95 | 132,4 | 0 | 20 | 100,6 |
| Prepare | data: URI 4 x 1 MB, chiaro | 4096,3 | 73,30 | 76,65 | 130,4 | 0 | 20 | 63,3 |
| solo analisi AngleSharp | 100.000 "<" | 97,7 | 1792,84 | 1931,91 | 9543,6 | 0 | 20 | 1884,8 |
| Prepare | 100.000 "<", scuro | 97,7 | 1732,83 | 1890,35 | 9548,2 | 0 | 20 | 1804,8 |
| Prepare | 100.000 "<", chiaro | 97,7 | 1810,66 | 2011,67 | 9548,2 | 0 | 20 | 1713,8 |
| solo analisi AngleSharp | attributo title da 1 MB | 1024,1 | 4,07 | 4,32 | 6,0 | 0 | 100 | 4,5 |
| Prepare | attributo title da 1 MB, scuro | 1024,1 | 8,26 | 9,76 | 22,1 | 0 | 20 | 12,9 |
| Prepare | attributo title da 1 MB, chiaro | 1024,1 | 7,62 | 9,52 | 22,1 | 0 | 20 | 7,3 |
| solo analisi AngleSharp | attributo style da 1 MB | 1024,1 | 4,07 | 4,40 | 6,0 | 0 | 100 | 4,2 |
| Prepare | attributo style da 1 MB, scuro | 1024,1 | 202,28 | 232,47 | 181,8 | 0 | 20 | 282,6 |
| Prepare | attributo style da 1 MB, chiaro | 1024,1 | 8,71 | 11,50 | 22,1 | 0 | 20 | 11,1 |
| solo analisi AngleSharp | 5000 &lt;div> non chiusi | 29,3 | 139,76 | 152,30 | 2,6 | 0 | 20 | 140,6 |
| Prepare | 5000 &lt;div> non chiusi, scuro | 29,3 | 216,80 | 237,23 | 6,0 | 0 | 20 | 220,4 |
| Prepare | 5000 &lt;div> non chiusi, chiaro | 29,3 | 141,10 | 150,59 | 6,0 | 0 | 20 | 142,8 |
| solo analisi AngleSharp | 20.000 &lt;div> non chiusi | 117,2 | 4120,83 | 4168,86 | 10,2 | 0 | 3 | 4153,2 |
| Prepare | 20.000 &lt;div> non chiusi, scuro | 117,2 | 7689,35 | 7722,22 | 23,2 | 0 | 3 | 7103,3 |
| Prepare | 20.000 &lt;div> non chiusi, chiaro | 117,2 | 3763,21 | 3874,29 | 23,2 | 0 | 3 | 4228,9 |
| solo analisi AngleSharp | 20.000 &lt;b>&lt;i> non chiusi | 136,8 | 5596,80 | 5632,06 | 18,2 | 0 | 3 | 5639,8 |
| Prepare | 20.000 &lt;b>&lt;i> non chiusi, scuro | 136,8 | 38523,61 | 40227,43 | 42,0 | 0 | 3 | 38863,6 |
| Prepare | 20.000 &lt;b>&lt;i> non chiusi, chiaro | 136,8 | 5622,69 | 5924,99 | 42,0 | 0 | 3 | 5314,9 |
| solo analisi AngleSharp | "&lt;meta " ripetuto 10.000 volte (60 KB) | 58,6 | 3,20 | 4,44 | 1,5 | 0 | 20 | 5,5 |
| Prepare | "&lt;meta " ripetuto 10.000 volte (60 KB), scuro | 58,6 | 1300,61 | 1348,04 | 1,5 | 0 | 20 | 1305,3 |
| Prepare | "&lt;meta " ripetuto 10.000 volte (60 KB), chiaro | 58,6 | 1,47 | 1,67 | 1,5 | 0 | 100 | 1,5 |
| solo analisi AngleSharp | style con "url(" ripetuto 10.000 volte (40 KB) | 39,1 | 0,24 | 0,30 | 0,2 | 0 | 100 | 0,3 |
| Prepare | style con "url(" ripetuto 10.000 volte (40 KB), scuro | 39,1 | 6767,15 | 7861,59 | 19,5 | 30000 | 3 | 6931,6 |
| Prepare | style con "url(" ripetuto 10.000 volte (40 KB), chiaro | 39,1 | 6726,24 | 7478,93 | 1,0 | 0 | 3 | 7637,0 |
| solo analisi AngleSharp | src con "pixel" ripetuto 20.000 volte | 97,7 | 1,30 | 1,76 | 2,8 | 0 | 100 | 2,3 |
| Prepare | src con "pixel" ripetuto 20.000 volte, scuro | 97,7 | 656,38 | 715,24 | 2,8 | 0 | 20 | 1051,0 |
| Prepare | src con "pixel" ripetuto 20.000 volte, chiaro | 97,7 | 831,92 | 850,37 | 2,8 | 0 | 20 | 664,3 |
| solo analisi AngleSharp | attributo con url( e 30.000 spazi | 29,4 | 0,21 | 0,27 | 0,1 | 0 | 100 | 0,4 |
| Prepare | attributo con url( e 30.000 spazi, scuro | 29,4 | 1825,27 | 1880,89 | 0,7 | 0 | 20 | 1844,8 |
| Prepare | attributo con url( e 30.000 spazi, chiaro | 29,4 | 1787,38 | 1851,17 | 0,7 | 0 | 20 | 1820,1 |
| solo analisi AngleSharp | style con parola da 30.000 lettere e una \ | 29,4 | 0,20 | 0,25 | 0,1 | 0 | 100 | 0,4 |
| Prepare | style con parola da 30.000 lettere e una \, scuro | 29,4 | 2446,94 | 2506,34 | 0,8 | 0 | 20 | 2575,1 |
| Prepare | style con parola da 30.000 lettere e una \, chiaro | 29,4 | 2460,69 | 2565,78 | 0,7 | 0 | 20 | 2241,7 |

### A.2 PlainPreview, ToText e ReadableText sugli stessi ingressi

`PlainPreview` lavora sulle anteprime, che in Missiva sono brevi (Graph `bodyPreview` al massimo 255 caratteri, Core/GraphProvider.cs:525; `PreviewText` di MailKit, Core/GmailProvider.cs:481, Core/ImapProvider.cs:602 e 900; copia locale, Core/MailCache.cs:41): misurarla su HTML interi serve solo a vedere l'andamento, che è lineare su questi ingressi; le sue regex quadratiche (A.4) con l'anteprima vera costano microsecondi (ultime righe). `ToText` e `ReadableText` invece ricevono il corpo intero (V-08, V-09, V-28):

- newsletter: lineari (200 KB 4,9 ms, 1 MB 31-34 ms, 5 MB 175-185 ms);
- quadratiche in profondità: 200 tabelle annidate 5,7 ms, 1000 tabelle 147 ms, 20.000 `<div>` non chiusi 19,5 s (`Closest("pre")` per ogni testo, V-03); su un thread con lo stack di Windows alcuni di questi chiudono il processo (A.7, V-01);
- quadratiche in larghezza: 5.000 righe con `<br>` nello stesso `<div>` 262 ms (la sostituzione dei `<br>`, V-09);
- 100.000 "<": 1,26 s e 9,5 GB, come `Prepare` (V-02).

| gruppo | caso | ingresso KB (caratteri/1024) | mediana ms | 95° percentile ms | MB allocati (mediana) | eccezioni per giro | giri | primo giro ms |
|---|---|---|---|---|---|---|---|---|
| PlainPreview | newsletter 50 KB | 51,2 | 0,51 | 0,61 | 0,3 | 0 | 100 | 1,9 |
| ToText | newsletter 50 KB | 51,2 | 1,37 | 1,86 | 1,1 | 0 | 100 | 1,5 |
| ReadableText | newsletter 50 KB | 51,2 | 1,34 | 1,43 | 1,2 | 0 | 100 | 1,7 |
| PlainPreview | newsletter 200 KB | 201 | 1,19 | 1,36 | 1,1 | 0 | 100 | 1,6 |
| ToText | newsletter 200 KB | 201 | 4,90 | 9,00 | 4,3 | 0 | 20 | 11,9 |
| ReadableText | newsletter 200 KB | 201 | 4,97 | 7,99 | 4,6 | 0 | 100 | 3,9 |
| PlainPreview | newsletter 1 MB | 1025,2 | 6,69 | 12,01 | 5,5 | 0 | 20 | 7,8 |
| ToText | newsletter 1 MB | 1025,2 | 34,12 | 45,15 | 22,4 | 0 | 20 | 75,2 |
| ReadableText | newsletter 1 MB | 1025,2 | 31,07 | 38,04 | 24,1 | 0 | 20 | 30,9 |
| PlainPreview | newsletter 5 MB | 5121 | 37,02 | 49,47 | 27,7 | 0 | 20 | 61,8 |
| ToText | newsletter 5 MB | 5121 | 174,84 | 210,67 | 108,2 | 0 | 20 | 275,3 |
| ReadableText | newsletter 5 MB | 5121 | 185,11 | 209,36 | 116,3 | 0 | 20 | 187,1 |
| PlainPreview | newsletter 200 KB con tema scuro proprio | 201,1 | 1,19 | 1,30 | 1,1 | 0 | 100 | 1,4 |
| ToText | newsletter 200 KB con tema scuro proprio | 201,1 | 3,94 | 5,60 | 4,3 | 0 | 20 | 7,1 |
| ReadableText | newsletter 200 KB con tema scuro proprio | 201,1 | 5,13 | 6,01 | 4,6 | 0 | 100 | 4,0 |
| PlainPreview | tabelle annidate 10 | 1,8 | 0,03 | 0,05 | 0,0 | 0 | 100 | 0,0 |
| ToText | tabelle annidate 10 | 1,8 | 0,25 | 0,32 | 0,1 | 0 | 100 | 0,4 |
| ReadableText | tabelle annidate 10 | 1,8 | 0,21 | 0,31 | 0,1 | 0 | 100 | 0,6 |
| PlainPreview | tabelle annidate 50 | 9,2 | 0,05 | 0,06 | 0,0 | 0 | 100 | 0,1 |
| ToText | tabelle annidate 50 | 9,2 | 0,87 | 1,18 | 0,3 | 0 | 100 | 0,8 |
| ReadableText | tabelle annidate 50 | 9,2 | 0,88 | 0,97 | 0,3 | 0 | 100 | 1,1 |
| PlainPreview | tabelle annidate 200 | 36,9 | 0,17 | 0,23 | 0,2 | 0 | 100 | 0,2 |
| ToText | tabelle annidate 200 | 36,9 | 5,66 | 6,53 | 1,0 | 0 | 20 | 5,9 |
| ReadableText | tabelle annidate 200 | 36,9 | 5,64 | 6,16 | 1,0 | 0 | 20 | 6,4 |
| PlainPreview | tabelle annidate 1000 | 185,4 | 0,86 | 1,03 | 0,8 | 0 | 100 | 0,7 |
| ToText | tabelle annidate 1000 | 185,4 | 146,84 | 163,68 | 4,8 | 0 | 20 | 167,1 |
| ReadableText | tabelle annidate 1000 | 185,4 | 149,72 | 172,04 | 4,8 | 0 | 20 | 116,0 |
| PlainPreview | 5000 stili negli elementi | 628,8 | 2,47 | 3,03 | 1,2 | 0 | 100 | 2,6 |
| ToText | 5000 stili negli elementi | 628,8 | 261,59 | 287,31 | 12,0 | 0 | 20 | 409,9 |
| ReadableText | 5000 stili negli elementi | 628,8 | 261,48 | 283,82 | 12,3 | 0 | 20 | 263,2 |
| PlainPreview | &lt;style> con 2000 regole | 332,3 | 1,92 | 2,19 | 1,0 | 0 | 100 | 2,1 |
| ToText | &lt;style> con 2000 regole | 332,3 | 1,57 | 1,73 | 1,8 | 0 | 100 | 2,0 |
| ReadableText | &lt;style> con 2000 regole | 332,3 | 1,57 | 1,67 | 1,8 | 0 | 100 | 1,5 |
| PlainPreview | data: URI 4 x 1 MB | 4096,3 | 9,53 | 9,88 | 8,0 | 0 | 20 | 10,8 |
| ToText | data: URI 4 x 1 MB | 4096,3 | 33,51 | 38,81 | 60,2 | 0 | 20 | 40,8 |
| ReadableText | data: URI 4 x 1 MB | 4096,3 | 25,66 | 34,19 | 60,2 | 0 | 20 | 33,4 |
| PlainPreview | 100.000 "<" | 97,7 | 4,07 | 8,08 | 0,0 | 0 | 100 | 3,2 |
| ToText | 100.000 "<" | 97,7 | 1262,63 | 1322,26 | 9545,0 | 0 | 20 | 2149,7 |
| ReadableText | 100.000 "<" | 97,7 | 1282,04 | 1353,09 | 9545,0 | 0 | 20 | 1234,1 |
| PlainPreview | attributo title da 1 MB | 1024,1 | 1,71 | 2,56 | 2,0 | 0 | 100 | 2,7 |
| ToText | attributo title da 1 MB | 1024,1 | 4,22 | 4,71 | 6,0 | 0 | 100 | 4,9 |
| ReadableText | attributo title da 1 MB | 1024,1 | 4,09 | 4,69 | 6,0 | 0 | 100 | 4,1 |
| PlainPreview | attributo style da 1 MB | 1024,1 | 1,42 | 1,66 | 2,0 | 0 | 100 | 2,0 |
| ToText | attributo style da 1 MB | 1024,1 | 4,26 | 4,51 | 6,0 | 0 | 20 | 5,3 |
| ReadableText | attributo style da 1 MB | 1024,1 | 4,10 | 4,41 | 6,0 | 0 | 100 | 4,4 |
| PlainPreview | 5000 &lt;div> non chiusi | 29,3 | 0,30 | 0,34 | 0,0 | 0 | 100 | 0,3 |
| ToText | 5000 &lt;div> non chiusi | 29,3 | 486,31 | 503,47 | 4,5 | 0 | 20 | 492,4 |
| ReadableText | 5000 &lt;div> non chiusi | 29,3 | 388,96 | 481,97 | 4,5 | 0 | 20 | 457,7 |
| PlainPreview | 20.000 &lt;div> non chiusi | 117,2 | 1,17 | 1,26 | 0,0 | 0 | 100 | 1,3 |
| ToText | 20.000 &lt;div> non chiusi | 117,2 | 19507,21 | 19617,27 | 17,9 | 0 | 3 | 20696,5 |
| ReadableText | 20.000 &lt;div> non chiusi | 117,2 | 19826,04 | 19912,58 | 17,9 | 0 | 3 | 20286,5 |
| PlainPreview | 20.000 &lt;b>&lt;i> non chiusi | 136,8 | 1,80 | 2,75 | 0,0 | 0 | 100 | 2,3 |
| ToText | 20.000 &lt;b>&lt;i> non chiusi | 136,8 | 21690,27 | 22474,48 | 22,7 | 0 | 3 | 22554,0 |
| ReadableText | 20.000 &lt;b>&lt;i> non chiusi | 136,8 | 22018,20 | 22066,18 | 22,7 | 0 | 3 | 22162,9 |
| PlainPreview | "&lt;meta " ripetuto 10.000 volte (60 KB) | 58,6 | 0,77 | 0,89 | 0,1 | 0 | 100 | 0,8 |
| ToText | "&lt;meta " ripetuto 10.000 volte (60 KB) | 58,6 | 1,33 | 1,46 | 1,5 | 0 | 100 | 1,8 |
| ReadableText | "&lt;meta " ripetuto 10.000 volte (60 KB) | 58,6 | 1,33 | 1,61 | 1,5 | 0 | 100 | 1,3 |
| PlainPreview | style con "url(" ripetuto 10.000 volte (40 KB) | 39,1 | 0,18 | 0,22 | 0,1 | 0 | 100 | 0,2 |
| ToText | style con "url(" ripetuto 10.000 volte (40 KB) | 39,1 | 0,28 | 0,34 | 0,2 | 0 | 100 | 0,4 |
| ReadableText | style con "url(" ripetuto 10.000 volte (40 KB) | 39,1 | 0,27 | 0,34 | 0,2 | 0 | 100 | 0,3 |
| PlainPreview | src con "pixel" ripetuto 20.000 volte | 97,7 | 0,11 | 0,13 | 0,0 | 0 | 100 | 0,2 |
| ToText | src con "pixel" ripetuto 20.000 volte | 97,7 | 1,22 | 1,67 | 2,8 | 0 | 100 | 1,6 |
| ReadableText | src con "pixel" ripetuto 20.000 volte | 97,7 | 1,24 | 1,88 | 2,8 | 0 | 100 | 1,3 |
| PlainPreview | attributo con url( e 30.000 spazi | 29,4 | 0,13 | 0,16 | 0,0 | 0 | 100 | 0,1 |
| ToText | attributo con url( e 30.000 spazi | 29,4 | 0,24 | 0,30 | 0,2 | 0 | 100 | 0,3 |
| ReadableText | attributo con url( e 30.000 spazi | 29,4 | 0,27 | 0,38 | 0,2 | 0 | 100 | 0,3 |
| PlainPreview | style con parola da 30.000 lettere e una \ | 29,4 | 0,07 | 0,10 | 0,1 | 0 | 100 | 0,1 |
| ToText | style con parola da 30.000 lettere e una \ | 29,4 | 0,26 | 0,31 | 0,2 | 0 | 100 | 0,4 |
| ReadableText | style con parola da 30.000 lettere e una \ | 29,4 | 0,25 | 0,33 | 0,2 | 0 | 100 | 0,2 |
| PlainPreview | anteprima vera da 210 caratteri | 0,2 | 0,02 | 0,04 | 0,0 | 0 | 100 | 0,4 |
| PlainPreview | 255 "[" (il massimo di Graph) | 0,2 | 0,05 | 0,06 | 0,0 | 0 | 100 | 1,1 |

### A.3 Invitation, vCard e CSV, SearchTerms, Mailto, ContactLinks, SenderCheck

Tutte veloci anche agli ingressi massimi che Missiva accetta (misurate a una sola dimensione: l'andamento delle loro regex è in A.4). Note:

- `Invitation.Parse` su un .ics al limite di 256 KB (`MaxLength`): 1,4-9 ms secondo la forma (molte righe, molti partecipanti, una descrizione piegata in 3.400 righe). Le 2 eccezioni per giro sono `TimeZoneInfo.FindSystemTimeZoneById` su un TZID che il sistema non conosce ("Customized Time Zone", come negli inviti di Outlook), presa in Core/Invitation.cs:269 per passare al fuso definito nell'invito: trascurabile.
- vCard: la 3.0 da 5 MB (4.562 contatti) 38,6 ms; la 2.1 quoted-printable da 5 MB (3.645 contatti) 180 ms e 214 MB allocati, perché `QuotedPrintable` (Core/Contacts.cs:413-427) crea una stringa e un vettore per ogni carattere (`Encoding.UTF8.GetBytes(value[i].ToString())`). Gira nell'importazione, che è già in sottofondo: non serve cambiarla, ma un `Span<byte>` con `Encoding.UTF8.GetBytes(ReadOnlySpan<char>, Span<byte>)` la renderebbe lineare senza allocazioni.
- CSV di Outlook con 5.000 contatti (943 KB, virgolette, virgole e a capo nei campi): 7,3 ms.
- `SearchTerms`: un `Parse` con sette filtri costa circa 1 µs (1.000 in 1,0 ms); `Matches` su 10.000 messaggi 0,4 ms; `ToGmail`, `ToKql` e `Filter` insieme circa 1,6 µs.
- `Mailto.Parse` di un link con 24 KB di testo 0,23 ms; `ContactLinks.NormalizePhone` e `IsAllowed` su 4.500 voci 0,3 e 1,0 ms; `SenderCheck.Parse` circa 5 µs per un'intestazione vera e 0,4 ms per una da 1.000 parti.

| gruppo | caso | ingresso KB (caratteri/1024) | mediana ms | 95° percentile ms | MB allocati (mediana) | eccezioni per giro | giri | primo giro ms |
|---|---|---|---|---|---|---|---|---|
| Invitation.Parse | .ics da 255 KB (partecipanti) | 255,8 | 5,37 | 7,76 | 5,2 | 2 | 20 | 26,7 |
| Invitation.Parse | .ics da 252 KB (descrizione) | 252,5 | 8,97 | 9,59 | 13,1 | 2 | 20 | 11,3 |
| Invitation.Parse | .ics da 143 KB (righe) | 143,7 | 1,43 | 2,16 | 4,1 | 2 | 100 | 2,6 |
| ContactFiles.VCard | vCard 3.0, 5.0 MB | 5119,3 | 38,55 | 70,22 | 70,4 | 0 | 20 | 118,6 |
| ContactFiles.VCard | vCard 2.1 quoted-printable, 5.0 MB | 5119,8 | 180,22 | 188,85 | 213,9 | 0 | 20 | 285,1 |
| ContactFiles.Csv | CSV di Outlook, 5000 contatti | 943 | 7,28 | 21,54 | 8,1 | 0 | 20 | 36,5 |
| SearchTerms.Parse | ricerca con 7 filtri (x1000) | 0,1 | 1,01 | 2,48 | 2,1 | 0 | 100 | 4,9 |
| SearchTerms.Parse | ricerca di 2000 parole, 200 da: | 21,2 | 0,09 | 0,13 | 0,2 | 0 | 100 | 0,2 |
| SearchTerms.Matches | 10.000 messaggi | 0 | 0,41 | 0,69 | 0,0 | 0 | 100 | 1,2 |
| SearchTerms.ToGmail/ToKql/Filter | x1000 | 0,1 | 1,61 | 2,21 | 3,3 | 0 | 100 | 4,1 |
| Mailto.Parse | link con testo da 24 KB | 24,7 | 0,23 | 0,44 | 0,2 | 0 | 100 | 1,7 |
| ContactLinks.NormalizePhone | 4500 numeri | 0 | 0,31 | 1,39 | 0,8 | 0 | 100 | 2,4 |
| ContactLinks.IsAllowed | 4500 indirizzi | 0 | 0,96 | 1,56 | 0,5 | 0 | 100 | 4,3 |
| SenderCheck.Parse | intestazione vera (x1000) | 0,2 | 5,02 | 5,37 | 3,9 | 0 | 20 | 7,1 |
| SenderCheck.Parse | intestazione da 1000 parti | 87,9 | 0,39 | 0,49 | 0,9 | 0 | 100 | 0,5 |

### A.4 Espressioni regolari con ingressi scelti per metterle in difficoltà

Tutte le espressioni regolari dei file chiesti, ognuna con l'operazione che usa il codice (Replace, IsMatch, Match, Matches) e un ingresso scelto per metterla in difficoltà, a lunghezze doppie da 4.000 a 256.000 caratteri (mediana di 5 giri; ci si ferma quando un giro passa 3 s). "Esponente" è il logaritmo in base 2 del rapporto fra gli ultimi due tempi: circa 1 vuol dire lineare, circa 2 quadratico. Le righe "proposta" sono le versioni corrette di V-04, sugli stessi ingressi.

Quadratiche (esponente 1,7-2,1): `ImagePlaceholder`, `MarkdownLink` e `CutMarkdownLink` (solo sulle anteprime, quindi su al più qualche centinaio di caratteri: innocue), `SpacerName`, `OwnDarkMode`, `RemoteUrlFunction`, `CssUrl`, `CssEscapedFunction` e `ColorToken`, che lavorano su HTML, stili e attributi delle email (V-04). Le altre sono lineari o limitate: quelle di `Invitation`, `ContactLinks` e `SenderCheck` sono ancorate o hanno quantificatori limitati (`Duration` riceve al più 64 caratteri, Core/Invitation.cs:281).

| dove | regex | uso | ingresso | ms a 4k | 8k | 16k | 32k | 64k | 128k | 256k | esponente |
|---|---|---|---|---|---|---|---|---|---|---|---|
| MailHtml.cs:201 | BlankLines | Replace | "\n\nx" ripetuto | 0,000 | 0,000 | 0,001 | 0,002 | 0,004 | 0,007 | 0,015 | troppo veloce |
| MailHtml.cs:248 | CutEntity | Replace | "&ab" ripetuto | 0,20 | 0,43 | 0,60 | 1,15 | 3,20 | 4,88 | 8,72 | 0,8 |
| MailHtml.cs:251 | ImagePlaceholder | Replace | "[image:" ripetuto senza ] | 0,026 | 0,084 | 0,31 | 1,60 | 7,98 | 32 | 125 | 2,0 |
| MailHtml.cs:254 | MarkdownLink | Replace | "[" ripetuto | 0,58 | 1,09 | 2,99 | 13 | 55 | 210 | 844 | 2,0 |
| MailHtml.cs:261 | CutMarkdownLink | Replace | "[" ripetuto | 0,52 | 1,07 | 2,84 | 16 | 52 | 217 | 848 | 2,0 |
| MailHtml.cs:268 | WebAddress | Replace | "( <\t" ripetuto | 0,12 | 0,27 | 0,48 | 0,81 | 1,56 | 3,17 | 6,29 | 1,0 |
| MailHtml.cs:272 | AngleMailto | Replace | "<", n spazi, "x" | 0,039 | 0,021 | 0,023 | 0,038 | 0,065 | 0,11 | 0,24 | 1,0 |
| MailHtml.cs:276 | EdgeBracket | Replace | " <" ripetuto | 0,11 | 0,16 | 0,15 | 0,26 | 0,49 | 0,95 | 1,86 | 1,0 |
| MailHtml.cs:279 | Invisible | Replace | U+200B ripetuto | 0,14 | 0,19 | 0,37 | 0,74 | 1,50 | 3,09 | 6,27 | 1,0 |
| MailHtml.cs:313 | SpacerName | IsMatch | "pixel" ripetuto senza .gif | 15 | 5,38 | 21 | 85 | 349 | 1363 | 5411 | 2,0 |
| MailHtml.cs:316 | OwnDarkMode | IsMatch | "&lt;meta " ripetuto senza > | 5,81 | 23 | 93 | 376 | 1503 | 5863 | - | 2,0 |
| MailHtml.cs:353 | RemoteUrlFunction | IsMatch | "url(", n spazi, "x" | 32 | 126 | 511 | 2120 | 7945 | - | - | 1,9 |
| MailHtml.cs:363 | WhitespaceRun | Replace | " x" ripetuto | 0,039 | 0,079 | 0,16 | 0,33 | 0,95 | 1,37 | 3,12 | 1,2 |
| MailHtml.cs:366 | UrlInText | Replace | "http://." ripetuto | 0,051 | 0,068 | 0,12 | 0,22 | 0,46 | 1,09 | 2,25 | 1,0 |
| MailHtml.cs:370 | RootSelectors | Replace | "{html-" ripetuto | 0,019 | 0,038 | 0,078 | 0,16 | 0,28 | 0,57 | 1,17 | 1,0 |
| MailHtml.cs:375 | CssUrl | Replace | "url(" ripetuto senza ) | 100 | 229 | 1198 | 4213 | - | - | - | 1,8 |
| MailHtml.cs:378 | CssImport | Replace | "@import" ripetuto | 0,000 | 0,000 | 0,001 | 0,001 | 0,002 | 0,003 | 0,007 | troppo veloce |
| MailHtml.cs:382 | CssImageSet | Replace | "image-set(" ripetuto | 0,041 | 0,081 | 0,16 | 0,29 | 0,63 | 1,08 | 2,22 | 1,0 |
| MailHtml.cs:389 | CssEscapedFunction | Replace | n lettere, uno spazio, una \ | 46 | 180 | 698 | 2840 | 10638 | - | - | 1,9 |
| MailHtml.cs:423 | ColorDeclaration | Replace | "color", n spazi, "x" | 0,003 | 0,006 | 0,012 | 0,024 | 0,048 | 0,097 | 0,19 | 1,0 |
| MailHtml.cs:426 | ColorToken | Replace | "url(" ripetuto senza ) | 0,060 | 0,16 | 0,51 | 2,35 | 11 | 43 | 221 | 2,4 |
| MailHtml.cs:426 | ColorToken (rgb) | Replace | "rgb(" ripetuto senza ) | 0,078 | 0,22 | 0,66 | 3,18 | 15 | 55 | 224 | 2,0 |
| MailHtml.cs:446 | CssBlock | Replace | "{}a" ripetuto | 0,062 | 0,16 | 0,20 | 0,38 | 0,79 | 1,81 | 3,36 | 0,9 |
| ContactLinks.cs:42 | PersonId | IsMatch | "people/" e n lettere | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | troppo veloce |
| ContactLinks.cs:45 | GoogleAddress | IsMatch | indirizzo con n lettere | 0,005 | 0,005 | 0,005 | 0,005 | 0,005 | 0,005 | 0,005 | troppo veloce |
| ContactLinks.cs:48 | AccountEmail | IsMatch | n lettere e @ | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | 0,001 | troppo veloce |
| ContactLinks.cs:51 | TelAddress | IsMatch | "tel:+" e n cifre | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | troppo veloce |
| Invitation.cs:122 | Text (\\n) | Replace | "\" ripetuto | 0,12 | 0,23 | 0,48 | 0,93 | 1,92 | 3,78 | 7,60 | 1,0 |
| Invitation.cs:204 | ^mailto: | Replace | n lettere | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | troppo veloce |
| Invitation.cs:246 | Offset | Match | "+" e n cifre | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | 0,000 | troppo veloce |
| Invitation.cs:282 | Duration | Match | "P", n cifre, "X" (nel codice solo fino a 64) | 0,012 | 0,025 | 0,049 | 0,097 | 0,20 | 0,42 | 0,82 | 0,9 |
| Invitation.cs:298 | BYDAY | Match | n cifre e "MOX" | 0,006 | 0,012 | 0,024 | 0,049 | 0,11 | 0,20 | 0,39 | 1,0 |
| Invitation.cs:332 | link https | Matches | "https://" ripetuto | 0,006 | 0,012 | 0,025 | 0,051 | 0,098 | 0,19 | 0,39 | 1,0 |
| Invitation.cs:492 | link https (corpo) | Matches | "https:/ " ripetuto | 0,000 | 0,001 | 0,001 | 0,003 | 0,005 | 0,010 | 0,020 | troppo veloce |
| proposta | SpacerName NonBacktracking | IsMatch | "pixel" ripetuto senza .gif | 0,012 | 0,023 | 0,044 | 0,086 | 0,17 | 0,34 | 0,70 | 1,0 |
| proposta | OwnDarkMode NonBacktracking | IsMatch | "&lt;meta " ripetuto senza > | 0,011 | 0,022 | 0,042 | 0,084 | 0,17 | 0,35 | 0,71 | 1,0 |
| proposta | RemoteUrlFunction riscritta | IsMatch | "url(", n spazi, "x" | 0,13 | 0,16 | 0,23 | 0,43 | 0,87 | 1,68 | 3,21 | 0,9 |
| proposta | CssUrl riscritta | Replace | "url(" ripetuto senza ) | 0,40 | 0,64 | 1,17 | 2,26 | 4,62 | 9,42 | 18 | 0,9 |
| proposta | CssUrl riscritta, apici | Replace | "url('" ripetuto | 0,17 | 0,34 | 0,86 | 1,51 | 2,77 | 5,38 | 12 | 1,2 |
| proposta | CssEscapedFunction NonBacktracking | Replace | n lettere, uno spazio, una \ | 0,011 | 0,022 | 0,055 | 0,10 | 0,21 | 0,46 | 0,90 | 1,0 |
| proposta | ColorToken riscritta | Replace | "url(" ripetuto senza ) | 0,087 | 0,12 | 0,25 | 0,66 | 1,32 | 1,95 | 4,11 | 1,1 |
| SenderCheck.cs:46 | risultato | Match | n lettere, n spazi, "!" | 0,012 | 0,024 | 0,060 | 0,12 | 0,20 | 0,40 | 0,82 | 1,0 |

### A.5 Suggerimenti a ogni lettera

Copia di `AddressBook.Suggest` e `Rank` con una rubrica di 5.000 voci (il 10% "no-reply", che `Wanted` scarta) e 4.500 contatti con due indirizzi, azienda, ruolo, cellulare e una nota di 30 parole, restituiti da una funzione che copia l'elenco come `ContactBook.ForSuggestions`; e la ricerca di Persone come `BuildEntries` (`Contact.Matches` e `OrderBy` per nome con `StringComparer.CurrentCultureIgnoreCase`), cultura it-IT. Nomi solo finti: Mario, Maria e "Nome1".."Nome10", Rossi e "Cognome1".."Cognome9". Una riga per lettera di "mario r".

- `Suggest`: 10-15 ms e 18 MB allocati a ogni lettera, quasi indipendenti da quello che si scrive: ogni volta si ricostruisce e si ri-tokenizza tutto (V-15). 18 MB per lettera vogliono dire raccolte di generazione 0 frequenti mentre si scrive.
- Persone: 8-62 ms a ogni lettera, e cresce allungando la ricerca: con meno contatti che corrispondono `Matches` non si ferma al primo campo e confronta tutti i campi, note comprese, con il confronto culturale (V-16). Il costo delle righe nuove di WPF non è compreso.

| gruppo | caso | ingresso KB (caratteri/1024) | mediana ms | 95° percentile ms | MB allocati (mediana) | eccezioni per giro | giri | primo giro ms |
|---|---|---|---|---|---|---|---|---|
| AddressBook.Suggest | "m", 8 proposte | 0 | 15,20 | 27,77 | 18,2 | 0 | 20 | 68,5 |
| Persone: Matches + OrderBy | "m" | 0 | 7,94 | 9,90 | 2,0 | 0 | 20 | 27,2 |
| AddressBook.Suggest | "ma", 8 proposte | 0 | 12,76 | 24,80 | 18,2 | 0 | 20 | 22,7 |
| Persone: Matches + OrderBy | "ma" | 0 | 21,33 | 22,81 | 2,0 | 0 | 20 | 21,6 |
| AddressBook.Suggest | "mar", 8 proposte | 0 | 11,10 | 12,65 | 18,2 | 0 | 20 | 17,4 |
| Persone: Matches + OrderBy | "mar" | 0 | 39,15 | 50,35 | 1,8 | 0 | 20 | 40,1 |
| AddressBook.Suggest | "mari", 8 proposte | 0 | 10,51 | 11,55 | 18,2 | 0 | 20 | 17,7 |
| Persone: Matches + OrderBy | "mari" | 0 | 38,81 | 39,44 | 1,8 | 0 | 20 | 39,4 |
| AddressBook.Suggest | "mario", 8 proposte | 0 | 10,38 | 12,40 | 18,2 | 0 | 20 | 17,6 |
| Persone: Matches + OrderBy | "mario" | 0 | 56,26 | 68,53 | 1,8 | 0 | 20 | 45,8 |
| AddressBook.Suggest | "mario r", 8 proposte | 0 | 13,70 | 16,55 | 18,2 | 0 | 20 | 21,9 |
| Persone: Matches + OrderBy | "mario r" | 0 | 62,16 | 63,18 | 1,8 | 0 | 20 | 67,3 |

### A.6 Le correzioni proposte, misurate sugli stessi ingressi

`MailHtmlFix` è la copia di `MailHtml.cs` con le correzioni di V-01, V-02, V-03, V-04 e V-07 (`mailhtmlfix.patch` in appendice): `EscapeStrayLt` prima dell'analisi, `keepSet` al posto della risalita fino alla radice, `TextOf` e `ToText` con una pila, le sei regex corrette, `ParseColor` senza eccezioni. L'originale è la mediana di A.1 e A.2; la corretta viene da un'esecuzione successiva dello stesso banco (le due colonne non vengono dallo stesso processo: sui casi normali una parte della differenza è rumore fra due esecuzioni). "Uscita uguale" confronta carattere per carattere il frammento di `Prepare` (o il testo di `ToText`) dell'originale e della corretta sullo stesso ingresso.

- Uscita identica in tutti i 69 confronti.
- Casi patologici: da secondi a millisecondi (100.000 "<" da 1,7-1,8 s a 5-8 ms; "&lt;meta " ripetuto da 1,3 s a 3,6 ms; "url(" ripetuto in uno stile da 6,7-6,8 s a 0,9-4,1 ms; "pixel" nell'indirizzo di un'immagine da 0,66-0,83 s a 1,6-2,0 ms; "url(" e 30.000 spazi da 1,8 s a 0,5-0,7 ms; una parola di 30.000 lettere e una "\" da 2,4-2,5 s a 0,5 ms; `ToText` con 5.000 righe `<br>` da 262 a 13 ms e con 1.000 tabelle annidate da 147 a 13 ms); l'annidamento profondo resta lento per il costo di AngleSharp (20.000 `<b><i>` nel tema scuro da 38,5 a 5,5 s, 20.000 `<div>` da 7,7 a 3,95 s e `ToText` da 19,5 a 3,9 s: quello che resta è l'analisi di AngleSharp, 4,1-5,6 s), che solo un limite di profondità prima dell'analisi può togliere (V-03, punto c).
- Casi normali: nel tema scuro le eccezioni passano a zero (`<style>` con 2.000 regole da 84,6 a 14,9 ms, 5.000 stili negli elementi da 126 a 91 ms); `ToText` delle newsletter da 1 e 5 MB più veloce (34 → 16 ms, 175 → 115 ms) perché non modifica il documento; `Prepare` sulle newsletter da -24% a +5%, nessun peggioramento oltre il rumore.
- Una prima versione della `CssUrl` corretta, con `\s` nella classe di caratteri, era 5 volte più lenta dell'originale su un `url()` da 1 MB (9,1 contro 1,8 ms) e rendeva l'email con i `data:` URI più lenta di circa 9 ms (alternando le due versioni 30 volte: 81 contro 72 ms e 75 contro 67 ms). Con gli spazi del CSS scritti per esteso costa 0,10 ms e l'email torna uguale (71,2 contro 71,9 ms): quella riportata qui è questa versione.

| funzione | caso | originale mediana ms | corretta mediana ms | uscita uguale |
| --- | --- | --- | --- | --- |
| Prepare | newsletter 50 KB, scuro | 3,36 | 2,79 | sì |
| Prepare | newsletter 50 KB, chiaro | 1,91 | 1,85 | sì |
| ToText | newsletter 50 KB | 1,37 | 1,09 | sì |
| Prepare | newsletter 200 KB, scuro | 11,57 | 10,33 | sì |
| Prepare | newsletter 200 KB, chiaro | 7,23 | 7,38 | sì |
| ToText | newsletter 200 KB | 4,90 | 4,11 | sì |
| Prepare | newsletter 1 MB, scuro | 61,41 | 49,24 | sì |
| Prepare | newsletter 1 MB, chiaro | 40,90 | 31,05 | sì |
| ToText | newsletter 1 MB | 34,12 | 16,07 | sì |
| Prepare | newsletter 5 MB, scuro | 445,57 | 394,93 | sì |
| Prepare | newsletter 5 MB, chiaro | 307,19 | 307,83 | sì |
| ToText | newsletter 5 MB | 174,84 | 114,61 | sì |
| Prepare | newsletter 200 KB con tema scuro proprio, scuro | 9,75 | 9,92 | sì |
| Prepare | newsletter 200 KB con tema scuro proprio, chiaro | 7,29 | 7,68 | sì |
| ToText | newsletter 200 KB con tema scuro proprio | 3,94 | 3,30 | sì |
| Prepare | tabelle annidate 10, scuro | 0,46 | 0,30 | sì |
| Prepare | tabelle annidate 10, chiaro | 0,26 | 0,23 | sì |
| ToText | tabelle annidate 10 | 0,25 | 0,16 | sì |
| Prepare | tabelle annidate 50, scuro | 1,56 | 0,86 | sì |
| Prepare | tabelle annidate 50, chiaro | 0,68 | 0,65 | sì |
| ToText | tabelle annidate 50 | 0,87 | 0,48 | sì |
| Prepare | tabelle annidate 200, scuro | 7,43 | 4,04 | sì |
| Prepare | tabelle annidate 200, chiaro | 2,40 | 2,61 | sì |
| ToText | tabelle annidate 200 | 5,66 | 1,67 | sì |
| Prepare | tabelle annidate 1000, scuro | 93,14 | 25,57 | sì |
| Prepare | tabelle annidate 1000, chiaro | 17,39 | 18,32 | sì |
| ToText | tabelle annidate 1000 | 146,84 | 12,99 | sì |
| Prepare | 5000 stili negli elementi, scuro | 126,24 | 91,28 | sì |
| Prepare | 5000 stili negli elementi, chiaro | 50,13 | 61,26 | sì |
| ToText | 5000 stili negli elementi | 261,59 | 13,08 | sì |
| Prepare | &lt;style> con 2000 regole, scuro | 84,57 | 14,92 | sì |
| Prepare | &lt;style> con 2000 regole, chiaro | 4,06 | 3,61 | sì |
| ToText | &lt;style> con 2000 regole | 1,57 | 1,18 | sì |
| Prepare | data: URI 4 x 1 MB, scuro | 67,13 | 71,73 | sì |
| Prepare | data: URI 4 x 1 MB, chiaro | 73,30 | 65,41 | sì |
| ToText | data: URI 4 x 1 MB | 33,51 | 34,67 | sì |
| Prepare | 100.000 "<", scuro | 1732,83 | 8,02 | sì |
| Prepare | 100.000 "<", chiaro | 1810,66 | 5,08 | sì |
| ToText | 100.000 "<" | 1262,63 | 5,58 | sì |
| Prepare | attributo title da 1 MB, scuro | 8,26 | 7,38 | sì |
| Prepare | attributo title da 1 MB, chiaro | 7,62 | 7,25 | sì |
| ToText | attributo title da 1 MB | 4,22 | 4,14 | sì |
| Prepare | attributo style da 1 MB, scuro | 202,28 | 183,95 | sì |
| Prepare | attributo style da 1 MB, chiaro | 8,71 | 7,78 | sì |
| ToText | attributo style da 1 MB | 4,26 | 4,04 | sì |
| Prepare | 5000 &lt;div> non chiusi, scuro | 216,80 | 146,63 | sì |
| Prepare | 5000 &lt;div> non chiusi, chiaro | 141,10 | 141,64 | sì |
| ToText | 5000 &lt;div> non chiusi | 486,31 | 137,13 | sì |
| Prepare | 20.000 &lt;div> non chiusi, scuro | 7689,35 | 3950,42 | sì |
| Prepare | 20.000 &lt;div> non chiusi, chiaro | 3763,21 | 3660,76 | sì |
| ToText | 20.000 &lt;div> non chiusi | 19507,21 | 3925,33 | sì |
| Prepare | 20.000 &lt;b>&lt;i> non chiusi, scuro | 38523,61 | 5489,21 | sì |
| Prepare | 20.000 &lt;b>&lt;i> non chiusi, chiaro | 5622,69 | 5486,82 | sì |
| ToText | 20.000 &lt;b>&lt;i> non chiusi | 21690,27 | 5594,49 | sì |
| Prepare | "&lt;meta " ripetuto 10.000 volte (60 KB), scuro | 1300,61 | 3,59 | sì |
| Prepare | "&lt;meta " ripetuto 10.000 volte (60 KB), chiaro | 1,47 | 1,62 | sì |
| ToText | "&lt;meta " ripetuto 10.000 volte (60 KB) | 1,33 | 1,17 | sì |
| Prepare | style con "url(" ripetuto 10.000 volte (40 KB), scuro | 6767,15 | 4,13 | sì |
| Prepare | style con "url(" ripetuto 10.000 volte (40 KB), chiaro | 6726,24 | 0,93 | sì |
| ToText | style con "url(" ripetuto 10.000 volte (40 KB) | 0,28 | 0,27 | sì |
| Prepare | src con "pixel" ripetuto 20.000 volte, scuro | 656,38 | 1,98 | sì |
| Prepare | src con "pixel" ripetuto 20.000 volte, chiaro | 831,92 | 1,59 | sì |
| ToText | src con "pixel" ripetuto 20.000 volte | 1,22 | 1,23 | sì |
| Prepare | attributo con url( e 30.000 spazi, scuro | 1825,27 | 0,73 | sì |
| Prepare | attributo con url( e 30.000 spazi, chiaro | 1787,38 | 0,46 | sì |
| ToText | attributo con url( e 30.000 spazi | 0,24 | 0,23 | sì |
| Prepare | style con parola da 30.000 lettere e una \, scuro | 2446,94 | 0,51 | sì |
| Prepare | style con parola da 30.000 lettere e una \, chiaro | 2460,69 | 0,45 | sì |
| ToText | style con parola da 30.000 lettere e una \ | 0,26 | 0,17 | sì |

### A.7 Profondità e stack: quando il processo si chiude

Ogni prova in un processo a parte (`profondo N KB modo`), su un thread creato con lo stack indicato; "overflow" vuol dire processo chiuso da .NET con "Stack overflow" (codice 134 su Linux), con la traccia che ripete `AngleSharp.Dom.Element.AppendText` sotto `Element.get_TextContent`, chiamato da `MailHtml.ToText`.

| funzione e ingresso | profondità | stack 1 MB | stack 1,5 MB | stack 8 MB |
|---|---|---|---|---|
| `ToText`, N `<div>x` non chiusi | 3.000 | finisce, 1,04 s | finisce, 1,02 s | finisce, 1,03 s |
| | 5.000 | finisce, 1,46 s | finisce, 1,95 s | finisce, 1,50 s |
| | 8.000 | overflow | finisce, 3,78 s | finisce, 3,79 s |
| | 12.000 | overflow | overflow | finisce, 10,5 s |
| | 16.000 | overflow | overflow | finisce, 20,5 s |
| `Prepare` chiaro, un link con N `<span>x` | 5.000 | finisce, 0,23 s | finisce, 0,23 s | |
| | 8.000 | overflow | finisce, 0,24 s | |
| | 12.000 | overflow | overflow | |
| copia corretta: `ToText`, N `<div>x` | 8.000 / 20.000 | finisce, 1,03 s / 3,92 s | | |
| copia corretta: `Prepare` chiaro, link con N `<span>x` | 8.000 / 20.000 | finisce, 0,26 s / 0,28 s | | |
| `Prepare` scuro, N `<div>x` (nessun link) | 1.000 | finisce, 0,26 s | finisce, 0,23 s | finisce, 0,29 s |
| | 5.000 | finisce, 0,78 s | finisce, 0,83 s | finisce, 0,83 s |
| | 20.000 | finisce, 9,9 s | finisce, 11,2 s | finisce, 11,1 s |
| | 50.000 | finisce, 49,5 s | finisce, 49,5 s | finisce, 49,4 s |
| solo analisi di AngleSharp, N `<div>x` | 5.000 / 20.000 | 0,70 s / 4,16 s | | |
| `Prepare` chiaro, N `<div>x` | 5.000 / 20.000 | 0,75 s / 4,36 s | | |

Le soglie dipendono dalla dimensione delle cornici dello stack, che su Windows con lo stesso JIT dovrebbe essere simile, e dallo stack dei thread di Missiva (thread principale e pool), che va controllato sul PC (V-01, verifica). La serializzazione di AngleSharp (`InnerHtml`, usata da `Prepare`) non va in overflow neanche a 50.000 livelli con 1 MB, e `Descendants` (usata da `ToText` prima di `TextContent`) passa a 8.000 livelli con 1 MB: il problema è `TextContent`.

### A.8 AngleSharp e i "<" che non aprono un tag

Progetto a parte con la sola AngleSharp (1.8.2, poi 1.8.3), `new HtmlParser().ParseDocument(html)` su `<html><body><p>` più il testo ripetuto; un giro dopo due di riscaldamento, tempo e allocazioni del thread.

| testo ripetuto nel `<p>` | 5.000 volte | 10.000 | 20.000 | 40.000 |
|---|---|---|---|---|
| "<" | 5,6 ms, 24 MB | 16,0 ms, 96 MB | 52,2 ms, 383 MB | 151,9 ms, 1.528 MB |
| "< " | 5,6 ms, 48 MB | 22,4 ms, 191 MB | 77,7 ms, 764 MB | 552,6 ms, 3.054 MB |
| "<1" | 8,5 ms, 48 MB | 28,1 ms, 191 MB | 78,7 ms, 764 MB | 550,8 ms, 3.054 MB |
| "a < b " | 23,1 ms, 144 MB | 103,9 ms, 573 MB | 578,3 ms, 2.291 MB | 2.266,7 ms, 9.172 MB |
| "x <= y " | 20,4 ms, 167 MB | 110,7 ms, 669 MB | 650,2 ms, 2.676 MB | 2.969,3 ms, 10.694 MB |
| "a&lt;b>" (tag non chiusi) | 116,6 ms, 2,5 MB | 87,7 ms, 5,1 MB | 632,8 ms, 10,1 MB | 6.636,7 ms, 20,1 MB |
| "<" dentro `<style>` | 5,1 ms, 24 MB | 15,6 ms, 96 MB | 35,7 ms, 383 MB | 146,0 ms, 1.528 MB |
| "<" dentro un attributo | 0,2 ms, 0,1 MB | 0,1 ms, 0,1 MB | 0,2 ms, 0,1 MB | 0,3 ms, 0,2 MB |
| "&amp;lt;" al posto di "<" | 1,0 ms, 0,1 MB | 1,6 ms, 0,2 MB | 3,0 ms, 0,3 MB | 5,9 ms, 0,6 MB |

Con AngleSharp 1.8.3 (l'ultima versione su NuGet il 3 ottobre 2026) le stesse righe danno gli stessi ordini di grandezza (per esempio "a < b " a 40.000: 2.219,8 ms e 9.170 MB; "x <= y ": 2.655,0 ms e 10.695 MB): aggiornare il pacchetto non basta.

### A.9 Compilazione di Missiva su Linux (facoltativo)

`dotnet build Missiva.csproj` (Debug, SDK 10.0.112, su una copia del commit fuori dal repository): compila, 0 errori, 24 avvisi, cioè 12 avvisi diversi ripetuti dalle due compilazioni di WPF (il progetto temporaneo del markup e quello vero): 6 CS8602 (in Core/ProvaBozze.cs e Core/GmailProvider.cs), 3 CS8604 (UI/MainWindow.xaml.cs e Core/GmailProvider.cs), 1 CS0649 (Core/MailDns.cs), 1 CS0252 (Core/ProvaBozze.ContattiGoogle.cs), 1 CA2200 (UI/ReadingView.xaml.cs:663). Come dice CLAUDE.md, non dimostra nulla sul comportamento.

## Parte B: punti trovati

In ordine di sigla, che segue la gravità: Alta da V-01 a V-06, poi Media (con qualche Bassa-Media), poi Bassa da V-39 a V-44. Ogni punto ha Dove, Scenario, Gravità, Stato, Proposta e Verifica su Windows.

### V-01 Un'email con molti elementi annidati chiude Missiva (overflow dello stack nel testo dell'email)

- Dove: Core/MailHtml.cs:197 (`ToText`: `dom.Body?.TextContent`) e Core/MailHtml.cs:124 (`Prepare`: `e.TextContent` di ogni link). In AngleSharp 1.8.2 `Element.TextContent` è ricorsivo (`Element.AppendText`, una chiamata per livello: lo dice la traccia dell'overflow). Chiamanti di `ToText` e `ReadableText`: UI/ComposeWindow.xaml.cs:797 (`ShowOriginalAsync`: Rispondi e Inoltra, sul thread dell'interfaccia), Core/Rules.cs:383 (regole di Missiva con "il testo contiene", per ogni messaggio arrivato), Core/GmailProvider.cs:1427 e Core/ImapProvider.cs:1501 (parte di testo della risposta, a ogni salvataggio della bozza), Core/EmlProvider.cs:120, UI/MainWindow.xaml.cs:3523 (Rispondi con riunione). `Prepare`: UI/ReadingPage.cs:424, da `Warm` in `Task.Run` (UI/ReadingView.xaml.cs:682) o direttamente nel disegno (UI/ReadingPage.cs:434).
- Scenario: un'email con qualche migliaio di tag aperti e mai chiusi (8.000 `<div>x` sono 48 KB). Rispondi, Inoltra, una regola sul testo o il salvataggio della risposta arrivano a `ToText`, lo stack finisce e il processo si chiude: in .NET un overflow dello stack non si intercetta, quindi niente `catch`, niente riga nel registro, bozze aperte perse. Con gli stessi elementi dentro un link (`<a href=...>` seguito da migliaia di `<span>`) la chiusura arriva già all'apertura del messaggio nel riquadro, da `Prepare`.
- Gravità: Alta. Chiusura del processo con un'email di 50 KB che chiunque può mandare; se c'è una regola sul testo, all'arrivo, senza nessun clic.
- Stato: MISURATO qui. Ogni prova in un processo a parte, su un thread con lo stack indicato (`profondo N KB modo` nel banco):

  | prova | stack 1 MB | stack 1,5 MB | stack 8 MB |
  |---|---|---|---|
  | `ToText`, N `<div>x` non chiusi | 5.000 finisce (1,46 s); 8.000 overflow | 8.000 finisce (3,8 s); 12.000 overflow | 16.000 finisce (20,5 s) |
  | `Prepare` chiaro, link con N `<span>x` | 5.000 finisce (0,23 s); 8.000 overflow | 8.000 finisce (0,24 s); 12.000 overflow | |
  | `Prepare` scuro, N `<div>x` (senza link) | 50.000 finisce (49,5 s) | 50.000 finisce (49,5 s) | 50.000 finisce (49,4 s) |

  Su Windows le soglie sono SOSPETTO: dipendono dallo stack del thread principale e dei thread del pool (di solito 1-1,5 MB) e dalla grandezza delle cornici del JIT, che dovrebbe essere simile; vanno confermate con la prova qui sotto.
- Proposta: il testo senza ricorsione. `TextOf` (pila esplicita al posto di `TextContent`) per i link, e `ToText` riscritto come una sola visita con una pila che produce gli stessi spazi e a capo (codice in appendice, file `mailhtmlfix.patch`). MISURATO con la copia corretta: uscita identica carattere per carattere su tutti gli ingressi del banco, e con uno stack da 1 MB la copia corretta finisce dove l'originale chiude il processo (`ToText` con 8.000 e 20.000 `<div>` in 1,0 e 3,9 s; il link con 8.000 e 20.000 `<span>` in 0,26 e 0,28 s, A.7); `ToText` di 20.000 `<div>` 3,9 s invece di 19,5 s. Insieme, il limite di profondità di V-03.
- Verifica su Windows: un .eml di prova scritto dal banco (codice in appendice, `ScriviEml`), aperto nella versione di prova con `--eml profondo.eml`, poi Rispondi: oggi ci si aspetta la chiusura (nel Visualizzatore eventi, applicazione chiusa con 0xC00000FD), dopo la correzione la finestra di risposta con la citazione. Per il caso del link: `Missiva.exe --page profondo-link.html pagina.html` (Debug), che chiama `Prepare`.

### V-02 AngleSharp: ogni "<" che non apre un tag ricopia il testo accumulato (tempo e memoria quadratici)

- Dove: Core/MailHtml.cs:48 (`Prepare`) e 190 (`ToText`): `new HtmlParser().ParseDocument(html)` con AngleSharp 1.8.2 (Missiva.csproj). Lo stesso con 1.8.3, l'ultima versione su NuGet il 3 ottobre 2026 (MISURATO).
- Scenario: un'email con molti "<" nel testo che non aprono un tag: "x <= y", "a < b", "<<", frecce "<-", codice incollato in un HTML scritto male, o un'email costruita apposta. Per ogni "<" AngleSharp alloca in proporzione al testo che lo precede nello stesso nodo: tempo e memoria crescono col quadrato. L'analisi gira nel pool (`Warm`), ma i gigabyte allocati fanno partire raccolte che fermano tutti i thread, compreso quello dell'interfaccia; e quando la pulizia avviene nel disegno (sole, Mostra immagini, cambio di tema: V-19) blocca direttamente l'interfaccia.
- Gravità: Alta per queste email: un'email di 100 KB ferma la pulizia per quasi 2 s e alloca 9,5 GB.
- Stato: MISURATO (solo l'analisi di AngleSharp, una volta per lunghezza):

  | testo nel corpo | 5.000 volte | 10.000 | 20.000 | 40.000 |
  |---|---|---|---|---|
  | "a < b " | 23 ms, 144 MB | 104 ms, 573 MB | 578 ms, 2,3 GB | 2,27 s, 9,2 GB |
  | "x <= y " | 20 ms, 167 MB | 111 ms, 669 MB | 650 ms, 2,7 GB | 2,97 s, 10,7 GB |
  | "<" | 6 ms, 24 MB | 16 ms, 96 MB | 52 ms, 383 MB | 152 ms, 1,5 GB |
  | "&amp;lt;" al posto di "<" | 1,0 ms, 0,1 MB | 1,6 ms, 0,2 MB | 3,0 ms, 0,3 MB | 5,9 ms, 0,6 MB |
  | "<" in un attributo | 0,2 ms | 0,1 ms | 0,2 ms | 0,3 ms |

  Nel banco completo, 100.000 "<" (98 KB): analisi 1,79 s, `Prepare` 1,73-1,81 s, 9,5 GB allocati per giro.
- Proposta: prima dell'analisi, ogni "<" che non è seguito da una lettera ASCII, "/", "!" o "?" diventa "&amp;lt;", fuori dai blocchi `<style>`: per l'HTML5 è lo stesso testo (il tokenizer lo emetterebbe come carattere), dentro un attributo l'entità torna "<", dentro un commento resta un commento. Funzione `EscapeStrayLt` in `mailhtmlfix.patch` (una passata, nessuna allocazione se non ci sono "<" sciolti). MISURATO: 100.000 "<" da 1,73-1,81 s a 5-8 ms, uscita identica; nessuna differenza misurabile sulle newsletter normali (A.6). Segnalare il difetto ad AngleSharp.
- Verifica su Windows: `Missiva.exe --page` con un HTML di 40.000 "a < b " prima e dopo, con la riga "tempi: HTML pulito in sottofondo"; dotnet-counters (gc-heap-size, gen-0-gc-count) aprendo lo stesso messaggio come .eml.

### V-03 Annidamento profondo: costo quadratico in AngleSharp e nel tema scuro di Missiva

- Dove: AngleSharp (l'algoritmo dell'HTML5 controlla gli elementi aperti a ogni tag: costo proporzionale alla profondità); Core/MailHtml.cs:71 (tema scuro: per ogni elemento si risale fino alla radice cercando un antenato "vivace", `for (var p = e; ...; p = p.ParentElement) keepText = vivid.Contains(p)`); Core/MailHtml.cs:193 (`ToText`: `Closest("pre")` per ogni nodo di testo, anche lui fino alla radice).
- Scenario: email con tag non chiusi o tabelle annidate in profondità (costruita apposta, o generata male). Nel tema scuro il costo di Missiva supera quello di AngleSharp.
- Gravità: Alta per queste email (decine di secondi con 137 KB); Bassa per le email normali (le tabelle annidate delle newsletter vere sono 5-15 livelli).
- Stato: MISURATO (A.1 e A.6):

  | ingresso | analisi | Prepare scuro | Prepare chiaro | Prepare scuro corretto |
  |---|---|---|---|---|
  | 200 tabelle annidate (37 KB) | 1,46 ms | 7,43 ms | 2,40 ms | 4,0 ms |
  | 1000 tabelle annidate (185 KB) | 9,4 ms | 93 ms | 17,4 ms | 25,6 ms |
  | 5.000 `<div>` non chiusi (29 KB) | 140 ms | 217 ms | 141 ms | 147 ms |
  | 20.000 `<div>` non chiusi (117 KB) | 4,1 s | 7,7 s | 3,8 s | 3,95 s |
  | 20.000 `<b><i>` non chiusi (137 KB) | 5,6 s | 38,5 s | 5,6 s | 5,5 s |

- Proposta: (a) nel tema scuro, sapere se un antenato è vivace senza risalire: `dom.All` è in ordine di documento, quindi basta un `HashSet<IElement> keepSet` con l'elemento se è vivace o se lo è il genitore (due righe in `mailhtmlfix.patch`; MISURATO: uscita identica, tempi nella colonna "corretto"); (b) `ToText` con la pila di V-01, che porta con sé "dentro un pre" invece di `Closest`; (c) contro il costo di AngleSharp, un limite di profondità prima dell'analisi: una passata lineare che conta i tag aperti e chiusi (fuori da commenti e `<style>`), e oltre 1.000 livelli il messaggio si mostra come testo semplice con una fascia ("Messaggio troppo complesso: mostrato come testo"). Il parser di Blink, il motore di WebView2, oltre 512 livelli attacca i nodi come fratelli invece che come figli (`kMaximumHTMLParserDOMTreeDepth = 512` in third_party/blink/renderer/core/html/parser/html_construction_site.h, letto nel sorgente di Chromium): oltre quella profondità il riquadro già oggi non rispetta l'annidamento, quindi un limite di Missiva intorno a 1.000 livelli tocca solo email che già oggi non si vedono come sono scritte. (c) non è misurato.
- Verifica su Windows: `Missiva.exe --page` sugli HTML del banco (`Nested(1000)`, 20.000 `<div>x`) in tema scuro e chiaro, riga "tempi: HTML pulito in sottofondo"; StallLog con il sole premuto sullo stesso messaggio.

### V-04 Espressioni regolari quadratiche su fogli di stile e attributi: un'email di 40 KB ferma la pulizia per secondi

- Dove: Core/MailHtml.cs:375 (`CssUrl`), 389 (`CssEscapedFunction`, solo se lo stile contiene "\"), 353 (`RemoteUrlFunction`, su ogni attributo), 316 (`OwnDarkMode`, su tutto l'HTML, solo nel tema scuro), 313 (`SpacerName`, sull'indirizzo delle immagini bloccate), 426 (`ColorToken`, nei valori dei colori, tema scuro). Tutte con il motore che torna indietro (`[GeneratedRegex]` senza `NonBacktracking`).
- Scenario: per ognuna basta una ripetizione senza chiusura: "url(" ripetuto senza ")" in uno stile, "url(" seguito da molti spazi in un attributo, una parola lunga e una "\" in uno stile, "&lt;meta " ripetuto senza ">". Ogni posizione di partenza rilegge fino in fondo. L'email non deve essere grande.
- Gravità: Alta: secondi o decine di secondi con poche decine di KB, sia nel pool (`Warm`) sia sul thread dell'interfaccia quando la pulizia avviene nel disegno (V-19).
- Stato: MISURATO. Le regex da sole (A.4, tempi a 32.000 e 64.000 caratteri): `CssUrl` 4,6 s a 32.000 caratteri; `RemoteUrlFunction` 8,4 s a 64.000; `CssEscapedFunction` 11,3 s a 64.000; `OwnDarkMode` 1,5 s a 64.000 e 6,0 s a 128.000; `SpacerName` 1,1 s a 128.000 e 5,2 s a 256.000; `ColorToken` 219 ms a 256.000. Le versioni proposte restano lineari: a 256.000 caratteri fra 0,7 e 18 ms. Dentro `Prepare` (A.1): "url(" ripetuto 10.000 volte in uno stile (40 KB) 6,8 s nel tema scuro e 6,7 s nel chiaro; "&lt;meta " ripetuto 10.000 volte (60 KB) 1,30 s nel tema scuro e 1,5 ms nel chiaro; "pixel" ripetuto 20.000 volte nell'indirizzo di un'immagine (100 KB) 0,66 s nel tema scuro e 0,83 s nel chiaro; "url(" e 30.000 spazi in un attributo (29 KB) 1,8 s; una parola di 30.000 lettere e una "\" in uno stile (29 KB) 2,4-2,5 s.
- Proposta (in `mailhtmlfix.patch`): `CssUrl` riscritta senza riferimento all'indietro, con le tre forme separate e la forma senza virgolette che si ferma a una parentesi aperta: `url\(\s*(?:'(?<url>[^']*)'|"(?<url>[^"]*)"|(?<url>[^'"() \t\r\n\f]*))\s*\)` (gli spazi del CSS scritti per esteso: con `\s` dentro la classe il motore non usa la ricerca vettoriale, e un `url()` da 1 MB costava 9,1 ms invece di 0,10; l'originale 1,8 ms: MISURATO); `RemoteUrlFunction` come `url\([\s'"]*(?:https?:)?//` (più larga: blocca anche con più virgolette); `ColorToken` con il ramo `url\(` che si ferma a una parentesi aperta (`url\([^()]*(?:\([^()]*\)[^()]*)*\)`, `rgba?\([^()]*\)`, `hsla?\([^()]*\)`); `SpacerName`, `OwnDarkMode` e `CssEscapedFunction` con `RegexOptions.NonBacktracking`. MISURATO: tutte lineari sugli ingressi ostili (A.4, righe "proposta"), uscita di `Prepare` identica su tutti gli ingressi del banco, e i casi sopra da secondi a millisecondi (A.6). In più `CssUrl` riscritta blocca un `url('...')` fra virgolette con parentesi nell'indirizzo, che oggi passa (vedi "Fuori tema").
- Verifica su Windows: `Missiva.exe --page` sugli HTML del banco, prima e dopo, con "tempi: HTML pulito in sottofondo"; il banco stesso sotto Windows (`dotnet run -c Release -- regex`).

### V-05 Segna letto o da leggere su molti messaggi: icona, area di notifica e registro rifatti per ogni messaggio

- Dove: UI/MainWindow.xaml.cs:3233-3241 (`SetReadAsync`, ciclo per messaggio con `where.Unread += ...` e una scansione di `rows.Rows` per messaggio), UI/ViewModels.cs:558-562 (setter di `FolderItem.Unread`, 3 notifiche più `InboxUnreadChanged`), UI/MainWindow.xaml.cs:1215-1218 (`OnInboxChanged`), 1226-1237 (`UpdateBadge`: `UpdateUnified`, `UpdateTrayTip`, `BadgeImage` con un RenderTargetBitmap, `TaskbarItemInfo.Overlay` e `Description`, `Log.Write`), 1240-1257 (`BadgeImage`). Lo stesso percorso, una volta, a ogni messaggio non letto aperto: UI/ReadingView.xaml.cs:311 (`Render` fino a `Navigate`) e 319 (`MarkRead`), poi `OnReaderRead` (UI/MainWindow.xaml.cs:2780-2785), tutto nello stesso blocco sincrono, prima che il Dispatcher serva la pagina a WebView2.
- Scenario: Ctrl+A nella Posta in arrivo con alcune pagine caricate, poi Ctrl+Q (o Ctrl+U, o la busta). Per ognuno dei 500 messaggi cambia il conteggio, quindi si rifanno icona sovrapposta (WPF crea un secondo bitmap e un'icona per `SetOverlayIcon`), descrizione della miniatura, suggerimento dell'area di notifica (Shell_NotifyIcon) e una riga "numero sull'icona" nel registro, che apre e chiude il file (V-39). La riga dell'elenco si cerca con una scansione lineare per messaggio (500 x 2000 confronti).
- Gravità: Alta per le azioni su molti messaggi. Stima 1-6 ms per messaggio, cioè 0,5-3 s di interfaccia ferma per 500 messaggi; lineare nei messaggi. Media per il singolo clic su un non letto (stima 2-15 ms fra `Navigate` e la pagina servita).
- Stato: CONFERMATO il percorso (righe citate). SOSPETTO il costo per chiamata.
- Proposta: (a) in `SetReadAsync` un solo `Unread +=` per cartella con il totale (`mine.GroupBy(m => m.FolderId)`); (b) le righe con un `ToLookup(r => r.Summary.Id)` costruito una volta per account; (c) `UpdateBadge` accorpato: un flag `badgeQueued` e `Dispatcher.BeginInvoke(DispatcherPriority.Background, UpdateBadgeNow)`, così N cambi danno un solo aggiornamento, dopo il disegno; bitmap tenute per testo ("1".."99+"); `Log.Write` solo nella prova; (d) in `ShowAsync` chiamare `MarkRead` e `bodyShown` dopo `RevealAsync` della pagina, o in `BeginInvoke` a priorità Background.
- Verifica su Windows: con MISSIVA_NO_READ il ciclo non si raggiunge (UI/MainWindow.xaml.cs:3222-3230); va usata la Debug con `--senza-rete` (le azioni vanno in coda e al ritorno passano da TestingProvider). Posta in arrivo con 4-5 pagine, Ctrl+A, Ctrl+Q: contare le righe "numero sull'icona" (oggi una per messaggio) e leggere la riga di StallLog "interfaccia lenta: blocco di N ms, Input ...". Per il clic singolo: "clic N" su non letti e letti, righe "clic, testo pronto" e "pagina mostrata", con una variante della prova che non esca a UI/ReadingView.xaml.cs:758-764.

### V-06 Account IMAP: ogni salvataggio automatico riscarica e ricarica tutti gli allegati della bozza

- Dove: Core/ImapProvider.cs:1440-1450 (`SaveDraftAsync` restituisce gli allegati della bozza senza `Content`), 1520-1521 (`BuildAsync`: `AttachmentAsync` per ogni allegato tenuto), 1130-1132 (`AttachmentAsync` scarica dal server se `Content` è null); UI/ComposeWindow.xaml.cs:509-511 (`Heavy` vale solo per Gmail).
- Scenario: account IMAP, risposta con un PDF da 10 MB. Dopo il primo salvataggio, ogni salvataggio automatico (10 s dopo l'ultima lettera, o ogni minuto mentre si scrive) scarica 10 MB dalle Bozze e ne rimanda circa 13,5 MB (base64) con APPEND. Invia, se trova un salvataggio in corso, lo aspetta. Gmail tiene in memoria gli allegati sotto 5 MB (Core/GmailProvider.cs:1309-1320, 1346-1351) e salva più di rado oltre 5 MB.
- Gravità: Alta per chi usa un account IMAP con allegati: circa 25 MB di traffico per salvataggio con 10 MB di allegati (calcolato, non misurato); con una linea lenta in uscita un salvataggio dura decine di secondi e ritarda Invia. Lineare nella dimensione degli allegati e nel numero di salvataggi.
- Stato: CONFERMATO (righe citate).
- Proposta: copiare in `ImapProvider.SaveDraftAsync` la logica di Gmail che tiene gli allegati sotto 5 MB in `Attachment.Content`; applicare `Heavy` a tutti i provider che riscrivono la bozza intera (Gmail e IMAP, cioè tutti tranne Graph); cadenza legata alla durata dell'ultimo salvataggio: `now - lastAutoSave >= max(intervallo, 5 x durataUltimoSalvataggio)`.
- Verifica su Windows: in `--prova-bozze`, con il server IMAP finto, due salvataggi di una bozza con un allegato e al secondo nessuna FETCH della parte dell'allegato; poi le righe "tempi: bozza salvata da sola N ms" con `--imap-finto` su una copia dei dati con il solo account finto.

### V-07 Tema scuro: una FormatException per ogni parola che non è un colore

- Dove: Core/MailHtml.cs:480-499 (`ParseColor`: per un nome chiama `ColorConverter.ConvertFromString`, che per una parola sconosciuta lancia FormatException, presa a 495-498); le parole arrivano da `ColorToken` (426, ramo `[a-zA-Z]+`) attraverso `Recolor` (472-478) e `IsVivid` (458-464), chiamati per ogni stile nel tema scuro (70, 156-157, 416, 433-444, 453).
- Scenario: ogni newsletter aperta nel tema scuro: "px" di "1px", "solid", "none", "no-repeat", "center", "top", "important" sono tutte eccezioni. Succede nel pool (`Warm`), ma è sul percorso fra il corpo arrivato e il disegno; e sul thread dell'interfaccia quando la pulizia avviene nel disegno (V-19).
- Gravità: Media per le email con molti stili (fogli lunghi, tanti `border` e `background` scritti per esteso nei tag), dove le eccezioni sono la parte più cara della pulizia; sulle newsletter generate qui il guadagno resta dentro il rumore fra due esecuzioni. Cresce con gli stili dell'email.
- Stato: MISURATO (A.1; il banco usa un ColorConverter finto che si comporta come quello di WPF, con la stessa eccezione: vedi appendice). Eccezioni per giro e tempo scuro contro chiaro: newsletter 50 KB 146 eccezioni, 3,36 contro 1,91 ms; 200 KB 348, 11,6 contro 7,2 ms; 1 MB 1.456, 61 contro 41 ms; 5.000 stili negli elementi 10.000, 126 contro 50 ms; `<style>` con 2.000 regole 24.000, 84,6 contro 4,1 ms. Il costo di un'eccezione su Windows può essere diverso (SOSPETTO), ma .NET 10 usa la stessa gestione delle eccezioni su tutti e due i sistemi.
- Proposta: in `ParseColor`, prima di `ConvertFromString`, restituire null per ciò che non può essere un colore: "#" con 4, 5, 7 o 9 caratteri tutti esadecimali, "rgb..." chiuso da ")", oppure un nome in un `HashSet` dei nomi di colore costruito una volta (in Missiva dai nomi di `System.Windows.Media.Colors`, `typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)`). MISURATO con la copia corretta: zero eccezioni, uscita identica, eccezioni sempre a zero; `<style>` con 2.000 regole 14,9 ms invece di 84,6, 5.000 stili negli elementi 91 invece di 126; newsletter da 50 KB a 5 MB 2,8, 10,3, 49 e 395 ms invece di 3,4, 11,6, 61 e 446, differenze che però stanno dentro il rumore fra due esecuzioni (la colonna corretta viene da un'altra esecuzione, A.6). In più una cache `Dictionary<(string, Role), string>` per `Recolor` (i valori si ripetono moltissimo nelle newsletter): non misurata.
- Verifica su Windows: dotnet-counters (exception-count) aprendo 10 newsletter nel tema scuro; "tempi: HTML pulito in sottofondo" chiaro contro scuro, prima e dopo.

### V-08 Regole di Missiva: il testo del messaggio si ricalcola per ogni regola, sul thread dell'interfaccia

- Dove: Core/Rules.cs:434-446 (`RunAsync`: per ogni messaggio e per ogni regola `Matches`), 380-384 (`Matches`: `MailHtml.ReadableText(body.Html)` per ogni regola con "il testo contiene"); chiamato da UI/App.cs:716 (controllo dei nuovi messaggi) e da UI/RulesView.cs:474 (Esegui ora, fino a 500 messaggi). Dopo gli `await` dei provider si è sul thread dell'interfaccia (V-17).
- Scenario: tre regole sul testo e 30 newsletter arrivate di notte: 90 analisi AngleSharp complete sul thread dell'interfaccia al primo controllo; "Esegui ora" su 500 messaggi con due regole sul testo: 1.000.
- Gravità: Media. Il costo di una `ReadableText` è MISURATO qui (A.2): 5,0 ms per una newsletter da 200 KB, 31 ms per 1 MB; moltiplicato per regole e messaggi. Più il rischio di V-01 all'arrivo.
- Stato: CONFERMATO il ricalcolo per regola (righe citate); MISURATO il costo di una chiamata (su Linux).
- Proposta: in `RunAsync` il testo una volta per messaggio (`string? text = null; text ??= ...`), passato a `Matches`; calcolato in `Task.Run`.
- Verifica su Windows: in `--prova-bozze` (`RulesAsync`) un contatore di `ReadableText`; StallLog durante "Esegui ora" su una cartella con newsletter.

### V-09 Rispondi e Inoltra: il testo dell'originale si calcola sul thread dell'interfaccia, e `ToText` è quadratico sulle righe con `<br>`

- Dove: UI/ComposeWindow.xaml.cs:775-800 (`ShowOriginalAsync`: dopo `await provider.GetBodyAsync`, `OriginalText.Text = MailHtml.ReadableText(originalBody.Html)` a 797, sul thread dell'interfaccia); lo stesso in UI/MainWindow.xaml.cs:3523 (Rispondi con riunione).
- Scenario: Rispondi a una newsletter, o a un'email di testo lunga (riepiloghi, registri, liste di discussione) scritta come righe separate da `<br>` nello stesso elemento: la finestra della risposta resta ferma mentre AngleSharp analizza tutto il corpo e `ToText` sostituisce ogni `<br>` con un nodo di testo (Core/MailHtml.cs:194, `br.Replace`): ogni sostituzione cerca il posto del nodo fra i fratelli, quindi N righe nello stesso elemento costano N². Con un HTML profondo, la chiusura di V-01.
- Gravità: Media (una volta per risposta, e a ogni salvataggio delle risposte di Gmail e IMAP, V-28). MISURATO qui: `ReadableText` 5,0 ms per una newsletter da 200 KB, 31 ms per 1 MB, 185 ms per 5 MB; la sola sostituzione dei `<br>` con N righe in un `<div>` 26 ms per 2.000 righe (33 KB), 55 ms per 4.000, 212 ms per 8.000 (132 KB), 820 ms per 16.000 (265 KB), quadratica (progetto a parte, in appendice); `ToText` intero su 5.000 righe con `<br>` e uno `<span>` con stile ciascuna (629 KB) 262 ms, contro 13 ms della versione con la pila.
- Stato: CONFERMATO il thread; MISURATO il costo (su Linux).
- Proposta: `OriginalText.Text = await Task.Run(() => MailHtml.ReadableText(originalBody.Html));` (e lo stesso a 3523), più `ToText` con la pila di V-01, che non modifica il documento e quindi non ha il costo dei `<br>` (MISURATO in A.6: uscita identica).
- Verifica su Windows: StallLog "interfaccia lenta: blocco ..." premendo Rispondi su una newsletter grande; `--rispondi N` in Debug.

### V-10 Store.Save: tutte le impostazioni serializzate e scritte sul thread dell'interfaccia, a ogni piccolo cambio

- Dove: Core/Store.cs:477-488 (`Save`: `JsonSerializer.Serialize` di tutto `Settings` con rientri, Core/Store.cs:444; `File.WriteAllText` e `File.Move`, sotto `lock (saving)`). 83 chiamate fuori dalle prove, tutte sul thread dell'interfaccia (nessun `ConfigureAwait(false)` nella catena). Le più frequenti: UI/ReadingView.xaml.cs:609 (`OnZoomFactorChanged`, una per tacca di Ctrl+rotellina), UI/ViewModels.cs:36 (`AccountItem.IsExpanded`, freccia dell'account), UI/ViewModels.cs:334 (apri e chiudi le sottocartelle), UI/MainWindow.Unified.cs:70, UI/MainWindow.xaml.cs:1616 (Preferiti), UI/MainWindow.xaml.cs:4183 (ogni ricerca, `RememberSearch`), UI/MainWindow.xaml.cs:3619 (`SnoozeAsync`: una per messaggio posposto, dentro il ciclo), UI/MainWindow.xaml.cs:3705 (`CheckSnoozedAsync`: una per messaggio tornato), Core/Rules.cs:581 (`ApplyToNewAsync`, a ogni controllo con messaggi arrivati e regole di Missiva attive), UI/SettingsWindow.cs:668 e 935 (modelli e firme, 500 ms dopo l'ultima lettera, con l'RTF intero rigenerato da `RichEditor.Rtf`, UI/RichEditor.cs:38-46).
- Scenario: Ctrl+rotellina nel riquadro (cinque tacche, cinque salvataggi completi e cinque righe di registro); posporre 30 messaggi; un modello con un logo scritto a pezzi (a ogni pausa RTF intero, DPAPI dell'RTF e file intero). Il file cresce con contrassegni, posposti, firme e modelli con immagini e con `RulesSeen` (2000 impronte, una per riga con i rientri).
- Gravità: Media con impostazioni grandi, Bassa con quelle piccole. Stima: 0,5-2 ms con 100 KB, 5-15 ms con 1,5 MB (serializzazione, scrittura, rinomina, più l'eventuale antivirus alla chiusura del file); lineare nella dimensione del file, moltiplicata per il numero di chiamate.
- Stato: CONFERMATO struttura, thread e frequenza. SOSPETTO la durata: dipende dalla dimensione di impostazioni.json sul PC dell'autore.
- Proposta: `Store.SaveSoon()` per i percorsi frequenti: segna "da salvare" e fa partire un DispatcherTimer da 300-500 ms; allo scatto serializza sul thread dell'interfaccia con `JsonSerializer.SerializeToUtf8Bytes` (Settings cambia solo lì) e scrive in `Task.Run` con un solo scrittore. `Store.Save()` sincrono resta per account, token, uscita e fine sessione. Lo zoom si salva 500 ms dopo l'ultimo cambio. `RulesSeen` come HashSet in memoria (oggi `seen.Contains` su una List di 2000 voci per ogni messaggio arrivato, Core/Rules.cs:577). Nelle firme e nei modelli non salvare se l'RTF è uguale a quello di prima.
- Verifica su Windows: un `Log.Time` in `Store.Save` con i KB e i tempi di serializzazione, scrittura e rinomina; copia delle impostazioni gonfiata (500 contrassegni finti, 3 firme e 3 modelli con un logo); 20 clic sulla freccia dell'account, dieci tacche di Ctrl+rotellina, 30 posposti (solo nel registro): righe "interfaccia lenta" di StallLog.

### V-11 Avvio: una decifratura DPAPI per ogni testo protetto, contrassegni mai potati; domini.dat e copia locale sul thread dell'interfaccia

- Dove: UI/App.cs:33 (`Store.Load` prima della finestra), Core/Store.cs:391-404 (`ProtectedTextConverter.Open`: una `Store.Unprotect` per ogni valore "dpapi:"; contrassegni e posposti 2 testi, modelli 3, regole fino a circa 10, Core/Store.cs:314-355 e Core/Rules.cs:20-49). Le voci dei contrassegni escono solo da azioni fatte in Missiva (Core/Flags.cs:112, UI/MainWindow.Flags.cs:215) o con l'account (Core/Store.cs:248): un contrassegno completato sul telefono resta per sempre. Poi, nel primo elenco: UI/ViewModels.cs:589 (costruttore di `MessageItem`) chiama `SenderLogos.Note`, che la prima volta legge domini.dat con DPAPI e JSON per riflessione (Core/SenderLogos.cs:100-139); UI/MainWindow.xaml.cs:2104 (`MailCache.LoadMessages`, file e DPAPI) e 915 (`LoadFolders`), sul thread dell'interfaccia.
- Scenario: avvio dopo un anno d'uso con centinaia di contrassegni datati e qualche regola e modello: le decifrature si sommano prima che la finestra esista. Al primo elenco, nel tempo fra "Posta in arrivo scelta" ed "elenco nel disegno", lettura di domini.dat e della copia locale.
- Gravità: Media e crescente nel tempo. Stima 0,05-0,3 ms per decifratura: 500 contrassegni aggiungerebbero 50-300 ms; domini.dat e copia locale 10-50 ms una volta. La misura dell'autore nel csproj (Missiva.csproj:31-33) dà "lettura delle impostazioni 41-44 ms" con i dati di allora.
- Stato: CONFERMATO il meccanismo e la mancata potatura. SOSPETTO il costo e quante voci ha l'autore. Il legame con i blocchi di 61-70 ms "Send SynchronizationContextAwaitTaskContinuation" del primo disegno (CLAUDE.md) è un'ipotesi.
- Proposta: decifrare oggetto e mittente di contrassegni e posposti alla prima lettura (servono solo alle notifiche), le regole al primo controllo; potare all'avvio le voci senza promemoria scadute da oltre 90 giorni (da decidere con l'autore); `SenderLogos` precaricato in `Task.Run` in `App.OnStartup` come `AddressBook.Preload` (UI/App.cs:44), con un `JsonSerializerContext`; `MailCache.LoadMessages` e `LoadFolders` della prima Posta in arrivo in `Task.Run`, aspettati in `LoadAccountsAsync`.
- Verifica su Windows: righe "tempi: avvio, stili" e "impostazioni lette" (UI/App.cs:32-34) in Release con 0, 200 e 500 contrassegni finti nella copia dei dati; `Log.Startup` prima e dopo il primo `Note`; blocchi "interfaccia lenta" del primo elenco su 4 avvii, con e senza la lettura anticipata.

### V-12 Ogni messaggio nuovo nella cartella aperta ricostruisce l'elenco (view.Refresh)

- Dove: UI/MainWindow.xaml.cs:2397-2401 (`AddIncoming`: `rows.Prepend`, poi `RegroupKeepingSelection`), 2583-2592 (`RegroupKeepingSelection`: `view.Refresh()` a 2588, poi `selected.Where(rows.Rows.Contains)`); anche 3283-3289 (`TogglePin`) e UI/MainWindow.Unified.cs:264-265.
- Scenario: arriva un messaggio (IDLE, delta, controllo) con la Posta in arrivo aperta e qualche pagina caricata, magari mentre si scorre. `Refresh` rigruppa tutte le righe e manda un Reset alla ListBox: i contenitori visibili si rifanno e ogni riga visibile rifà Compute e OnRender; poi la riselezione costa O(S x N). Il commento dice che serve per i gruppi nuovi (Fissati, Oggi dopo mezzanotte), ma si fa sempre.
- Gravità: Media. Stima 15-20 righe visibili x 1,6-2,3 ms (costo di una riga nuova misurato dall'autore) = 25-45 ms più il rigruppamento O(N), a ogni arrivo; l'animazione di ingresso parte su righe appena rifatte.
- Stato: CONFERMATO. SOSPETTO il costo.
- Proposta: `Refresh` solo se una riga aggiunta appartiene a un gruppo che la vista non ha ancora (`view.Groups` contiene già il nome del gruppo di ogni riga aggiunta: allora WPF la mette da solo al posto giusto); riselezione con un HashSet.
- Verifica su Windows: "arrivo di prova" in MISSIVA_KEYS dopo aver caricato 4-5 pagine, con `Log.Time` attorno a `RegroupKeepingSelection`; StallLog; `lampi.ps1 -List` per controllare che l'ingresso resti uguale.

### V-13 Elimina, Sposta e Annulla di molte righe: una SelectionChanged per riga e un messaggio aperto per sbaglio

- Dove: UI/MainWindow.xaml.cs:2847-2858 (`RemoveRows`: `rows.Rows.IndexOf` e `rows.Remove` per riga, senza `regrouping`), 2594-2614 (`OnMessageSelected`, che esce solo con `regrouping`), 3153-3155 (Annulla: `SelectedItems.Add` per riga).
- Scenario: Ctrl+A e Canc su 2000 righe, poi Ctrl+Z. Per ogni riga tolta parte SelectionChanged e `UpdateCommands` (che copia la selezione: O(k²) in tutto); quando resta scelta una riga sola, quella che sta per uscire, parte `ShowInReaderAsync`, subito annullato. In Annulla, al primo `SelectedItems.Add` (una riga scelta) si apre nel riquadro il primo messaggio ripreso e, con "segna letto subito", lo si segna letto.
- Gravità: Media. Stima a 2000 righe: 4 milioni di confronti fra IndexOf e Remove, 2000 rimozioni con gruppi e selezione, 2000 SelectionChanged: 0,1-0,5 s; quadratico.
- Stato: CONFERMATO la mancanza di `regrouping` e il ramo con una riga scelta. SOSPETTO il segno di letto dopo Annulla (dipende da quando arriva il corpo).
- Proposta: `regrouping = true` in un try/finally attorno ai cicli di `RemoveRows` e di Annulla, come già a 2183-2187 e 2217-2221; poi una sola `UpdateCommands` e una scelta esplicita del riquadro (una riga: il messaggio; più righe: nessuno); indici da un dizionario costruito una volta.
- Verifica su Windows: nella prova Elimina e Annulla cambiano solo l'elenco (`Testing`). Ctrl+A con 4-5 pagine, Canc, Ctrl+Z: StallLog e la riga "prova: segnerei letto &lt;id>" dopo Annulla (oggi c'è, dopo la modifica no).

### V-14 Prima pagina riletta con le pagine già caricate: elenco ricostruito, costo quadratico

- Dove: UI/MainWindow.xaml.cs:2198-2225 (`MergeFirstPage`: `rows.Clear(); rows.Append(fresh.Concat(older...))` a 2218-2219), UI/ViewModels.cs:1020-1040 (`Append`, un `Insert` per riga); nel raggruppamento di WPF ogni inserimento cerca il posto scorrendo la lista dall'inizio (ListCollectionView, CollectionViewGroupInternal.FindIndex).
- Scenario: Posta in arrivo scorsa fino a 2000 righe; un avviso IDLE di messaggi usciti (eliminato dal telefono) o la ripresa della coda rilegge la prima pagina (`RefreshListAsync`, 3783). Si ricreano 2000 `MessageItem` (ognuno con `Kind`, una scansione delle cartelle, e `SenderLogos.Note`), un Reset e 2000 inserimenti O(N).
- Gravità: Media. Stima per N=2000: 50-130 ms; quadratico in N. Una pagina successiva normale costa invece O(N) per riga.
- Stato: CONFERMATO (Missiva e sorgente pubblico di WPF). SOSPETTO i tempi.
- Proposta: unire invece di ricostruire: togliere con `rows.Remove` solo le righe con data ≥ della più vecchia della pagina che il server non ha più, aggiornare letto e contrassegno come `ShowFirstPage(fromCache: true)` (2155-2171), `Prepend` per i nuovi se sono più recenti della prima riga non fissata; altrimenti la ricostruzione di oggi.
- Verifica su Windows: in Debug "aggiungi 40 0 3" più volte per avere N grande, poi un comando di prova che chiami `MergeFirstPage` con la prima pagina meno un messaggio, con `Log.Time` e StallLog.

### V-15 Suggerimenti dei destinatari: tutta la rubrica ricostruita e ri-tokenizzata a ogni lettera

- Dove: Core/AddressBook.cs:187-223 (`Suggest`: copia di tutte le voci in un dizionario nuovo a 197-198, un `Entry` nuovo per ogni indirizzo dei contatti a 200-216, tutto sotto `lock (Gate)`), 271-283 (`Rank`: `ToLowerInvariant`, tre `Split` con vettori di separatori nuovi, `Concat`, `ToList` per voce), 175-181 (`Wanted`: `ToLowerInvariant` e tre `Replace` per indirizzo); Core/Contacts.cs:261-266 (`ForSuggestions` copia l'elenco a ogni chiamata). Chiamato a ogni lettera da UI/MainWindow.Suggestions.cs:112 (casella Cerca, già dalla prima lettera), UI/ComposeWindow.xaml.cs:958 (A, Cc, Ccn) e UI/EventWindow.cs:400.
- Scenario: rubrica di 5.000 voci e 4.500 contatti (per esempio i contatti di Outlook e di Google copiati), si scrive "mario r" nel campo A.
- Gravità: Media: è sul thread dell'interfaccia a ogni lettera; lineare in rubrica più contatti.
- Stato: MISURATO (A.5, copia di `Entry`, `Wanted`, `Suggest` e `Rank`): 10-15 ms e 18 MB allocati a ogni lettera, quasi indipendenti da quello che si scrive.
- Proposta: un indice in cache, ricostruito solo quando cambiano rubrica o contatti (`bookVersion` in `Add`, `ContactBook.Version` in `Add`, `Remove`, `SetRemote`): per ogni voce le parole già in minuscolo (`string[]`), nome e indirizzo in minuscolo; `Rank` senza allocazioni sui vettori pronti; se la ricerca nuova comincia con la precedente e la versione è la stessa, si filtra solo fra le voci trovate la volta prima; separatori in campi `static readonly char[]`; il lucchetto solo per prendere l'indice.
- Verifica su Windows: Debug con `--persone --contatti-finti --contatti-molti`, poi in MISSIVA_KEYS `ctrl+e` e "testo m", "testo ma", "testo mar": righe "tempi: suggerimenti: pannello aggiornato N ms" (UI/MainWindow.Suggestions.cs:123); nella scrittura "suggerimenti per ... sullo schermo" (UI/ComposeWindow.xaml.cs:977).

### V-16 Persone: ricerca senza pausa, confronti culturali e riordino completo a ogni lettera

- Dove: UI/PeopleView.cs:79-83 (`TextChanged` chiama `Fill` a ogni lettera), 163-174 (`Fill`), UI/PeopleView.Sections.cs:95 (`all.Where(c => c.Matches(words)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)` per ogni sezione), Core/Contacts.cs:67-68 (`Matches`: un vettore, due `Concat` e fino a una decina di `Contains` con `CurrentCultureIgnoreCase`).
- Scenario: 4.500 contatti, si scrive un nome nella casella di Persone: per ogni lettera tutti i contatti confrontati campo per campo, poi riordinati per nome con il confronto culturale, poi righe nuove e `ItemsSource` sostituito.
- Gravità: Media; lineare nei contatti (n log n per l'ordine).
- Stato: MISURATO (A.5, `Matches` e `OrderBy` copiati, cultura it-IT): 8-62 ms a ogni lettera, di più allungando la ricerca (meno contatti corrispondono, quindi si confrontano tutti i campi, note comprese). Il costo di WPF per le righe nuove non è misurato.
- Proposta: `Matches` senza vettori né `Concat`; l'elenco ordinato tenuto per sezione e invalidato da `LoadAsync`, `SaveAsync`, `RemoveAsync` e Importa, così il filtro conserva l'ordine senza riordinare; una pausa di 120-150 ms prima di `Fill` con lo stesso schema a versione di `SuggestPause`. `OrdinalIgnoreCase` darebbe risultati diversi solo con le lettere accentate maiuscole e minuscole: va deciso, non è necessario per il guadagno principale.
- Verifica su Windows: Debug `--persone --contatti-finti --contatti-molti --cerca-persone a` con un `Log.Time` attorno a `Fill`; StallLog scrivendo a mano nella casella.

### V-17 Il codice dei provider riprende sul thread dell'interfaccia dopo ogni await

- Dove: in Core e UI c'è un solo `ConfigureAwait(false)` (Core/SenderLogos.cs:144). Graph: Core/GraphProvider.cs:134-135 (`ReadAsStringAsync`), 178 (`JsonNode.Parse` della risposta con l'HTML dentro), 670-717 (`ReadBodyAsync`, 717 `Convert.FromBase64String` delle immagini incorporate). Gmail: Core/GmailProvider.cs:149-209 (`RunAsync`), 884-897 e 934-943 (decodifica del testo e delle immagini). `MicrosoftAuth.InBackgroundAsync` (Core/MicrosoftAuth.cs:63-73) imposta solo un AsyncLocal, non cambia thread. Chiamanti dal thread dell'interfaccia: UI/ReadingView.xaml.cs:655 (corpo), 378 e 418 (conversazione e anteprime), UI/MainWindow.xaml.cs:2114, 2242, 2289, 2336 (elenco, anteprime, anticipi), UI/App.cs:482-491 (Posta inviata) e tutto il controllo dei nuovi messaggi (UI/App.cs:685-753), anche con la finestra nascosta.
- Scenario: ogni corpo, elenco, conversazione e anticipo: il JSON di Graph, la decodifica delle parti MIME e delle immagini base64 e la costruzione dei riepiloghi girano sul thread dell'interfaccia, anche mentre si scorre o si legge.
- Gravità: Media. Stima 1-15 ms per corpo grande; si somma agli scatti dello scorrimento quando arrivano anticipi e anteprime.
- Stato: CONFERMATO per il codice di Missiva; cosa fa MailKit al suo interno non si vede da qui.
- Proposta: chiamare i provider da `Task.Run` dove li usa l'interfaccia (`FetchBodyAsync`, conversazione, `PrefetchAsync`, `GetPreviewsAsync`), oppure `ConfigureAwait(false)` in `GraphProvider.SendAsync` e nei `RunAsync` di Gmail e IMAP. Prima vanno protetti i campi oggi serializzati solo dal thread dell'interfaccia: `allMailFolder` (Core/GmailProvider.cs:668), `threadsWarm` (674), `expandAttachments` (Core/GraphProvider.cs:31).
- Verifica su Windows: dotnet-trace sampled-thread-time sul thread dell'interfaccia durante 20 "clic N": tempo in `JsonNode.Parse`, `TextPart.Text`, `DecodeToAsync`.

### V-18 La cache delle strutture dei messaggi si svuota tutta oltre 1000 voci

- Dove: Core/GmailProvider.cs:639 e Core/ImapProvider.cs:890 (`if (known.Count > 1000) known.Clear()`). La riempiono elenco, ricerca, conversazioni, nuovi messaggi e copia dei 30 giorni; ne dipendono `OnDisk` (Core/GmailProvider.cs:762, Core/ImapProvider.cs:959), l'anticipo (Core/GmailProvider.cs:834: senza voce salta il messaggio) e `ReadBodyAsync` (869-872: senza voce una FETCH in più).
- Scenario: dopo una ventina di pagine, qualche conversazione lunga o un giro della copia dei 30 giorni la cache si svuota mentre la cartella resta aperta. Fino al prossimo elenco il clic va al server invece che alla copia su disco (CLAUDE.md: 290-440 ms contro 40-70) e i vicini non si scaricano più in anticipo.
- Gravità: Media, a intermittenza, sull'azione più frequente.
- Stato: CONFERMATO. SOSPETTO quanto spesso succede.
- Proposta: togliere le voci più vecchie (una coda d'inserimento come `bodyOrder`) invece di `Clear()`, o un limite che non tocchi la cartella aperta.
- Verifica su Windows: una riga di registro al `Clear()`; con "clic N" confrontare "corpo dalla copia su disco" e "corpo (intero...)" prima e dopo aver scorso 25 pagine.

### V-19 I corpi già in memoria si puliscono solo al clic, e la pulizia vale per un'istanza sola

- Dove: `ReadingPage.Warm` ha un solo chiamante, UI/ReadingView.xaml.cs:682; la cache è una `ConditionalWeakTable<MessageBody, Prepared>` con una sola variante (tema e immagini) per istanza (UI/ReadingPage.cs:401, 413, 425). I corpi scaricati in anticipo non passano di lì (UI/MainWindow.xaml.cs:2282-2298 e 2319-2337, Core/GmailProvider.cs:824-855, Core/GraphProvider.cs:622-638); ogni lettura dalla copia su disco crea un'istanza nuova (Core/Offline.cs:226-230), così ogni corpo di Graph (Core/GraphProvider.cs:640-650). Sole, Mostra immagini e cambio di tema ripuliscono sul thread dell'interfaccia: UI/ReadingView.xaml.cs:1185-1188, 1297-1302, 142-149, attraverso UI/ReadingPage.cs:434.
- Scenario: freccia giù su un messaggio scaricato in anticipo: il corpo è pronto, ma prima del disegno si aspetta `MailHtml.Prepare` in `Task.Run` (MISURATO nella parte A su Linux: 7,2 ms nel tema chiaro e 11,6 nello scuro per una newsletter da 200 KB, 41 e 61 ms per 1 MB). Il sole avanti e indietro rifà la pulizia ogni volta, e sul thread dell'interfaccia.
- Gravità: Media.
- Stato: CONFERMATO; il costo di Prepare è MISURATO (parte A, A.1, su Linux).
- Proposta: dopo `PrefetchAsync` e dopo `SavedBody` dei vicini, `ReadingPage.Warm` in `Task.Run` con il tema e le immagini di adesso; una piccola LRU (circa 30 voci) di `Prepared` con chiave account, cartella, id, StableId, tema e immagini, così le istanze nuove dello stesso messaggio la trovano; prima dei Render del sole, di Mostra immagini e del tema, `await Task.Run(() => ReadingPage.Warm(...))` come in `FetchBodyAsync`; due varianti per corpo (chiaro e scuro).
- Verifica su Windows: "tempi: HTML pulito in sottofondo" durante una navigazione con la freccia sui messaggi scaricati in anticipo, prima e dopo; StallLog premendo il sole su una newsletter grande.

### V-20 BodyCache: Trim elenca tutta la copia dopo ogni scrittura, sotto il lucchetto che Forget prende sul thread dell'interfaccia

- Dove: Core/Offline.cs:174-188 (`Save`: scrittura e `Trim(account)` dentro `lock (Gate)`), 304-317 (`Trim`: `GetFiles` ricorsivo di tutti i .bin, ordinamento, `GetDirectories`), 344-361 (`Forget`, stesso lucchetto, chiamato sul thread dell'interfaccia da UI/MainWindow.xaml.cs:2910, 2990, 2995, 3620), 153-156 (`FileOf` con SHA-256 e `File.Exists` sul thread che chiama), 429-451 (`OfflineCopy.RunAsync`: `BodyCache.Size` e fino a 500 `Has` sul thread dell'interfaccia).
- Scenario: si apre una cartella e i 10 corpi in anticipo fanno 10 `Trim` di fila; si apre un messaggio e lo si elimina subito: `Forget` aspetta un `Trim` in corso. Nella copia dei 30 giorni (fino a 500 corpi per giro) il costo diventa quadratico nei file.
- Gravità: Media sopra qualche migliaio di file, Bassa sotto. Stima 2-5 µs per file elencato: 10-25 ms per `Trim` con 5.000 file, 40-100 ms con 20.000.
- Stato: CONFERMATO. SOSPETTO i tempi di enumerazione su NTFS con l'antivirus.
- Proposta: un totale di byte per account in memoria (una sola enumerazione in `TrimAll`, poi aggiornato da `Save`, `Forget`, `Delete`); `Trim` completo solo oltre il limite o al più una volta all'ora; `Forget` e `ForgetFolder` in `Task.Run`; `File.Exists` dentro il `Task.Run` di `Save`; `Size` e `Has` di `RunAsync` fuori dal thread dell'interfaccia.
- Verifica su Windows: copiare un .bin della prova fino a 5.000 e 20.000 file nella cartella dei corpi di un account in prova-dati; `Log.Time` su `Trim`; aprire una cartella, aprire un messaggio, Canc subito (solo nel registro), StallLog.

### V-21 Ricerca senza rete: decifra e analizza ogni file a ogni ricerca, senza annullamento

- Dove: Core/Offline.cs:250-287 (`BodyCache.Search`: `Parallel.ForEach` su tutti i .bin, 267 `ReadAllBytes`, `Unprotect` e JSON, 281 `PlainText` con due regex NonBacktracking, 260 `Contains` con `CurrentCultureIgnoreCase`); chiamata da UI/MainWindow.Offline.cs:189 (`Task.Run` senza CancellationToken) e una volta per account da UI/MainWindow.Unified.cs:317. `Take(max)` arriva dopo aver letto tutto.
- Scenario: senza rete si cerca una parola e poi subito un'altra: due scansioni complete insieme, ognuna su tutti i core, e in Tutti gli account una per account.
- Gravità: Media, solo senza rete. Misura dell'autore in CLAUDE.md: 0,73 s in parallelo e 2,6 s su un thread solo per circa 400 corpi; lineare nei byte della copia, fino a 180-200 MB per account.
- Stato: CONFERMATO. SOSPETTO quale fase domini (DPAPI, JSON, PlainText o i confronti culturali).
- Proposta: `CancellationToken` in `ParallelOptions`, annullato da una ricerca nuova e dalla chiusura; file ordinati per `LastWriteTimeUtc` decrescente a blocchi di 200, fermandosi a `max` risultati; per le ricerche con soli filtri (da:, oggetto:, date) una cache in memoria delle sole `SavedBody.Summary` costruita alla prima ricerca senza rete.
- Verifica su Windows: `--senza-rete` con le copie gonfiate di V-20; la riga "tempi: ricerca nella copia sul PC" c'è già; servono Stopwatch per fase e una seconda ricerca subito dopo la prima.

### V-22 Rubrica: lucchetto tenuto durante il salvataggio cifrato, voci mai tolte

- Dove: Core/AddressBook.cs:321 (`ScheduleSave`, 5 s dopo una modifica), 323-343 (`Save`: serializzazione per riflessione, `ProtectedData.Protect` e scrittura dentro `lock (Gate)`). Prendono lo stesso lucchetto sul thread dell'interfaccia: `Received` (113-122, da UI/MainWindow.xaml.cs:2116 a ogni pagina dell'elenco), `Record` (130-139, da UI/ReadingView.xaml.cs:687 a ogni corpo), `LookAlike` (253-268, da Core/SenderCheck.cs:106 a ogni Render, scorre tutte le voci), `Suggest` (187-223, a ogni lettera). Nessuna rimozione di voci. Stesso schema in Core/Contacts.cs:121-137 (`Save`, sincrono da UI/PeopleView.cs:573, 608, 682) e 181-204 (`SetRemote` tiene `gate`, usato da `Local` e `Remote` a ogni lettera).
- Scenario: si legge un messaggio dopo l'altro; un elenco o un corpo con persone nuove fa partire un salvataggio 5 s dopo; il clic successivo in quel momento aspetta in `LookAlike` dentro Render.
- Gravità: Bassa-Media. Stima 10-60 ms per blocco quando coincide; cresce con la rubrica, che non si sfoltisce mai.
- Stato: CONFERMATO. SOSPETTO durata e frequenza dei conflitti.
- Proposta: sotto `Gate` solo la copia delle voci e di `sentScans`; JSON (con un contesto generato), Protect e scrittura fuori, sotto un lucchetto solo per il file. Lo stesso in `ContactBook.Save` e `SetRemote`. Per `LookAlike` un indice nome → indirizzi a cui si è scritto, aggiornato in `Add`. Potatura in `Save` delle voci con `Sent == 0`, `Received <= 2` e ultima ricezione più vecchia di un anno.
- Verifica su Windows: `Log.Time` dentro `Save` con il tempo tenuto sotto lucchetto; dotnet-trace thread-time aprendo messaggi ogni 4-6 s (Monitor.Enter in `LookAlike` e `Record`).

### V-23 HttpClient senza compressione, in HTTP/1.1 e con un pool separato per gli scaricamenti

- Dove: Core/GraphProvider.cs:33 (`Http`, timeout 60 s) e 35 (`Downloads`), Core/GoogleApi.cs:20, Core/GoogleAuth.cs:74, Core/MailAutoconfig.cs:23: gestore predefinito, niente `AutomaticDecompression` (c'è solo nei loghi, Core/SenderLogos.cs:338-367), `DefaultRequestVersion` 1.1, nessun `PooledConnectionIdleTimeout`.
- Scenario: i JSON di Graph (corpo HTML dentro il JSON con tutte le intestazioni, pagine da 50 righe, calendarView, contatti) viaggiano non compressi; ogni richiesta contemporanea (corpo e conversazione, quattro immagini, sei cartelle speciali) apre una connessione TLS se non ce n'è una calda; un'apertura annullata a metà (Ctrl+. tenuto) butta la connessione; `Downloads` (allegati, Origine, PEC) ha un pool suo, quasi sempre freddo.
- Gravità: Media sulle reti lente (telefono). Stima: gzip toglie il 70-85% dei byte di un JSON di testo; HTTP/2 risparmia 1-2 giri di handshake (40-100 ms) per ogni richiesta parallela a connessione fredda.
- Stato: CONFERMATO la configurazione. SOSPETTO il guadagno: va verificato che Graph e le API di Google rispondano in HTTP/2 e con gzip.
- Proposta: un solo `SocketsHttpHandler` per Graph con `AutomaticDecompression = DecompressionMethods.All`, `PooledConnectionIdleTimeout` di 5-10 minuti, `EnableMultipleHttp2Connections = true`; due HttpClient che lo condividono (`new HttpClient(handler, disposeHandler: false)`) con i due timeout; `DefaultRequestVersion = HttpVersion.Version20` e `DefaultVersionPolicy = RequestVersionOrLower`. Lo stesso per GoogleApi. Nella riga "tempi:" di Graph (Core/GraphProvider.cs:136) scrivere anche `response.Version` e il Content-Encoding.
- Verifica su Windows: righe "tempi: Graph ..." con versione e KB prima e dopo; "clic, testo pronto" su 20 messaggi Microsoft con la rete del telefono; `netstat -ano` con il PID di Missiva aprendo una newsletter con 8 immagini incorporate.

### V-24 Graph: il limite di 4 richieste per casella vale solo dentro ogni singola chiamata

- Dove: Core/GraphProvider.cs:808-820 (`EachAsync`: un `SemaphoreSlim(4)` nuovo a ogni chiamata), 1192 (calendario, semaforo locale), 234-243 (sei GET delle cartelle speciali senza limite), 118-150 (`SendAsync`: nessun semaforo per casella; con 429 aspetta Retry-After o 400-800 ms, fino a 5 s, 141-146).
- Scenario: clic su una newsletter con la conversazione ancora in corso più quattro immagini (5 richieste); all'avvio sei cartelle speciali, prima pagina e delta di partenza (UI/App.cs:529). Oltre quattro, Outlook risponde 429 e la richiesta aspetta.
- Gravità: Media: quando capita sul corpo o sulle immagini costa da 400 ms a 5 s.
- Stato: CONFERMATO che il limite per casella manca. SOSPETTO quanto spesso capita sul clic.
- Proposta: un `SemaphoreSlim(4)` per istanza di `GraphProvider` (ogni istanza è una casella, anche le condivise) attorno a `Http.SendAsync` e `Downloads.SendAsync`, con un posto riservato a corpo e immagini del messaggio aperto.
- Verifica su Windows: contare "risposta 429" nelle righe "tempi: Graph" all'avvio e aprendo 20 newsletter Microsoft di fila.

### V-25 Graph: tutte le immagini incorporate prima del testo, senza limite di dimensione; l'invito arriva dopo, in fila

- Dove: Core/GraphProvider.cs:684-703 (`FetchAsync` di tutte le incorporate a 690, senza filtro di dimensione: il limite di 300 KB vale solo nel ciclo 693-703), attraverso il client con 60 s su tutta la risposta (33); 737 e 1312-1340 (l'invito di Exchange: l'intero messaggio senza `$select`, dopo le immagini, più `CalendarTokenAsync`); 741 (l'.ics dopo le immagini); 716-717 (`Str` e `Convert.FromBase64String`: stringa, nodo JSON e byte per ogni immagine).
- Scenario: un'email con quattro foto incollate da 3 MB (isInline, `cid:` nel testo): il testo non si vede finché non arrivano circa 16 MB di base64 in JSON non compresso; con 3 Mbit/s il timeout di 60 s può far fallire l'apertura. Un invito aggiunge una GET in fila.
- Gravità: Media per le foto (raro ma bloccante); Bassa per gli inviti.
- Stato: CONFERMATO.
- Proposta: stessa soglia anche per le incorporate; quelle oltre la soglia da `DownloadAsync` su `/attachments/{id}/$value` (byte veri, timeout sull'attesa) dopo il primo disegno, con un secondo disegno come la conversazione; `EventInviteAsync` e la GET dell'.ics in parallelo con le immagini; lettura con `ReadAsStreamAsync` e `JsonDocument.ParseAsync`, poi `GetBytesFromBase64()`.
- Verifica su Windows: un messaggio di prova con foto incollate, "clic, testo pronto" e righe "tempi: Graph GET .../attachments/..." con i KB.

### V-26 La notifica di un messaggio nuovo aspetta il conteggio dei non letti e, una volta all'ora, la Posta inviata

- Dove: UI/App.cs:738 (`_ = AddressBook.ScanSentAsync(p)`), 742 (`await p.GetInboxUnreadAsync()`), 746-750 (`AddIncoming` e `Notify`). `GetSentRecipientsAsync` e `GetInboxUnreadAsync` usano tutte e due la connessione in sottofondo (Core/GmailProvider.cs:607-630, Core/ImapProvider.cs:864-879).
- Scenario: il messaggio nuovo è già noto, ma prima di `Notify` partono NOOP e SEARCH COUNT (Graph: una GET). Se è il primo controllo dopo un'ora, `ScanSentAsync` prende il semaforo per primo e il conteggio (quindi la notifica) aspetta LIST, EXAMINE della Posta inviata e FETCH di 500 buste; il commento a UI/App.cs:736-737 dice che le notifiche non la aspettano, ma in Gmail e IMAP la aspettano.
- Gravità: Media. Stima 1-2 giri a ogni notifica (Graph 60-95 ms, Gmail 100-250 ms); circa 2 s in più una volta all'ora.
- Stato: CONFERMATO l'ordine. SOSPETTO quanto spesso la scansione oraria capita su un arrivo.
- Proposta: `AddIncoming(p, incoming, null)` e `Notify` subito dopo le regole (`AddIncoming` accetta `unread` null, UI/MainWindow.xaml.cs:2391-2395), poi il conteggio e un secondo aggiornamento del numero; `ScanSentAsync` dopo il conteggio; per Graph `GetNewAsync` e `GetInboxUnreadAsync` insieme.
- Verifica su Windows: nel registro il tempo fra "IDLE: nuovo messaggio" e "notifica mostrata" prima e dopo; in Debug `ScanInterval` accorciato per far capitare la scansione su un arrivo.

### V-27 Invio dopo i suggerimenti rifà la stessa ricerca; l'annullamento non ferma i comandi IMAP

- Dove: UI/MainWindow.Suggestions.cs:155-167 e 215-227 (`suggestCache` serve solo al pannello), UI/MainWindow.Offline.cs:176 (Invio richiama `provider.SearchAsync`), Core/GmailProvider.cs:493 (la prima pagina rifà sempre la SEARCH), 377-382 (`Remember` tiene una sola ricerca "cerca|": quella dei file cancella quella del testo), 501 e 507 (SEARCH e FETCH senza controllo del token fra i due); Core/ImapProvider.cs:676-688; Core/GraphProvider.cs:387-409.
- Scenario: si scrive "fattura", si aspetta il pannello (in Gmail due SEARCH X-GM-RAW e due FETCH da 50 righe in fila sulla connessione delle conversazioni) e si preme Invio: la stessa SEARCH e la stessa FETCH ripartono in coda a quelle del pannello, già annullate ma non fermabili a metà. Graph rifà il `$search`. Intanto aprire un risultato aspetta la connessione.
- Gravità: Media. Stima: fino a una ricerca intera in più a ogni Invio (0,3-1 s in Gmail); CLAUDE.md riporta 0,42 s per Invio su "fattura" con due account.
- Stato: CONFERMATO.
- Proposta: per la prima pagina usare il Task di `suggestCache` con la stessa chiave (account, cartella, testo) se è finito bene da meno di 60 s o se è ancora in corso (`suggestCache` che tiene `Task<MessagePage>`); in Gmail `Remember` con 2-3 voci "cerca|" e riuso degli UID entro 60 s come `ReuseSearch` di IMAP (Core/ImapProvider.cs:536); `cancel.ThrowIfCancellationRequested()` fra SEARCH e FETCH (nessun comando MailKit interrotto).
- Verifica su Windows: "testo fattura", 1 s, "invio": righe "tempi: attesa della connessione delle conversazioni N ms" (Core/GmailProvider.cs:154), "tempi: suggerimenti per ..." e il tempo della ricerca; ProtocolLogger di MailKit per contare le UID SEARCH X-GM-RAW (oggi 2, dopo 1).

### V-28 Risposte Gmail e IMAP: ogni salvataggio della bozza rilegge l'originale sulla connessione principale e ricalcola la citazione sul thread dell'interfaccia

- Dove: Core/GmailProvider.cs:1415-1416 (FETCH di Envelope e References sulla connessione della cartella dell'originale, con SELECT se ne è aperta un'altra) e 1427 (`MailHtml.ToText(quoted.Html)`); Core/ImapProvider.cs:1489-1490 e 1501 (stesso schema). Il codice dopo gli `await` torna sul thread dell'interfaccia (V-17).
- Scenario: risposta a una newsletter da 200-500 KB con la finestra principale su un'altra cartella. A ogni salvataggio automatico: SELECT della cartella dell'originale sulla principale (0,3 s secondo CLAUDE.md) e FETCH; il clic successivo nella cartella aperta paga un altro SELECT; `ToText` della citazione sul thread dell'interfaccia (MISURATO nella parte A su Linux: 4,9 ms per una newsletter da 200 KB, 34 ms per 1 MB; quadratico con molte righe `<br>`, V-09) e, con un HTML profondo, il rischio di V-01.
- Gravità: Media.
- Stato: CONFERMATO; il costo di `ToText` è MISURATO (parte A, su Linux).
- Proposta: In-Reply-To, References e testo citato calcolati una volta per finestra (campi in `OutgoingMessage` riempiti al primo `BuildAsync`, o una cache nel provider per `original.StableId`); se servono di nuovo, con EXAMINE sulla connessione in sottofondo; `ToText` in `Task.Run`.
- Verifica su Windows: risposta aperta ed elenco su un'altra cartella: righe "tempi: apertura di &lt;cartella>" (Core/GmailProvider.cs:219) a ogni "bozza salvata da sola"; StallLog durante il salvataggio.

### V-29 Il processo dei PDF si avvia e si ferma sul thread dell'interfaccia

- Dove: Core/PdfWorker.cs:271-277 (`RunAsync`: dopo `await gate.WaitAsync(cancel)` chiama `Start()` in modo sincrono), 304-328 (`Start`: `Sandbox.Start` con CreateProcessW, job, AppContainer, Core/Sandbox.cs:38-211), 344-370 (`Stop`: `WaitForExit(500)` e `WaitForExit(2000)`, chiamato a 283 e 293); chiamanti dal thread dell'interfaccia: UI/AttachmentPreview.cs:245, 335, 381.
- Scenario: la prima anteprima di un PDF o di un'immagine nella sessione, e di nuovo ogni volta che il processo si è chiuso (un minuto senza richieste, o 20 s dopo la chiusura dell'anteprima, Core/PdfWorker.cs:244, 299): chi apre una fattura ogni mezzo minuto paga l'avvio ogni volta.
- Gravità: Media. CLAUDE.md: "processo nell'AppContainer avviato in 40-90 ms", che sono di interfaccia ferma; con un PDF bloccato `Stop` può tenerla ferma fino a 0,5-2,5 s (stima).
- Stato: CONFERMATO il thread; durata riportata da CLAUDE.md.
- Proposta: `await Task.Run(Start)` dentro il semaforo (che resta preso), `await Task.Run(Stop)` nei due rami di errore; oppure `await gate.WaitAsync(cancel).ConfigureAwait(false)`.
- Verifica su Windows: righe "tempi: processo dei PDF avviato" e StallLog "interfaccia lenta: blocco di N ms" al clic sull'allegato, prima e dopo.

### V-30 Chiudere l'anteprima fa due raccolte complete bloccanti sul thread dell'interfaccia

- Dove: UI/AttachmentPreview.cs:167-174 (`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` a priorità ApplicationIdle), chiamato da `ReadingView.ClosePreview` (UI/ReadingView.xaml.cs:1366-1372), anche aprendo un altro messaggio dall'anteprima (268).
- Scenario: Esc o clic su un altro messaggio con l'anteprima aperta: le due raccolte cadono mentre il messaggio nuovo si sta caricando.
- Gravità: Media. Stima 40-200 ms con 150-250 MB di heap (CLAUDE.md riporta 13-90 ms per una raccolta compattata).
- Stato: CONFERMATO.
- Proposta: una sola `GC.Collect(2, GCCollectionMode.Forced, blocking: false)`, senza `WaitForPendingFinalizers` e senza la seconda; oppure rimandarla finché il messaggio nuovo non è mostrato.
- Verifica su Windows: la riga esistente "tempi: anteprima chiusa, memoria liberata (...)" misura proprio le due raccolte; StallLog; memoria dopo dieci anteprime con e senza la modifica.

### V-31 Riga dell'elenco: ogni testo si formatta due o tre volte, e ogni riga crea risorse di disegno nuove

- Dove: UI/MessageRowElement.cs:393, 401-402, 417, 419 (ft1 e data: `Height`, `WidthIncludingTrailingWhitespace` letti in Compute, poi `DrawText` a 693), 428-457 (seconda riga e icone), 445-447 (dove), 519-520 (anteprima, e `Make("A", ...)` per l'anteprima vuota), 549-551 (iniziali), 576-600 (pastiglie: Width, `Fit` che invalida, poi Height); risorse: 545 (`AvatarBrush`, un SolidColorBrush nuovo a ogni lettura, UI/ViewModels.cs:723 e 1317-1322), 671 (ImageBrush non congelato a ogni disegno con logo), 596 (Pen non congelata), 689 (RectangleGeometry), 697-698 (`Guide`: un GuidelineSet nuovo per forma), 350 (`TryFindResource` per ogni pennello, 6-10 per riga). Nel sorgente di WPF FormattedText rifà la formattazione al disegno se le misure sono già state lette ("we'll format twice").
- Scenario: ogni riga nuova nello scorrimento veloce: circa 12 passate del TextFormatter per riga e 3-8 risorse nuove del compositore.
- Gravità: Media. Stima 0,1-0,3 ms in meno per riga nuova (il 5-15% della mediana di 1,6-2,3 ms misurata dall'autore) e meno allocazioni, quindi meno raccolte a 2-4 righe per fotogramma. Non tocca la scelta già fatta della riga disegnata da un elemento: solo i suoi costi interni.
- Stato: CONFERMATO che ogni testo si misura prima del disegno e che le risorse sono nuove; SOSPETTO l'entità del guadagno.
- Proposta (disegno identico): larghezza e altezza di icone, iniziali e date in cache statiche per (testo, dimensione, peso, DPI); l'altezza della riga vuota "A" in cache; per `where` e pastiglie leggere Height insieme a Width prima di `Fit`; 12 pennelli congelati per la tavolozza degli avatar, un ImageBrush congelato per logo, la Pen congelata rifatta in `RefreshAll`, GuidelineSet congelati in cache per barra e immagine; pennelli del tema in campi statici riletti in `MessageRowElement.RefreshAll`.
- Verifica su Windows: "vetrina" e `confronta.ps1` (pixel uguali), "altezze" (69,2 e 86,84), "costo righe" e "scorri 60/120/240" con lavoro, MB allocati, raccolte e "chi alloca" (MS.Internal.TextFormatting, SolidColorBrush, GuidelineSet); dotnet-trace del tempo di `TextFormatterImp.FormatLine` sotto MessageRowElement.

### V-32 Loghi: file e decodifica PNG sul thread dell'interfaccia durante lo scorrimento; logo riletto dal disco a ogni disegno del riquadro

- Dove: UI/ViewModels.cs:1222-1235 (`Logos.Get`: `File.Exists` e decodifica sincrona, 1245-1256 BitmapImage OnLoad) chiamato da UI/MessageRowElement.cs:546 dentro Compute; UI/ViewModels.cs:742-744 (`LogoDomain` ricalcolato a ogni lettura con `SenderLogos.DomainOf`, Core/SenderLogos.cs:69-81), UI/MainWindow.xaml.cs:566-570 (`OnLogo` rifà il dominio su tutte le righe a ogni logo arrivato); UI/ReadingView.xaml.cs:793 e 836-837 e UI/ViewModels.cs:1238-1243 (`File.ReadAllBytes` del logo a ogni Render, per ogni messaggio della conversazione).
- Scenario: primo scorrimento della sessione fra molti mittenti aziendali; conversazione di 30-50 messaggi di un'azienda con logo (30-50 letture di file per Render, 2-3 Render per apertura).
- Gravità: Media sui fotogrammi con un dominio nuovo (stima 0,3-1,5 ms per decodifica: due domini nuovi in un fotogramma a 120 Hz lo fanno perdere); Bassa per il riquadro (5-30 ms per Render sulle conversazioni lunghe).
- Stato: CONFERMATO. SOSPETTO i tempi.
- Proposta: precaricare in sottofondo i loghi dei domini di ogni pagina arrivata (`Task.Run`, BitmapImage con Freeze, poi in `images`); `logoDomain` calcolato una volta per `MessageItem`; i byte del logo in un dizionario dominio → byte[] svuotato da `Arrived` e un indirizzo per dominio nella pagina.
- Verifica su Windows: dopo un avvio nuovo "scorri 120" due volte con "Mostra il logo" acceso e spento; dotnet-trace (BitmapDecoder e File.Exists sotto MeasureOverride); "tempi: pagina costruita" su una conversazione lunga con logo.

### V-33 La conversazione che arriva rifà la pagina intera; gli script del riquadro vanno in fila

- Dove: UI/ReadingView.xaml.cs:323-345 (337 `Render()` alla conversazione arrivata), 786-833 (`Render`, 832 Navigate), 961-1005 (DOMContentLoaded: Fit, ImagesPending, eventuale secondo Fit, ScrollTo, Reveal; NavigationCompleted: Fit, ScrollTo, Reveal), 1070-1077 (Fit scrive zoom, legge scrollWidth, riscrive: impaginazioni forzate), 1086-1108 (risorse servite di nuovo dal thread dell'interfaccia).
- Scenario: prima apertura nella sessione di una conversazione di Gmail: dopo 0,2-0,5 s la seconda pagina; Chromium rianalizza, reimpagina e ridecodifica il corpo aperto, 5-7 script in più e tutte le immagini di nuovo. Un'apertura semplice fa 5-7 giri di `ExecuteScriptAsync` in serie.
- Gravità: Media sulle newsletter (stima 20-80 ms nel processo della pagina più 5-15 ms di interfaccia); Bassa-Media per gli script (2-10 ms per apertura).
- Stato: CONFERMATO il percorso e il numero di giri; SOSPETTO il costo.
- Proposta: un solo script per DOMContentLoaded che adatta, controlla le immagini e, se non ne mancano, rimette lo scorrimento e toglie "fitting"; in NavigationCompleted Fit solo se la pagina non è stata mostrata o sono arrivate immagini; Fit in due passate (azzera tutti, legge tutti, scrive tutti). Per la conversazione (cambiamento grande): con il messaggio già mostrato inserire le intestazioni chiuse con uno script invece di navigare, con identificativi stabili nelle azioni.
- Verifica su Windows: "clic N": distanza fra "testo pronto" e "pagina mostrata"; secondo "pagina mostrata" e `lampi.ps1` su conversazioni lunghe.

### V-34 Scrittura: quattro conversioni del documento per salvataggio; immagini decodificate due volte sul thread dell'interfaccia

- Dove: UI/ComposeWindow.xaml.cs:494 (`OnAutoSaveTick`, `State()`), 393 e 1425 (`Message`, `FlowToHtml.Convert`), 397 (`ContentState()`), 480 (`Saved`, `State()`), 1436 (`TextRange.Text` di tutto il documento); Core/FlowToHtml.cs:248-260 (`FromFile`: `ReadAllBytes` e `Decode` solo per controllare), 276-279 (`Create` decodifica di nuovo), 264-271 (`FromBitmap`, PNG), UI/ComposeWindow.xaml.cs:1275 e 1290-1295.
- Scenario: a ogni salvataggio automatico il documento si converte in HTML quattro volte sul thread dell'interfaccia (una tabella grande incollata da Excel le rende care); una foto da 12 MP trascinata si legge e si decodifica due volte; uno screenshot 4K negli appunti si codifica in PNG e si decodifica sul thread dell'interfaccia.
- Gravità: Bassa per i messaggi normali; Media con documenti grandi (stima 4 x 20-100 ms) e per le foto (150-500 ms per immagine, una volta).
- Stato: CONFERMATO il numero di conversioni e la doppia decodifica; SOSPETTO i tempi.
- Proposta: un contatore `revision++` in `Touched`, confrontato in `OnAutoSaveTick` e `Saved` invece di `State()`; in `SaveDraftAsync` una sola conversione passata a `Message` e a `ContentState`; `FromFile` che controlla solo la firma dei byte (`TypeOf`) o restituisce la bitmap già decodificata a `Create`; decodifica e PNG in `Task.Run` (le bitmap sono già congelate).
- Verifica su Windows: `Log.Time` attorno a `FlowToHtml.Convert` con la tabella di `--formato-finto` ingrandita; StallLog con `OnEditorPasting` e `OnFileDrop` per uno screenshot 4K e tre foto da 12 MP.

### V-35 Pulizia delle newsletter molto grandi: più che lineare e centinaia di MB allocati

- Dove: Core/MailHtml.cs:40-182 (`Prepare`), in particolare 56 (`dom.All.ToList()`), 58 e 151 (`Attributes.Where(...).ToList()` per ogni elemento), 173 (`body.InnerHtml` più `wrapper`, poi la stringa del frammento a 180); poi UI/ReadingPage.cs:66-77 e 491-501 (la pagina costruita in più StringBuilder e stringhe) e UI/ReadingView.xaml.cs:1100 (`Encoding.UTF8.GetBytes(page.Html)`).
- Scenario: newsletter da 1-5 MB (cataloghi, riepiloghi con molte righe).
- Gravità: Bassa (email rare), ma sono le più lente da aprire.
- Stato: MISURATO (A.1): da 1 MB a 5 MB l'analisi di AngleSharp cresce come l'ingresso (16,7 → 78,6 ms, ×4,7) mentre `Prepare` cresce di più (chiaro 40,9 → 307 ms, ×7,5; scuro 61 → 446 ms, ×7,3) e alloca 240-313 MB per un'email da 5 MB (49-64 MB per 1 MB). SOSPETTO: la parte più che lineare viene dalle raccolte di memoria su un heap grande; serve un profilo.
- Proposta: profilare `Prepare` sul 5 MB (dotnet-trace) prima di cambiare; due interventi sicuri: niente `ToList()` di attributi quando non c'è nulla da togliere (controllare prima con un ciclo), e la pagina del riquadro costruita in un solo buffer UTF-8, tenuto nella `ReadingPage` e servito così senza `GetBytes` a ogni richiesta (oggi, per una newsletter da 1 MB, circa cinque copie UTF-16 nel Large Object Heap più una UTF-8 per ogni disegno, e 2-3 disegni per apertura: stima).
- Verifica su Windows: `Missiva.exe --page` sulla newsletter da 5 MB del banco con dotnet-trace; dotnet-counters (LOH, gen2) aprendo 10 newsletter grandi.

### V-36 Controllo dei nuovi messaggi IMAP e Gmail: NOOP ripetuti, FETCH delle date a ogni giro, controllo completo per i soli segni

- Dove: Core/ImapProvider.cs:763-769 (`FreshInboxAsync`: NOOP, o EXAMINE se la connessione è su un'altra cartella), chiamato tre volte nello stesso controllo (Core/GmailProvider.cs:542, 558, 609; Core/ImapProvider.cs:773, 786, 866); Core/GmailProvider.cs:545 (FETCH delle date di fino a 20 non letti a ogni controllo); UI/App.cs:553-563 (anche un avviso di soli segni fa un controllo completo); Core/GmailProvider.cs:474-479 (anteprime e anticipi fanno EXAMINE della loro cartella sulla stessa connessione).
- Scenario: con non letti di oggi già annunciati ogni controllo fa NOOP, SEARCH, FETCH delle date, NOOP, SEARCH COUNT (5 comandi, 8 con le regole); leggere 10 messaggi sul telefono genera 10 controlli completi; guardando la Posta inviata la connessione in sottofondo salta fra due cartelle (0,3 s per EXAMINE).
- Gravità: Bassa-Media: in sottofondo, ma occupa la connessione che serve ad anteprime e anticipi e ritarda la notifica (V-26). Stima 2-3 giri per controllo.
- Stato: CONFERMATO.
- Proposta: niente NOOP se ce n'è stato uno da meno di 1-2 s sulla stessa connessione; `GetNewAsync` per UID come `ArrivedAsync` (UIDVALIDITY e ultimo UID visto, `UID n+1:* UNSEEN`), che toglie la FETCH delle date; per gli avvisi di soli segni solo il conteggio.
- Verifica su Windows: ProtocolLogger di MailKit sulla connessione in sottofondo per 10 minuti (con il token XOAUTH2 mascherato): comandi per controllo e righe "controllo di ..." con i ms.

### V-37 Gmail dopo una sospensione: il primo clic può aspettare una connessione morta

- Dove: Core/GmailProvider.cs:30 e 66 (`ImapClient` senza Timeout: il valore di base di MailKit; Core/ImapProvider.cs:32 usa 60 s), 159-203 (`RunAsync` riprova solo dopo l'errore); UI/App.cs:122-131 (un cambio di rete fa un controllo solo se un account è non in linea o ha azioni in coda); nessun gestore di `PowerModeChanged`; `ResetConnectionsAsync` non ridefinito da GmailProvider (Core/Models.cs:663).
- Scenario: portatile riaperto dopo un'ora; se la connessione è morta senza RST, il primo clic aspetta l'errore TCP o il timeout, poi TLS e XOAUTH2 (1-2 s secondo CLAUDE.md).
- Gravità: Media quando succede.
- Stato: SOSPETTO: dipende da come muore la connessione; il timeout predefinito di MailKit va confermato.
- Proposta: alla ripresa (`SystemEvents.PowerModeChanged`, Resume) e a `NetworkAddressChanged` chiudere senza saluto le connessioni che non fanno IDLE e riaprire la principale come `Preconnect`; timeout di 30-60 s anche per Gmail.
- Verifica su Windows: sospendere il PC 45 minuti, poi clic su un messaggio non in memoria: righe "attesa della connessione principale", "collegamento IMAP principale", "clic, testo pronto".

### V-38 Graph: sottocartelle chieste in fila, una per cartella

- Dove: Core/GraphProvider.cs:225-271 (`Children` con await in un `foreach`, 267 e 270), 234-243 (sei GET delle cartelle speciali).
- Scenario: un account di lavoro con 10 cartelle che hanno figlie fa più di 10 GET in fila (stima 0,6-1 s) all'avvio e a ogni ricarica delle cartelle.
- Gravità: Media per le caselle con un albero di cartelle; Bassa per l'account personale (CLAUDE.md: 0,13-0,2 s).
- Stato: CONFERMATO.
- Proposta: `Children` in parallelo con un limite di 3 (dentro il limite di V-24), o una sola `mailFolders/delta` (elenco piatto con `parentFolderId`, da verificare che dia i conteggi); gli id delle cartelle speciali dalla copia locale, chiesti solo se mancano.
- Verifica su Windows: righe "tempi: Graph GET mailFolders..." all'avvio con un account che ha sottocartelle.

### V-39 Registro: il file si apre e si chiude a ogni riga, sotto un lucchetto unico, sul thread di chi scrive

- Dove: Core/Store.cs:540-552 (`Log.Write`: `Directory.CreateDirectory`, `FileInfo`, eventuale rotazione, `File.AppendAllText` dentro `lock (Gate)`). Scrivono dal thread dell'interfaccia, fra gli altri, StallLog (UI/StallLog.cs:94, 115, 145), `UpdateBadge` (UI/MainWindow.xaml.cs:1236, una riga per ogni cambio dei non letti, vedi V-05), UI/App.cs:562, 752, 819, 847 e, nella prova, le righe "tempi:" del riquadro (UI/ReadingView.xaml.cs:684, 796, 963, 976, 1000).
- Scenario: nella versione installata poche righe a riposo, ma una per messaggio nelle azioni su molti messaggi (V-05). Nella prova 4-6 righe per clic sul thread dell'interfaccia, 2-3 dentro l'intervallo misurato fino a "testo pronto": la misura include il registro. Un thread di sottofondo che scrive fa aspettare l'interfaccia sul lucchetto.
- Gravità: Bassa nella versione installata (stima 0,1-1 ms per riga, di più con l'antivirus); Media per l'affidabilità delle misure della prova.
- Stato: CONFERMATO. SOSPETTO il costo per riga su Windows.
- Proposta: `Write` prende l'ora e mette la riga in un `Channel<string>` con un solo lettore che apre il file una volta per gruppo di righe (FilePath ricalcolato a ogni apertura), con la rotazione; svuotamento sincrono (al più 2 s) in `App.OnExit`, `DispatcherUnhandledException` e fine sessione; `Log.Error` può restare sincrono.
- Verifica su Windows: Release di prova, 20 "clic N" con dotnet-trace thread-time: tempo dell'interfaccia in `Log.Write` (CreateFileW, CloseHandle) e in Monitor.Enter, prima e dopo.

### V-40 MSAL: il "consenso mancante" non si ricorda, e i tentativi passano da un semaforo unico

- Dove: Core/MicrosoftAuth.cs:311-314 (`catch (MsalUiRequiredException) when (!interactive) { return null; }`, nulla in `tokens`), 82 (un solo `TokenGate` per tutti gli account); chiamanti a ogni caricamento delle cartelle (Core/GraphProvider.cs:556, 1020), a ogni invito aperto (1339), a ogni spostamento nel calendario (1183).
- Scenario: un account senza consenso alle impostazioni o al calendario rifà `AcquireTokenSilent` a ogni occasione, in fila; un rinnovo del token principale aspetta dietro.
- Gravità: Bassa-Media.
- Stato: CONFERMATO la mancata memoria; SOSPETTO il costo di ogni tentativo con WAM.
- Proposta: ricordare il "no" per account e permessi fino a un consenso interattivo riuscito o per 30-60 minuti; un semaforo per account.
- Verifica su Windows: `Log.Time` attorno ad `AcquireTokenSilent` in `ExtraTokenAsync`, all'avvio e aprendo un invito.

### V-41 Altri costi quadratici o ripetuti negli elenchi (piccoli)

- Dove: UI/ViewModels.cs:241-254 e 279 (`SetFolders`: `Any`, `flat.Contains`, `Folders.IndexOf` dentro cicli; con 300 cartelle circa 300.000 confronti) e 467-477, 537-546 (10 notifiche per cartella, su un elenco delle cartelle non virtualizzato); UI/ViewModels.cs:981-992 e 1039 (Contrassegnati per scadenza: `PlaceOf` scorre le righe e per ognuna `Flags.Find` su tutti i contrassegni, Core/Flags.cs:87-95); UI/MainWindow.xaml.cs:3413-3415 (`selected.All(rows.Rows.Contains)` a ogni apertura del menu); UI/MessageRowElement.cs:125-127 (`Arrived` invalida la disposizione della riga anche se Compute non lo legge: 400 ms dopo ogni arrivo le righe visibili si rifanno, UI/MainWindow.xaml.cs:2535-2542).
- Scenario: caricamento delle cartelle di un account Gmail con molte etichette; vista Contrassegnati di tutti gli account; menu del tasto destro dopo Ctrl+A su 2000 righe; prima pagina dal server.
- Gravità: Bassa. Stime: SetFolders 5-20 ms; Contrassegnati 5-15 ms per pagina; menu 2-5 ms; Arrived fino a 15-20 righe x 1,6-2,3 ms una volta.
- Stato: CONFERMATO; SOSPETTO i tempi.
- Proposta: HashSet e mappa genitore-figli in `SetFolders`, notifiche solo per i valori che cambiano; chiave di scadenza in cache per `MessageItem` e ricerca binaria; un HashSet delle righe per chiamata nel menu; `case nameof(MessageItem.Arrived): return;` in `OnItemChanged`.
- Verifica su Windows: `Log.Time` attorno a `SetFolders` all'avvio, ad `Append` con `--unified Flagged`, a `BuildMessageMenu` dopo Ctrl+A; "arrivo di prova" con un contatore su Compute.

### V-42 Raccolte forzate ripetute dopo la copia dei 30 giorni

- Dove: UI/MainWindow.Offline.cs:219-222 (lo scatto chiama `OfflineCopyAsync` e subito dopo `CompactWhenIdle`), 276 e 291-297 (`CompactAfterCopy` non segna `compactedPause`), 237-246 (`CompactWhenIdle`); tutte `GC.Collect(2, Aggressive, blocking, compacting)` sul thread dell'interfaccia.
- Scenario: a finestra visibile ferma da 5 minuti, un giro della copia con corpi nuovi fa due compattazioni di fila.
- Gravità: Bassa (CLAUDE.md: 13-90 ms per raccolta).
- Stato: CONFERMATO.
- Proposta: `CompactAfterCopy` imposta `compactedPause = lastActivity`.
- Verifica su Windows: nel registro della prova "memoria compattata dopo la copia" seguita subito da "memoria compattata a finestra ferma".

### V-43 Senza rete il messaggio si apre dalla copia su disco sul thread dell'interfaccia

- Dove: UI/ReadingView.xaml.cs:636 e 663 (`BodyCache.Load` sincrono: file, DPAPI e JSON, Core/Offline.cs:209-233); la stessa lettura in linea, a 646-650, va in `Task.Run` con il commento "5-30 ms".
- Scenario: ogni clic con l'account non in linea o dopo un errore di rete.
- Gravità: Bassa-Media, solo senza rete: 5-30 ms per clic secondo il commento del codice.
- Stato: CONFERMATO.
- Proposta: `await Task.Run(() => BodyCache.Load(account, entry.Summary))` in tutti e due i punti.
- Verifica su Windows: `--senza-rete` con "clic N", righe "clic, testo pronto" prima e dopo.

### V-44 Calendario, notifiche e contatti: piccoli lavori sincroni sul thread dell'interfaccia

- Dove: Core/Calendar.cs:435-469 (copia locale con JSON per riflessione e DPAPI, da UI/CalendarView.cs:176-177 e 210); Core/GraphProvider.cs:1186 e Core/GmailProvider.Calendar.cs:62-63 (elenco dei calendari riletto a ogni cambio di settimana, prima degli eventi); UI/NotificationAvatar.cs:19-45 (`thread.Join()` del thread STA che disegna le iniziali, da `Notify`, UI/App.cs:840); Core/GoogleContacts.Write.cs:28 (`RegexOptions.Compiled` nell'inizializzazione statica di una classe parziale usata anche per leggere); `Process.Start` con ShellExecute sul thread dell'interfaccia (UI/ReadingView.xaml.cs:1527 e 1449, UI/P7mWindow.cs:145, UI/PeopleView.cs:228, UI/CalendarView.cs:868).
- Scenario: primo Calendario della sessione (CLAUDE.md: 131 ms senza una causa trovata); ogni freccia del calendario; la prima notifica di un mittente con iniziali nuove; la prima apertura di Persone; il clic su un link.
- Gravità: Bassa. Stime: 5-40 ms una volta per i primi, un giro di rete in fila per l'elenco dei calendari, 20-300 ms per ShellExecute.
- Stato: CONFERMATO; SOSPETTO i tempi.
- Proposta: `JsonSerializerContext` per `CalendarData` e Load/Save in `Task.Run`; elenco dei calendari tenuto per la sessione; `PathOfAsync` con un TaskCompletionSource invece di `Join`; `[GeneratedRegex]`; `_ = Task.Run(() => Process.Start(...))`.
- Verifica su Windows: `freddo-C.ps1 -Profilo` sul primo clic del Calendario; righe "calendario di ..."; StallLog al clic di un link e alla prima notifica di un mittente nuovo.

## Elenchi chiesti dal compito

### Avvio (punto 9): chi fa cosa, e su quale thread

| Quando | Cosa | Dove | Thread |
|---|---|---|---|
| subito | mutex, nome del canale | Program.cs:52-56 | interfaccia |
| subito | `Store.Load`: JSON generato più una decifratura DPAPI per ogni testo protetto (V-11) | UI/App.cs:33, Core/Store.cs:461-473, 391-404 | interfaccia |
| subito | `MicrosoftAuth.WarmUp`, `GmailProvider.Preconnect` (TLS e XOAUTH2), `AddressBook.Preload` | UI/App.cs:40-44 | pool |
| subito | `Safety.CleanTemp`, `StallLog.Start`, finestra (XAML) e Show | UI/App.cs:45, 66-93 | interfaccia |
| Loaded | `StartAsync`: impostazioni, cartelle; `Reader.Prepare` messo in coda ad ApplicationIdle; poi aspetta il primo disegno | UI/MainWindow.xaml.cs:95-110 | interfaccia |
| primo disegno | provider; IDLE subito (`Task.Run`), delta di Graph subito; per ogni Gmail quattro accessi XOAUTH2 nei primi secondi (principale, IDLE, conversazioni, in sottofondo) | UI/MainWindow.xaml.cs:885-937, UI/App.cs:473-479, 511-530, Core/GmailProvider.cs:387, 677-683 | pool e interfaccia |
| primo disegno | copia locale di cartelle e Posta in arrivo (DPAPI), 50 `PlainPreview`, primo `MessageItem` con `SenderLogos.Note` e domini.dat (V-11) | UI/MainWindow.xaml.cs:915, 2104-2108, UI/ViewModels.cs:589 | interfaccia |
| circa 1 s | elenco dal server, `SaveFirstPage` (DPAPI e scrittura), `AddressBook.Received` (lucchetto, V-22) | UI/MainWindow.xaml.cs:2114-2131 | rete, poi interfaccia |
| dopo | anteprime, poi 10 corpi in anticipo, ognuno con `BodyCache.Save` e `Trim` (V-20) | UI/MainWindow.xaml.cs:2132, 2231-2298 | interfaccia e pool |
| ApplicationIdle | `Reader.Prepare`: WebView2, `MailHtml.Prepare` di prova in `Task.Run`, pagina finta | UI/ReadingView.xaml.cs:445-475 | interfaccia e pool |
| +15 s | `ScanSentAsync` (Gmail e IMAP sulla connessione in sottofondo, che serve anche anteprime e anticipi: i primi clic la trovano occupata 1-3 s, stima), poi `ContactBook.RefreshRemoteAsync` | UI/App.cs:482-491, Core/AddressBook.cs:289-318, Core/GmailProvider.cs:619-630 | rete, poi interfaccia |
| +20 s | `MailCache.EncryptOld`: rilegge per intero tutti i .json della copia per guardarne il primo byte | Core/MailCache.cs:64-87 | pool |
| +20-30 s | primo giro dei promemoria; `BodyCache.TrimAll` sotto il lucchetto di `Forget` | UI/MainWindow.Calendar.cs:110-158, Core/Offline.cs:323-337 | interfaccia, pool |

### Dal clic alla pagina mostrata (punto 14)

1. `OnMessageSelected` → `ShowInReaderAsync` (UI/MainWindow.xaml.cs:2594-2614, 2306-2317), interfaccia.
2. `ReadingView.ShowAsync`: lettura rimandata, anteprima chiusa, annullamento dell'apertura di prima, `HurryDelay` (UI/ReadingView.xaml.cs:264-290), interfaccia.
3. `FetchBodyAsync` (626): `Pec.Recall` e `KeptBody` (interfaccia), `SavedBody` in `Task.Run`, `GetBodyAsync` (rete; il codice del provider dopo ogni await torna sull'interfaccia, V-17), `BodyCache.Save` (SHA-256, `File.Exists` e lucchetto sull'interfaccia, V-20), `ReadingPage.Warm` in `Task.Run` (l'unica analisi AngleSharp dell'apertura), `AddressBook.Received` (lucchetto, V-22).
4. Attesa del corpo con `FadeDelay` e `ShowDelay`, poi `Render` (311): `ReadingPage.Build` con `SenderCheck.Of` (→ `LookAlike`, lucchetto), `LogoOf` (lettura del file del logo per ogni messaggio, V-32), `PrepareBody` dalla cache, `Navigate`. Tutto sull'interfaccia senza cedere il thread.
5. Nello stesso blocco: `MarkRead` → `UpdateBadge` per un non letto (V-05), `bodyShown` → anticipo dei vicini.
6. `OnResourceRequested` (1086-1108): `Encoding.UTF8.GetBytes(page.Html)`, immagini cid e loghi, sull'interfaccia.
7. DOMContentLoaded (961-977): Fit, ImagesPending, eventuale attesa e secondo Fit, ScrollTo, Reveal: un giro di `ExecuteScriptAsync` per passo, in serie (V-33). NavigationCompleted (978-1005): Fit, ScrollTo, Reveal.
8. Conversazione arrivata diversa: un secondo `Render` intero e un altro giro di script (323-346, V-33).

Lo stesso HTML si analizza con AngleSharp una volta per corpo, tema e immagini consentite (UI/ReadingPage.cs:401-426); ma la cache vale per un'istanza del corpo (V-19). La pagina di una conversazione di 50 messaggi (Gmail ne prende al più 50, Core/GmailProvider.cs:708) contiene il corpo solo dei messaggi aperti; quelli chiusi sono intestazione e anteprima, circa 1-1,2 KB ciascuno (stima), cioè 50-60 KB più CSS e corpo aperto. Un'apertura semplice fa 5-7 giri di `ExecuteScriptAsync`, una conversazione nuova di Gmail 12-16.

### Chiamate frequenti a Store.Save (punto 6)

Ci sono 83 chiamate fuori dalle prove, tutte sul thread dell'interfaccia; ognuna serializza tutte le impostazioni (V-10). Quelle che scattano più spesso:

| file:riga | quando |
|---|---|
| UI/ReadingView.xaml.cs:609 | ogni tacca di Ctrl+rotellina o Ctrl +/- nel riquadro |
| UI/ViewModels.cs:36 | ogni apertura o chiusura di un account nella barra |
| UI/ViewModels.cs:334 | ogni apertura o chiusura di sottocartelle |
| UI/MainWindow.Unified.cs:70, UI/MainWindow.xaml.cs:1616 | apri e chiudi Tutti gli account e Preferiti |
| UI/MainWindow.xaml.cs:4183, UI/MainWindow.Unified.cs:296 | ogni ricerca (ricerche recenti, con una cifratura se l'elenco cambia) |
| UI/MainWindow.xaml.cs:3619 | Posponi: una per messaggio, dentro il ciclo |
| UI/MainWindow.xaml.cs:3705 | ritorno da Posponi: una per messaggio, dal timer di un minuto |
| Core/Rules.cs:581 | ogni controllo con messaggi arrivati e regole di Missiva attive |
| UI/MainWindow.xaml.cs:879 | Visualizza (conversazioni, densità, riquadro, zoom), poi l'elenco si ricarica |
| UI/SettingsWindow.cs:148, 202, 218 | ogni opzione delle Impostazioni |
| UI/SettingsWindow.cs:668, 935 | modelli e firme: 500 ms dopo l'ultima lettera, con l'RTF intero |
| UI/MainWindow.Flags.cs:78, 98, 117, 136 | menu Contrassegna |
| UI/ReadingView.xaml.cs:1300 | Mostra immagini |
| UI/CalendarView.cs:162, 318 | vista del calendario, mostra e nascondi un calendario |

Un salvataggio cifra con DPAPI solo i testi protetti nuovi o cambiati (Core/Store.cs:407-412): zero per un clic, una per una ricerca nuova, fino a due per contrassegno o posposto nuovo. La scrittura è atomica (temporaneo e rinomina) ma senza flush (vedi "Fuori tema").

### Timer e raccolte forzate (punto 11)

| Timer | Dove | Intervallo | Cosa fa | Finestra nascosta |
|---|---|---|---|---|
| poll | UI/App.cs:107-113 | 2 minuti | `CheckNewAsync` di ogni account | uguale |
| deltaTimer | UI/App.cs:524-530 | 30 s | una GET delta per account Graph; con cambiamenti un controllo completo | uguale |
| snoozeTimer | UI/MainWindow.xaml.cs:3538, gestore 133-139 | 1 minuto | ritorni di Posponi, invii pianificati, promemoria dei contrassegni (liste piccole se nulla è scaduto) | uguale |
| offlineTimer | UI/MainWindow.Offline.cs:199, 219-224 | 1 minuto | copia dei 30 giorni (un giro per account ogni 30 minuti a finestra ferma), poi `CompactWhenIdle` | la copia parte; la compattazione a finestra ferma no |
| reminderTimer | UI/MainWindow.Calendar.cs:123-125 | 30 s | promemoria scaduti; ogni 30 minuti rilegge oggi e domani | uguale |
| undoTimer | UI/MainWindow.xaml.cs:3046 | 8 s, una volta | nasconde Annulla | |
| autoSave | UI/ComposeWindow.xaml.cs:459 | 1 s, solo con modifiche | salvataggio automatico della bozza | per le scritture rimaste aperte |
| themeCheck | UI/App.cs:349 | 250 ms, solo dopo un cambio di tema, al più 8 volte | `ApplyTheme` | |
| limit | UI/App.cs:327 | 20 s, una volta | fine sessione | |
| rubrica | Core/AddressBook.cs:321 (Threading.Timer) | 5 s dopo una modifica | `Save` nel pool (V-22) | uguale |
| processo dei PDF | Core/PdfWorker.cs:326 (Threading.Timer) | 1 minuto, o 20 s dopo la chiusura | `Stop` nel pool | |
| IDLE | Core/Idle.cs:157-161, 114 | rinnovo ogni 9 minuti; ripresa dopo 5 s raddoppiati fino a 5 minuti | solo rete | uguale |
| Rendering | UI/WheelScrolling.cs:171, 240; UI/StableScrollBar.cs:76, 94 | a ogni fotogramma | solo durante scorrimento e assestamento | |

Raccolte forzate (tutte sul thread dell'interfaccia; una raccolta bloccante ferma comunque tutti i thread):

| Dove | Quando | Tipo |
|---|---|---|
| UI/MainWindow.xaml.cs:483-492 (`CompactLaterAsync`, 489) | 5 s dopo ogni chiusura nell'area di notifica | LOH compattato una volta, poi `GC.Collect(2, Aggressive, blocking: true, compacting: true)` |
| UI/MainWindow.xaml.cs:4389-4397 (4396) | 3 s dopo ogni riduzione a icona | uguale |
| UI/MainWindow.Offline.cs:237-246 (`CompactWhenIdle`, 244) | al più una per pausa di 5 minuti, finestra visibile | uguale |
| UI/MainWindow.Offline.cs:291-297 (`CompactAfterCopy`, 295) | dopo un giro della copia con corpi nuovi, anche a finestra nascosta | uguale (V-42) |
| UI/AttachmentPreview.cs:167-174 (170, 172) | a ogni chiusura di un'anteprima con pagine o immagine | `GC.Collect()`, `WaitForPendingFinalizers()`, `GC.Collect()` (V-30) |

## Tabella riassuntiva, ordinata per guadagno atteso

Il guadagno combina quanto si risparmia e quanto spesso capita; per i punti non misurati è una stima.

| # | Sigla | Cosa | Gravità | Stato | Guadagno atteso |
|---|---|---|---|---|---|
| 1 | V-01 | Overflow dello stack in `ToText` e nel testo dei link: chiusura del processo | Alta | MISURATO | niente più chiusure con email annidate (48-72 KB bastano oggi) |
| 2 | V-04 | Regex quadratiche su stili e attributi | Alta | MISURATO | da 1,3-6,8 s a millisecondi su email di 30-60 KB |
| 3 | V-02 | "<" che non aprono un tag: AngleSharp quadratico | Alta | MISURATO | da 1,8 s e 9,5 GB a 5-8 ms su 100 KB |
| 4 | V-03 | Annidamento profondo: tema scuro che risale fino alla radice, `Closest` in `ToText` | Alta | MISURATO | 20.000 `<b><i>`: da 38,5 s a 5,5 s nel tema scuro; il resto è di AngleSharp (limite di profondità) |
| 5 | V-05 | Segna letto su molti messaggi: icona e registro per ogni messaggio | Alta | CONFERMATO | 0,5-3 s in meno per 500 messaggi; 2-15 ms per ogni non letto aperto (stima) |
| 6 | V-06 | IMAP: allegati riscaricati e ricaricati a ogni salvataggio automatico | Alta | CONFERMATO | circa 25 MB di traffico in meno per salvataggio con 10 MB di allegati |
| 7 | V-19 | Corpi in anticipo non puliti, cache per istanza, pulizia sul thread dell'interfaccia | Media | CONFERMATO (costo MISURATO) | 7-60 ms per freccia sui messaggi già scaricati |
| 8 | V-17 | Codice dei provider sul thread dell'interfaccia | Media | CONFERMATO | 1-15 ms per corpo grande, meno scatti con anticipi e anteprime (stima) |
| 9 | V-15 | Suggerimenti dei destinatari a ogni lettera | Media | MISURATO | 10-15 ms e 18 MB per lettera oggi (MISURATO); con l'indice in cache una frazione (stima) |
| 10 | V-12 | `view.Refresh` a ogni messaggio nuovo | Media | CONFERMATO | 25-45 ms per arrivo (stima) |
| 11 | V-24 | Graph senza limite di 4 richieste per casella: 429 | Media | CONFERMATO | 0,4-5 s quando capita sul clic |
| 12 | V-27 | Invio rifà la ricerca dei suggerimenti | Media | CONFERMATO | 0,3-1 s per Invio in Gmail (stima) |
| 13 | V-26 | Notifica dopo il conteggio e la Posta inviata | Media | CONFERMATO | 1-2 giri per notifica, 2 s una volta all'ora |
| 14 | V-07 | Tema scuro: una FormatException per parola | Media | MISURATO | eccezioni a zero; 82% con un foglio di 2.000 regole (84,6 → 14,9 ms), 28% con 5.000 stili negli elementi; sulle newsletter generate dentro il rumore |
| 15 | V-10 | `Store.Save` sincrono e completo | Media | CONFERMATO | 1-15 ms per clic frequente; raffiche (zoom, Posponi) accorpate |
| 16 | V-23 | HttpClient senza compressione e HTTP/2 | Media | CONFERMATO | 70-85% di byte in meno sui JSON; 40-100 ms per richiesta parallela fredda (stima) |
| 17 | V-18 | Strutture dei messaggi svuotate a 1000 | Media | CONFERMATO | clic dalla copia su disco invece che dal server (40-70 contro 290-440 ms secondo CLAUDE.md) |
| 18 | V-13 | Elimina e Annulla di molte righe | Media | CONFERMATO | 0,1-0,5 s per 2000 righe; niente messaggio aperto e segnato letto per sbaglio |
| 19 | V-08 | Regole: testo ricalcolato per regola | Media | CONFERMATO (costo MISURATO) | una `ReadableText` per messaggio invece di una per regola |
| 20 | V-09 | Rispondi: testo dell'originale sul thread dell'interfaccia | Media | MISURATO | 31 ms in meno di interfaccia ferma per una newsletter da 1 MB |
| 21 | V-28 | Bozze di risposta: originale riletto a ogni salvataggio | Media | CONFERMATO | un SELECT e una FETCH in meno per salvataggio, `ToText` fuori dall'interfaccia |
| 22 | V-29 | Processo dei PDF avviato sul thread dell'interfaccia | Media | CONFERMATO | 40-90 ms per anteprima (CLAUDE.md) |
| 23 | V-30 | Due raccolte bloccanti chiudendo l'anteprima | Media | CONFERMATO | 40-200 ms (stima) |
| 24 | V-14 | Prima pagina riletta: elenco ricostruito | Media | CONFERMATO | 50-130 ms con 2000 righe (stima) |
| 25 | V-16 | Persone: ricerca a ogni lettera | Media | MISURATO | 8-62 ms per lettera oggi (MISURATO); con l'ordine in cache e la pausa, un solo calcolo alla fine della parola (stima) |
| 26 | V-11 | Avvio: DPAPI per campo, contrassegni mai potati | Media | CONFERMATO | 50-300 ms con 500 contrassegni (stima), cresce nel tempo |
| 27 | V-20 | `BodyCache.Trim` a ogni scrittura | Media | CONFERMATO | 10-100 ms per scrittura con migliaia di file (stima) |
| 28 | V-21 | Ricerca senza rete senza annullamento | Media | CONFERMATO | secondi per ricerca superata |
| 29 | V-22 | Rubrica: lucchetto durante il salvataggio | Bassa-Media | CONFERMATO | 10-60 ms quando coincide (stima) |
| 30 | V-25 | Graph: immagini incorporate prima del testo | Media | CONFERMATO | il testo subito con email di foto incollate |
| 31 | V-31 | Riga dell'elenco: testi formattati 2-3 volte, risorse nuove | Media | CONFERMATO | 5-15% del costo di una riga nuova (stima) |
| 32 | V-32 | Loghi sul thread dell'interfaccia | Media | CONFERMATO | fotogrammi persi con domini nuovi; 5-30 ms per Render di conversazioni lunghe |
| 33 | V-33 | Conversazione: pagina rifatta; script in serie | Media | CONFERMATO | 2-10 ms per apertura; il secondo disegno per le conversazioni |
| 34 | V-34 | Scrittura: quattro conversioni, immagini decodificate due volte | Bassa-Media | CONFERMATO | 80-400 ms con documenti grandi; 150-500 ms per foto |
| 35 | V-36 | Controllo IMAP: NOOP e FETCH ripetuti | Bassa-Media | CONFERMATO | 2-3 giri per controllo |
| 36 | V-38 | Graph: sottocartelle in fila | Media | CONFERMATO | 0,6-1 s all'avvio con un albero di cartelle |
| 37 | V-37 | Gmail dopo la sospensione | Media | SOSPETTO | decine di secondi al primo clic, quando succede |
| 38 | V-35 | Newsletter molto grandi | Bassa | MISURATO | da capire con un profilo |
| 39 | V-43 | Senza rete, copia letta sul thread dell'interfaccia | Bassa-Media | CONFERMATO | 5-30 ms per clic senza rete |
| 40 | V-39 | Registro aperto e chiuso a ogni riga | Bassa | CONFERMATO | 0,1-1 ms per riga; misure della prova più pulite |
| 41 | V-40 | MSAL: consenso mancante non ricordato | Bassa-Media | CONFERMATO | tentativi silenziosi in meno sotto un semaforo unico |
| 42 | V-41 | Piccoli costi quadratici negli elenchi | Bassa | CONFERMATO | 2-20 ms in casi particolari |
| 43 | V-44 | Calendario, notifiche, link: piccoli lavori sincroni | Bassa | CONFERMATO | 5-300 ms una volta per caso |
| 44 | V-42 | Doppia compattazione dopo la copia | Bassa | CONFERMATO | una raccolta da 13-90 ms in meno |

## Fuori tema, da guardare a parte

Due cose trovate per strada che non sono di velocità:

- `CssUrl` (Core/MailHtml.cs:375) non riconosce un `url('...')` fra virgolette con una parentesi nell'indirizzo, quindi l'indirizzo esterno resta nella pagina e non conta fra le immagini bloccate. MISURATO nel banco (modo `url`): `background:url('https://img.news.example/a(b).png')` con le immagini non consentite dà 0 bloccate e l'indirizzo ancora nel frammento; senza virgolette e parentesi è bloccato. Secondo CLAUDE.md il riquadro intercetta comunque ogni richiesta, quindi l'immagine non dovrebbe partire, ma il commento di Prepare (Core/MailHtml.cs:148-149) vuole un blocco che non dipenda dalla pagina. La `CssUrl` riscritta di V-04 la blocca (MISURATO, stesso modo `url`).
- `Store.Save` scrive con file temporaneo e rinomina ma senza `Flush(true)` (Core/Store.cs:485-486), e `Store.Load` con un errore di lettura riparte da impostazioni vuote che il primo salvataggio scrive sopra al file (Core/Store.cs:469-472). Dopo uno spegnimento brusco si potrebbero perdere account e token (SOSPETTO: non riprodotto). Lo stesso schema senza flush in Core/MailCache.cs:123-124, Core/ActionQueue.cs:89-90, Core/Contacts.cs:129-130 e 195-196, Core/AddressBook.cs:335-336; Core/SenderLogos.cs:151 scrive direttamente, senza file temporaneo.

## Guardato e trovato a posto

- Attese bloccanti: nei file chiesti non ce n'è una pericolosa. UI/ReadingView.xaml.cs:308-309 legge `conversation.Result` solo dopo `IsCompleted`; UI/ReadingPage.cs:442 `done.Result` è un record, non un Task; UI/App.cs:333 `all.Result` dopo `IsCompletedSuccessfully`, dentro un DispatcherFrame; Core/PdfWorker.cs:332 `Wait(0)` non aspetta; Core/PdfWorker.cs:397-401 e 520-542 sono solo nella prova o nel processo figlio; UI/P7mWindow.cs:97-101 apre il .p7m in `Task.Run`; UI/PeopleView.cs non ne ha.
- Già fuori dal thread dell'interfaccia: pulizia dell'HTML all'apertura (UI/ReadingView.xaml.cs:682), corpo dalla copia in linea (650), cifratura e scrittura dei corpi (Core/Offline.cs:174), `AddressBook.Preload` (Core/AddressBook.cs:78-82), `MailCache.EncryptOld` e `BodyCache.TrimAll` (ritardati, in `Task.Run`), Importa contatti (UI/PeopleView.cs:717-722), PNG dell'anteprima (UI/AttachmentPreview.cs:338, 383), IDLE (UI/App.cs:519), PEC (Core/Pec.cs:78).
- `ProtectedTextConverter.Seal` non ricifra i testi già noti (Core/Store.cs:407-412); `Store.Save` è atomico (temporaneo e rinomina); impostazioni, copia locale e coda usano il JSON generato (Core/Store.cs:444-446, Core/MailCache.cs:9-12, Core/ActionQueue.cs:64).
- HttpClient: tutti statici, mai uno per richiesta. Token Microsoft in memoria fino a 5 minuti dalla scadenza (Core/MicrosoftAuth.cs:161-180); token di Gmail riusato dal file (Core/GoogleAuth.cs:227-267).
- Apertura di un messaggio: corpo e conversazione partono insieme e il corpo non aspetta la conversazione (UI/ReadingView.xaml.cs:291-305); in Gmail un clic non aspetta l'anticipo di un altro messaggio (corpo sulla principale, anticipi in sottofondo, Core/GmailProvider.cs:744, 837-842) e, se lo stesso messaggio si sta già scaricando, aspetta quello invece di rifarlo (726-731, 792-815); `HurryDelay` e l'annullamento delle aperture di prima funzionano (UI/ReadingView.xaml.cs:270, 285-290). Nella pagina c'è solo il corpo dei messaggi aperti (UI/ReadingPage.cs:223-235).
- Graph: azioni quattro alla volta (Core/GraphProvider.cs:811-820), scaricamenti con il limite sull'attesa (776-803), delta con il deltaLink (474-505), copia dei 30 giorni con le sole chiavi (433-452), messaggio successivo chiesto una volta sola (622-650). Gmail: LIST-STATUS (233), elenco senza anteprime (384-409), un comando per cartella nelle azioni (967-974), MOVE con COPYUID (994-1022), ESEARCH COUNT (607-613), IDLE su una connessione sua (Core/Idle.cs:138-171).
- Scorrimento: `WheelScrolling` fa un solo UpdateLayout per fotogramma (UI/WheelScrolling.cs:296) e alloca circa 100-250 byte per fotogramma; `StableScrollBar` imposta due proprietà per fotogramma e stacca Rendering a fine movimento (UI/StableScrollBar.cs:53-105); `ScrollHitGuard` una ricerca in una ConditionalWeakTable per fotogramma (UI/ScrollHitGuard.cs:71-106); `StallLog` una lettura e una scrittura in un vettore per operazione, nessun gestore di Rendering (UI/StallLog.cs:55-136). `MessageRowElement` fa un solo Compute per riga riciclata (93-102, 619-637), con i Typeface in cache (46-52); in `MessageItem` `AttachmentChips`, `DraftBrush`, `Dark` e `Kind` sono in cache (UI/ViewModels.cs:777, 654-656; UI/ContextMenus.cs:143-147; UI/ReadingView.xaml.cs:193-201).
- Scrittura: a ogni lettera `Touched` non serializza nulla (UI/ComposeWindow.xaml.cs:468-474); l'immagine incollata si codifica in PNG una volta sola e resta nel `Tag` (Core/FlowToHtml.cs:291-306); Graph salva le bozze con PATCH senza ricaricare allegati e immagini già presenti (Core/GraphProvider.cs:1503-1541); il correttore delle Impostazioni si accende dopo aver impostato il documento (UI/SettingsWindow.cs:608, 862).
- Ricerca: pausa di 350 ms con versione e annullamento (UI/MainWindow.Suggestions.cs:73-79, 152-158); IMAP in tutte le cartelle si ferma fra una cartella e l'altra e si riusa per 60 s (Core/ImapProvider.cs:536, 696); i filtri non lavorano a ogni lettera (UI/MainWindow.SearchFilters.cs:249-279); `suggestCache` limitata a 40 voci (UI/MainWindow.Suggestions.cs:220); elenco di Persone virtualizzato a pixel (UI/PeopleView.cs:118-126).
- Limiti delle cache: conversazioni 50 (UI/ReadingView.xaml.cs:350-368), `previewBytes` 20 MB (1319, 1357), `Pec.Recall` 8 buste e 64 MB (Core/Pec.cs:86-88, 112-115), 20 corpi in memoria per provider.
- A riposo il thread dell'interfaccia si sveglia circa 6,5 volte al minuto (delta ogni 30 s, promemoria ogni 30 s, Posponi e copia ogni minuto, controllo ogni due minuti), con poco lavoro quando nulla è scaduto. Elenco dei timer: UI/App.cs:107-113 (2 minuti), UI/App.cs:524-530 (30 s), UI/MainWindow.xaml.cs:3538 (1 minuto), UI/MainWindow.Offline.cs:199 (1 minuto), UI/MainWindow.Calendar.cs:123-125 (30 s), più quelli di una volta sola (Annulla 8 s, tema 250 ms, salvataggio automatico 1 s solo con modifiche, rubrica 5 s, processo dei PDF 1 minuto o 20 s). Con la finestra nascosta girano tutti; la copia dei 30 giorni parte (finestra "ferma"), la compattazione a finestra ferma no.
- MISURATO e a posto (parte A): `Invitation.Parse` di un .ics al limite di 256 KB in 1,4-9 ms secondo la forma, con le sue regex lineari (A.4); `ContactFiles.VCard` da 5 MB e `Csv` di 5.000 contatti in 39-180 e 7 ms; `SearchTerms.Parse` e `Matches` in microsecondi; `Mailto.Parse`, `ContactLinks` e `SenderCheck.Parse` trascurabili e con regex lineari o limitate. Le regex di `PlainPreview` (`ImagePlaceholder`, `MarkdownLink`, `CutMarkdownLink`) sono quadratiche, ma lavorano solo sulle anteprime (Graph bodyPreview al massimo 255 caratteri, Core/GraphProvider.cs:525; MailKit PreviewText, Core/GmailProvider.cs:481 e Core/ImapProvider.cs:602, 900; copia locale, Core/MailCache.cs:41): con 255 "[" il costo misurato è 0,05 ms.

## Non guardato

- L'interno di MailKit (code dei comandi, thread delle continuazioni, PreviewText su Gmail), di MSAL e WAM, di WebView2 e Chromium (costo reale di `ExecuteScriptAsync` e di `WebResourceRequested`), di AngleSharp oltre ai casi misurati.
- Tutto quello che dipende da Windows e non si misura qui: costo di DPAPI, di NTFS e dell'antivirus su apertura, chiusura e rinomina dei file, ICU contro NLS nei confronti culturali, il costo di un'eccezione sotto Windows.
- HtmlToFlow (bozze riprese), ContactWindow, GroupWindow, PersonCard, EventWindow oltre ai suggerimenti, RulesView oltre a Esegui ora, il resto di SettingsWindow.
- L'inizializzazione di `ToastNotificationManagerCompat` (UI/App.cs:54, 76); il file della cache di MSAL, quello del token di Google, la cartella dei dati di WebView2, la cartella `loghi` (file `.none` mai tolti) e i PNG di `NotificationAvatar`.
- `WebView2.OnWindowPositionChanged` a ogni layout (7% del profilo di CLAUDE.md), il costo delle intestazioni dei gruppi, `AutomationName` con un client di UI Automation attivo, il peso di `CursorProbe` e `DevTools` nella "Release con gli strumenti" (definisce DEBUG: per esempio `HitTestCore` in UI/MessageRowElement.cs:306-310).
- Invio SMTP (una connessione nuova a ogni invio), `ActionQueue.ReplayAsync`, `Pec.OpenAsync`, la memoria del processo dei PDF, `GetRecentAsync` di Graph.
- Missiva compilata ed eseguita: qui non si esegue (vedi la compilazione nella parte A).

## Appendice: il banco di misura, per rifarlo su Windows

Il banco è un progetto console `net10.0` fuori dal repository. Compila copie invariate di file e parti di file di `Core` e pochi finti (`Stubs.cs`) al posto di quello che dipende da Windows o dal resto di Missiva. Niente di Missiva cambia; la copia corretta `MailHtmlFix.cs` serve solo a misurare le proposte V-01, V-02, V-03, V-04 e V-07.

Su Linux l'SDK 10.0.112 è stato preso dai pacchetti di Ubuntu (`apt-get download dotnet-sdk-10.0` e dipendenze, estratti con `dpkg -x` in una cartella temporanea: il sito di download di .NET non era raggiungibile dalla rete della sessione). Su Windows basta l'SDK di .NET 10 già usato per Missiva.

### 1. Le copie dei file

In PowerShell 7 (`$R` è la cartella del repository; le righe sono quelle del commit `59ed8ba`: con un commit diverso vanno ricontrollate):

```powershell
$R = '<cartella del repository>'
$B = Join-Path $env:TEMP 'missiva-banco'
New-Item -ItemType Directory -Force (Join-Path $B 'src') | Out-Null
foreach ($f in 'MailHtml','Invitation','SearchTerms','Mailto','ContactLinks','SenderCheck') {
    Copy-Item "$R\Core\$f.cs" "$B\src\$f.cs"
}
$c = Get-Content "$R\Core\Contacts.cs" -Encoding utf8
$c[0..83] | Set-Content "$B\src\Contacts.part1.cs" -Encoding utf8                          # righe 1-84: using e Contact
($c[0..7] + $c[273..($c.Count - 1)]) | Set-Content "$B\src\Contacts.part2.cs" -Encoding utf8  # using e ContactFiles (274-fine)
$m = Get-Content "$R\Core\Models.cs" -Encoding utf8
(@('namespace Missiva;', '') + $m[108..197]) | Set-Content "$B\src\Models.part.cs" -Encoding utf8   # Address e MessageSummary
$s = Get-Content "$R\Core\Store.cs" -Encoding utf8
(@('namespace Missiva;', 'public sealed partial class Settings', '{') + $s[132..156] + '}') |
    Set-Content "$B\src\Settings.part.cs" -Encoding utf8                                      # ImageKey e i suoi elenchi
$a = Get-Content "$R\Core\AddressBook.cs" -Encoding utf8
(@('using System.Text.Json.Serialization;', 'namespace Missiva;', 'public static class AddressBook', '{',
   '    static readonly object Gate = new();',
   '    public static Dictionary<string, Entry> Book = new(StringComparer.OrdinalIgnoreCase);',
   '    static Dictionary<string, Entry> Ensure() => Book;',
   '    public static Func<IReadOnlyList<Contact>>? Contacts { get; set; }',
   '    public static string? LookAlike(Address from) => null;') +
 $a[17..54] + $a[174..180] + $a[186..222] + $a[270..282] + '}') |
    Set-Content "$B\src\AddressBook.part.cs" -Encoding utf8                                   # Entry, Wanted, Suggest, Rank
Copy-Item "$B\src\MailHtml.cs" "$B\src\MailHtmlFix.cs"
# poi, nella cartella $B: git apply mailhtmlfix.patch   (git apply funziona anche fuori da un repository)
```

Su Linux le stesse copie con `cp` e `sed -n 'A,Bp'` sugli stessi intervalli di righe.

Poi nella cartella del banco i file qui sotto (`Missiva.Bench.csproj`, `src/Stubs.cs`, `Program.cs`, `mailhtmlfix.patch`) e:

```powershell
dotnet build -c Release
dotnet bin\Release\net10.0\Missiva.Bench.dll tutto > risultati.md     # tutte le sezioni (circa un'ora qui)
dotnet bin\Release\net10.0\Missiva.Bench.dll regex                    # solo la sezione 4
dotnet bin\Release\net10.0\Missiva.Bench.dll profondo 8000 1024 testo # V-01: ToText, 8.000 livelli, stack da 1 MB (codice d'uscita)
dotnet bin\Release\net10.0\Missiva.Bench.dll eml .                    # profondo.eml, profondo-link.html, minori.html
```

Modi di `Program.cs`: `tutto`, `html` (sezione 1), `testo` (2), `altri` (3), `regex` (4), `suggerimenti` (5), `confronto` (6, usa le mediane di 1 e 2 della stessa esecuzione: va lanciato con `tutto`), `profondo N KB modo` (modo `scuro`, `chiaro`, `testo`, `analisi`, `link`), `eml cartella`, `url` (il caso di "Fuori tema").

Note sulla fedeltà: `ColorConverter.ConvertFromString` di WPF è sostituito da un finto che segue `Parsers.ParseColor` (esadecimale con 4, 5, 7 o 9 caratteri, altrimenti nome noto in maiuscolo, FormatException per il resto): le eccezioni si contano anche con `AppDomain.FirstChanceException`. I nomi di colore sono quelli di `System.Drawing.KnownColor` senza i colori di sistema e senza RebeccaPurple, cioè gli stessi 141 di WPF. Il registro è finto (conta le righe). Fra un giro e l'altro una raccolta completa; prima di tutto 60 giri di riscaldamento su una newsletter da 200 KB, perché il JIT arrivi al codice ottimizzato.

### 2. I file del banco

<details><summary>Missiva.Bench.csproj</summary>

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Missiva</RootNamespace>
    <Optimize>true</Optimize>
    <TieredPGO>true</TieredPGO>
    <ServerGarbageCollector>false</ServerGarbageCollector>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
    <InvariantGlobalization>false</InvariantGlobalization>
    <NoWarn>$(NoWarn);CS8618;CS0162;CS1998</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AngleSharp" Version="1.8.2" />
    <PackageReference Include="MailKit" Version="4.18.0" />
  </ItemGroup>
</Project>
```

</details>
<details><summary>src/Stubs.cs (i finti)</summary>

```csharp
// Finti per il banco di misura: solo i tipi che le copie dei file di Missiva usano. Nessun file copiato e cambiato.
using System.Diagnostics;

namespace Missiva
{
    public enum AccountKind { Microsoft, Gmail, Imap }
    public enum FolderKind { Inbox, Drafts, Sent, Deleted, Archive, Junk, Other }
    public sealed class ImapServer { public string ImapHost { get; set; } = ""; }
    public sealed class MailAccount
    {
        public AccountKind Kind { get; set; }
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public ImapServer? Imap { get; set; }
    }
    public sealed class MessageBody
    {
        public string Html { get; init; } = "";
        public List<Address> To { get; init; } = new();
        public List<Address> Cc { get; init; } = new();
        public List<string> AuthenticationResults { get; init; } = new();
    }
    public sealed class OutgoingMessage
    {
        public List<Address> To { get; init; } = new();
        public string Subject { get; init; } = "";
        public string Html { get; init; } = "";
        public string Text { get; init; } = "";
        public string? Calendar { get; init; }
        public string CalendarMethod { get; init; } = "";
    }
    public sealed class MailException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>Il registro finto: conta le righe, non scrive nulla.</summary>
    public static class Log
    {
        public static int Lines;
        public static void Write(string text) => Lines++;
        public static void Error(string text, Exception ex) => Lines++;
        public static void Time(string text, Stopwatch clock) => Lines++;
    }

    /// <summary>Da AccountTypes.cs: DomainOf e LooksValid copiati com'erano; IsWork finto (falso).</summary>
    public static class AccountTypes
    {
        public static string DomainOf(string email)
        {
            var address = email.Trim().ToLowerInvariant();
            int at = address.LastIndexOf('@');
            return at >= 0 ? address[(at + 1)..].TrimEnd('.') : "";
        }
        public static bool LooksValid(string email)
        {
            var address = email.Trim();
            int at = address.LastIndexOf('@');
            var domain = DomainOf(address);
            return at > 0 && !address.Any(char.IsWhiteSpace) && domain.Contains('.') && !domain.StartsWith('.') && !domain.Contains("..");
        }
        public static bool IsWork(MailAccount account) => false;
    }

}

namespace System.Windows.Media
{
    /// <summary>Il Color di WPF quanto basta a MailHtml (A, R, G, B in byte).</summary>
    public struct Color : IEquatable<Color>
    {
        public byte A { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public static Color FromRgb(byte r, byte g, byte b) => new() { A = 255, R = r, G = g, B = b };
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };
        public bool Equals(Color o) => A == o.A && R == o.R && G == o.G && B == o.B;
        public override bool Equals(object? o) => o is Color c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(A, R, G, B);
    }

    /// <summary>
    /// ColorConverter.ConvertFromString come in WPF (Parsers.ParseColor, KnownColors.MatchColor e ColorStringToKnownColor):
    /// "#" con 4, 5, 7 o 9 caratteri è esadecimale (FormatException su una cifra non valida); il resto è un nome noto, cercato
    /// in maiuscolo; un nome sconosciuto lancia FormatException. Le eccezioni sono la parte che costa: si contano.
    /// </summary>
    public static class ColorConverter
    {
        public static long Thrown;
        static readonly Dictionary<string, Color> Known = BuildKnown();

        static Dictionary<string, Color> BuildKnown()
        {
            var d = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (var kc in Enum.GetValues<System.Drawing.KnownColor>())
            {
                var c = System.Drawing.Color.FromKnownColor(kc);
                if (c.IsSystemColor || kc == System.Drawing.KnownColor.RebeccaPurple) continue; // WPF non li conosce
                d[kc.ToString().ToUpperInvariant()] = Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            return d;
        }

        static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10
            : throw Fail();

        static FormatException Fail() { Interlocked.Increment(ref Thrown); return new FormatException("Token is not valid."); }

        public static object? ConvertFromString(string? value)
        {
            if (value == null) return null;
            var t = value.Trim();
            if (t.Length is 4 or 5 or 7 or 9 && t[0] == '#')
            {
                int a = 255, r, g, b;
                if (t.Length > 7) { a = Hex(t[1]) * 16 + Hex(t[2]); r = Hex(t[3]) * 16 + Hex(t[4]); g = Hex(t[5]) * 16 + Hex(t[6]); b = Hex(t[7]) * 16 + Hex(t[8]); }
                else if (t.Length > 5) { r = Hex(t[1]) * 16 + Hex(t[2]); g = Hex(t[3]) * 16 + Hex(t[4]); b = Hex(t[5]) * 16 + Hex(t[6]); }
                else if (t.Length > 4) { a = Hex(t[1]) * 17; r = Hex(t[2]) * 17; g = Hex(t[3]) * 17; b = Hex(t[4]) * 17; }
                else { r = Hex(t[1]) * 17; g = Hex(t[2]) * 17; b = Hex(t[3]) * 17; }
                return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
            }
            if (Known.TryGetValue(t.ToUpperInvariant(), out var known)) return known;
            throw Fail();
        }
    }
}
```

</details>
<details><summary>Program.cs (misure, ingressi, regex, profondità)</summary>

```csharp
// Banco di misura per l'audit di velocità di Missiva (parte A). Compila copie dei file di Core senza dipendenze da
// Windows (MailHtml, Invitation, SearchTerms, Mailto, ContactLinks, SenderCheck, la parte vCard e CSV di Contacts) e
// misura le funzioni pure su ingressi generati qui. Uso: dotnet run -c Release -- [tutto|html|testo|altri|regex|profondo N KB modo]
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Missiva;

static class Bench
{
    static object? sink;
    static long thrown;

    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, _) => Interlocked.Increment(ref thrown);
        var what = args.Length > 0 ? args[0] : "tutto";
        if (what == "profondo") return Deep(int.Parse(args[1]), int.Parse(args[2]), args[3]);
        if (what == "eml") return ScriviEml(args.Length > 1 ? args[1] : ".");
        if (what == "ab2")
        {
            var html = DataUris(4, 768 * 1024);
            var data = html.Substring(html.IndexOf("data:"), 1 << 20);
            var css = "background-image:url(" + data + ");height:10px";
            var esc = typeof(MailHtmlFix).GetMethod("EscapeStrayLt", BindingFlags.NonPublic | BindingFlags.Static)!;
            (string, Func<object?>)[] parts =
            [
                ("RemoteUrlFunction originale, 1 MB", () => Generated(typeof(MailHtml), "RemoteUrlFunction").IsMatch(data)),
                ("RemoteUrlFunction corretta, 1 MB", () => Generated(typeof(MailHtmlFix), "RemoteUrlFunction").IsMatch(data)),
                ("CssUrl originale, stile da 1 MB", () => Generated(typeof(MailHtml), "CssUrl").Replace(css, "")),
                ("CssUrl corretta, stile da 1 MB", () => Generated(typeof(MailHtmlFix), "CssUrl").Replace(css, "")),
                ("EscapeStrayLt, email da 4 MB", () => esc.Invoke(null, [html])),
            ];
            foreach (var (name, act) in parts)
            {
                for (int i = 0; i < 5; i++) act();
                var t = new List<double>();
                for (int i = 0; i < 30; i++) { var sw = Stopwatch.StartNew(); act(); t.Add(sw.Elapsed.TotalMilliseconds); }
                t.Sort();
                Console.WriteLine($"{name}: mediana {Pct(t, 50):0.00} ms");
            }
            return 0;
        }
        if (what == "ab")
        {
            // Originale e corretta alternate sullo stesso ingresso: dice se una differenza piccola è rumore.
            var html = DataUris(4, 768 * 1024);
            var a = new List<double>();
            var f = new List<double>();
            for (int i = 0; i < 6; i++) { sink = MailHtml.Prepare(html, false, false, Cid); sink = MailHtmlFix.Prepare(html, false, false, Cid); }
            for (int i = 0; i < 30; i++)
            {
                Full(); var sw = Stopwatch.StartNew(); sink = MailHtml.Prepare(html, false, false, Cid); a.Add(sw.Elapsed.TotalMilliseconds);
                Full(); sw.Restart(); sink = MailHtmlFix.Prepare(html, false, false, Cid); f.Add(sw.Elapsed.TotalMilliseconds);
            }
            a.Sort(); f.Sort();
            Console.WriteLine($"data: URI 4 x 1 MB, chiaro, alternati 30 volte: originale mediana {Pct(a, 50):0.0} ms (95° {Pct(a, 95):0.0}), corretta {Pct(f, 50):0.0} ms (95° {Pct(f, 95):0.0})");
            return 0;
        }
        if (what == "url")
        {
            foreach (var css in new[] { "background:url('https://img.news.example/a(b).png')", "background:url(https://img.news.example/a.png)" })
            {
                var r = MailHtml.Prepare($"<div style=\"{css}\">x</div>", false, false, Cid);
                var f = MailHtmlFix.Prepare($"<div style=\"{css}\">x</div>", false, false, Cid);
                Console.WriteLine($"  corretta: bloccate {f.BlockedImages}, indirizzo ancora nella pagina: {f.Fragment.Contains("img.news.example")}");
                Console.WriteLine($"{css} -> bloccate {r.BlockedImages}, indirizzo ancora nella pagina: {r.Fragment.Contains("img.news.example")}");
            }
            return 0;
        }
        // Le misure girano in un thread con uno stack grande: i casi profondi qui misurano il tempo; quanto stack serve lo
        // dice il modo "profondo", con lo stack di un thread di Windows.
        var main = new Thread(() =>
        {
            Environment();
            WarmUp();
            if (what is "tutto" or "html") HtmlCases();
            if (what is "tutto" or "testo") TextCases();
            if (what is "tutto" or "altri") OtherCases();
            if (what is "tutto" or "regex") RegexCases();
            if (what is "tutto" or "suggerimenti") SuggestCases();
            if (what is "tutto" or "confronto") CompareCases();
        }, 512 * 1024 * 1024);
        main.Start();
        main.Join();
        return 0;
    }

    /// <summary>
    /// Prima delle misure, 60 giri di tutte le funzioni misurate su una newsletter da 200 KB: il codice passa ai livelli
    /// ottimizzati del JIT (in Missiva il punto di partenza è ReadyToRun, poi gli stessi livelli).
    /// </summary>
    static void WarmUp()
    {
        var html = Newsletter(200 * 1024);
        for (int i = 0; i < 60; i++)
        {
            sink = MailHtml.Prepare(html, i % 2 == 0, false, Cid);
            sink = MailHtmlFix.Prepare(html, i % 2 == 0, false, Cid);
            sink = MailHtml.ReadableText(html);
            sink = MailHtmlFix.ReadableText(html);
            sink = MailHtml.PlainPreview("Riepilogo &amp;zwnj; [image: x] https://news.example/a [a](https://news.example/b)");
            sink = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
        }
    }

    static void Environment()
    {
        var cpu = File.Exists("/proc/cpuinfo") ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() : "?";
        Console.WriteLine($"# processore: {cpu}; core logici: {System.Environment.ProcessorCount}; {RuntimeInformation.FrameworkDescription}; " +
                          $"{RuntimeInformation.OSDescription}; GC server {GCSettings.IsServerGC}, {GCSettings.LatencyMode}");
    }

    // ---------------------------------------------------------------- misura

    /// <summary>
    /// Un primo giro (freddo, con il JIT), due di riscaldamento, poi almeno 20 giri (100 se il giro dura meno di 5 ms; 3
    /// solo se il primo giro supera maxRunMs, e la colonna "giri" lo dice). Fra un giro e l'altro una raccolta completa: i
    /// giri sono indipendenti, e le raccolte provocate dal giro stesso restano nel suo tempo. Allocazioni del thread.
    /// </summary>
    static readonly Dictionary<string, double> medians = new();

    static double Measure(string group, string name, long inputChars, Func<object?> action, double maxRunMs = 4000)
    {
        Full();
        var sw = Stopwatch.StartNew();
        sink = action();
        double cold = sw.Elapsed.TotalMilliseconds;
        int warm = cold > maxRunMs ? 0 : 2;
        for (int i = 0; i < warm; i++) sink = action();
        int runs = cold < 5 ? 100 : cold > maxRunMs ? 3 : 20;
        var times = new List<double>(runs);
        var allocs = new List<long>(runs);
        long exStart = Interlocked.Read(ref thrown);
        for (int i = 0; i < runs; i++)
        {
            Full();
            long a = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            sink = action();
            times.Add(sw.Elapsed.TotalMilliseconds);
            allocs.Add(GC.GetAllocatedBytesForCurrentThread() - a);
        }
        double exPerRun = (double)(Interlocked.Read(ref thrown) - exStart) / runs;
        times.Sort();
        allocs.Sort();
        Console.WriteLine($"| {group} | {name} | {inputChars / 1024.0:0.#} | {Pct(times, 50):0.00} | {Pct(times, 95):0.00} | {allocs[allocs.Count / 2] / 1048576.0:0.0} | " +
                          $"{exPerRun:0} | {runs} | {cold:0.0} |");
        return medians[group + "|" + name] = Pct(times, 50);
    }

    static double Pct(List<double> sorted, int p) => sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];

    static void Full()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"## {title}");
        Console.WriteLine("| gruppo | caso | ingresso KB (caratteri/1024) | mediana ms | 95° percentile ms | MB allocati (mediana) | eccezioni per giro | giri | primo giro ms |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
    }

    // ---------------------------------------------------------------- email generate

    static readonly string[] Words = "fattura ordine spedizione cliente offerta prodotto settimana novità consegna pagamento conferma prezzo servizio account aggiornamento sconto evento iscrizione notizia articolo".Split(' ');

    static string Lorem(Random r, int words)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words; i++) sb.Append(i == 0 ? "" : " ").Append(Words[r.Next(Words.Length)]);
        return sb.Append('.').ToString();
    }

    /// <summary>Una newsletter come quelle vere: tabelle da 600 pixel, stili negli elementi, immagini esterne, pixel di tracciamento, pulsanti.</summary>
    public static string Newsletter(int targetChars, bool ownDark = false, int seed = 1)
    {
        var r = new Random(seed);
        var sb = new StringBuilder(targetChars + 4096);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Notizie</title><style>");
        sb.Append("body{margin:0;padding:0;background-color:#f2f2f2;font-family:Arial,Helvetica,sans-serif}");
        for (int i = 0; i < 40; i++)
            sb.Append($".c{i}{{color:#{r.Next(0x1000000):X6};background-color:#{r.Next(0xE0E0E0, 0x1000000):X6};border:1px solid #e0e0e0;padding:{i % 12}px}}");
        sb.Append("@media only screen and (max-width:600px){.wrap{width:100%!important}.col{display:block!important;width:100%!important}}");
        if (ownDark) sb.Append("@media (prefers-color-scheme: dark){.wrap{background:#1e1e1e!important;color:#eeeeee!important}}");
        sb.Append("</style></head><body style=\"margin:0;background-color:#f2f2f2\" bgcolor=\"#f2f2f2\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"#f2f2f2\"><tr><td align=\"center\">");
        sb.Append("<table class=\"wrap\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:600px;background-color:#ffffff;border:1px solid #dddddd\">");
        int n = 0;
        while (sb.Length < targetChars - 120)
        {
            n++;
            sb.Append($"<tr><td class=\"col c{n % 40}\" style=\"padding:16px 24px;background-color:#ffffff;border-bottom:1px solid #eeeeee\">");
            sb.Append($"<a href=\"https://news.example/c/{n}?utm_source=nl&amp;utm_medium=email&amp;id={r.Next()}\" target=\"_blank\"><img src=\"https://img.news.example/a/{n}.jpg\" width=\"552\" height=\"276\" alt=\"Articolo {n}\" style=\"display:block;width:100%;height:auto;border:0\"></a>");
            sb.Append($"<h2 style=\"margin:12px 0 4px;font-size:20px;line-height:26px;color:#1a1a1a;font-family:Georgia,serif\">Titolo dell'articolo numero {n}</h2>");
            sb.Append($"<p style=\"margin:0 0 12px;font-size:15px;line-height:22px;color:#555555\">{Lorem(r, 60)}</p>");
            sb.Append($"<table cellpadding=\"0\" cellspacing=\"0\"><tr><td bgcolor=\"#0a66c2\" style=\"border-radius:4px;background-color:#0a66c2\"><a href=\"https://news.example/l/{n}\" style=\"display:inline-block;padding:10px 18px;color:#ffffff;font-weight:bold;text-decoration:none\">Leggi tutto</a></td></tr></table>");
            sb.Append("<font color=\"#888888\" size=\"2\">Pubblicato oggi</font>");
            sb.Append($"<img src=\"https://t.news.example/o/{n}.gif\" width=\"1\" height=\"1\" alt=\"\">");
            sb.Append("</td></tr>");
        }
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    static string Nested(int depth)
    {
        var sb = new StringBuilder("<html><body>");
        for (int d = 0; d < depth; d++)
            sb.Append($"<table width=\"100%\" style=\"border:1px solid #cccccc;background-color:#fafafa\"><tr><td style=\"padding:2px;color:#333333\">Livello {d} <a href=\"https://x.example/{d}\">link</a>");
        sb.Append("Fondo");
        for (int d = 0; d < depth; d++) sb.Append("</td></tr></table>");
        return sb.Append("</body></html>").ToString();
    }

    static string InlineStyles(int count)
    {
        var sb = new StringBuilder("<html><body><div>");
        for (int i = 0; i < count; i++)
            sb.Append($"<span style=\"color:#333333;background-color:#f4f4f4;border:1px solid #dddddd;font-size:14px;padding:4px 6px\">voce {i}</span><br>");
        return sb.Append("</div></body></html>").ToString();
    }

    static string StyleRules(int count)
    {
        var sb = new StringBuilder("<html><head><style>");
        for (int i = 0; i < count; i++)
            sb.Append($".r{i} td, body .r{i} > a {{ color: #333333; background: #fafafa url(https://img.news.example/bg{i}.png) no-repeat center top; border: 1px solid #cccccc; padding: 4px; }}\n");
        sb.Append("</style></head><body>");
        for (int i = 0; i < 50; i++) sb.Append($"<p class=\"r{i}\">paragrafo {i}</p>");
        return sb.Append("</body></html>").ToString();
    }

    static string DataUris(int images, int bytesEach)
    {
        var r = new Random(3);
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < images; i++)
        {
            var data = new byte[bytesEach];
            r.NextBytes(data);
            var b64 = Convert.ToBase64String(data);
            if (i == 0) sb.Append($"<div style=\"background-image:url(data:image/png;base64,{b64});height:10px\">fondo</div>");
            else sb.Append($"<img src=\"data:image/png;base64,{b64}\" width=\"600\" alt=\"immagine {i}\">");
        }
        return sb.Append("<p>testo</p></body></html>").ToString();
    }

    static string Wrap(string body) => "<html><head></head><body>" + body + "</body></html>";

    static string Repeat(string s, int times) => new StringBuilder(s.Length * times).Insert(0, s, times).ToString();

    static readonly Func<string, string?> Cid = id => "https://missiva.example/cid/" + id;

    static void Prepare(string name, string html)
    {
        Measure("solo analisi AngleSharp", name, html.Length, () => new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html));
        Measure("Prepare", name + ", scuro", html.Length, () => MailHtml.Prepare(html, true, false, Cid));
        Measure("Prepare", name + ", chiaro", html.Length, () => MailHtml.Prepare(html, false, false, Cid));
    }

    static List<(string Name, string Html)> Inputs()
    {
        return
        [
            ("newsletter 50 KB", Newsletter(50 * 1024)),
            ("newsletter 200 KB", Newsletter(200 * 1024)),
            ("newsletter 1 MB", Newsletter(1024 * 1024)),
            ("newsletter 5 MB", Newsletter(5 * 1024 * 1024)),
            ("newsletter 200 KB con tema scuro proprio", Newsletter(200 * 1024, ownDark: true)),
            ("tabelle annidate 10", Nested(10)),
            ("tabelle annidate 50", Nested(50)),
            ("tabelle annidate 200", Nested(200)),
            ("tabelle annidate 1000", Nested(1000)),
            ("5000 stili negli elementi", InlineStyles(5000)),
            ("<style> con 2000 regole", StyleRules(2000)),
            ("data: URI 4 x 1 MB", DataUris(4, 768 * 1024)),
            ("100.000 \"<\"", Wrap("<p>" + new string('<', 100_000) + "</p>")),
            ("attributo title da 1 MB", Wrap("<div title=\"" + new string('a', 1 << 20) + "\">x</div>")),
            ("attributo style da 1 MB", Wrap("<div style=\"" + Repeat("color:#123456;", (1 << 20) / 14) + "\">x</div>")),
            ("5000 <div> non chiusi", Wrap(Repeat("<div>x", 5000))),
            ("20.000 <div> non chiusi", Wrap(Repeat("<div>x", 20000))),
            ("20.000 <b><i> non chiusi", Wrap(Repeat("<b><i>x", 20000))),
            ("\"<meta \" ripetuto 10.000 volte (60 KB)", "<html><head>" + Repeat("<meta ", 10000) + "</head><body>x</body></html>"),
            ("style con \"url(\" ripetuto 10.000 volte (40 KB)", Wrap("<div style=\"background:" + Repeat("url(", 10000) + "\">x</div>")),
            ("src con \"pixel\" ripetuto 20.000 volte", Wrap("<img src=\"https://img.news.example/" + Repeat("pixel", 20000) + "\">")),
            ("attributo con url( e 30.000 spazi", Wrap("<div data-x=\"url(" + new string(' ', 30_000) + "x\">x</div>")),
            ("style con parola da 30.000 lettere e una \\", Wrap("<div style=\"font-family:" + new string('a', 30_000) + " \\\">x</div>")),
        ];
    }

    static void HtmlCases()
    {
        Header("1. MailHtml.Prepare (immagini non consentite; scuro e chiaro)");
        foreach (var (name, html) in Inputs()) Prepare(name, html);
    }

    static void TextCases()
    {
        Header("2. MailHtml.PlainPreview, ToText, ReadableText sugli stessi ingressi");
        foreach (var (name, html) in Inputs())
        {
            Measure("PlainPreview", name, html.Length, () => MailHtml.PlainPreview(html));
            Measure("ToText", name, html.Length, () => MailHtml.ToText(html));
            Measure("ReadableText", name, html.Length, () => MailHtml.ReadableText(html));
        }
        // Le anteprime vere sono brevi (Graph bodyPreview fino a 255 caratteri): il caso d'uso reale.
        var preview = "Riepilogo annunci &amp;zwnj; &amp;zwnj; [image: Logo] Leggi qui https://news.example/l/123?utm=1 [Leggi da browser](https://news.example/b) " +
                      "<https://news.example/x> <mailto:info@news.example> testo\u200B\u200B\u200B fine &zw…";
        Measure("PlainPreview", "anteprima vera da " + preview.Length + " caratteri", preview.Length, () => MailHtml.PlainPreview(preview));
        var hostile = new string('[', 255);
        Measure("PlainPreview", "255 \"[\" (il massimo di Graph)", hostile.Length, () => MailHtml.PlainPreview(hostile));
    }

    // ---------------------------------------------------------------- inviti, contatti, ricerca

    static string Ics(string variant)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append("\r\n");
        L("BEGIN:VCALENDAR"); L("PRODID:-//Prova//Prova//IT"); L("VERSION:2.0"); L("METHOD:REQUEST");
        L("BEGIN:VTIMEZONE"); L("TZID:Customized Time Zone");
        L("BEGIN:STANDARD"); L("DTSTART:16010101T030000"); L("TZOFFSETFROM:+0200"); L("TZOFFSETTO:+0100"); L("RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10"); L("END:STANDARD");
        L("BEGIN:DAYLIGHT"); L("DTSTART:16010101T020000"); L("TZOFFSETFROM:+0100"); L("TZOFFSETTO:+0200"); L("RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3"); L("END:DAYLIGHT");
        L("END:VTIMEZONE");
        L("BEGIN:VEVENT"); L("UID:prova-12345@esempio.example"); L("SEQUENCE:0");
        L("DTSTART;TZID=\"Customized Time Zone\":20261025T100000"); L("DTEND;TZID=\"Customized Time Zone\":20261025T110000");
        L("SUMMARY:Riunione di prova"); L("LOCATION:Sala 1"); L("RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261231T000000Z");
        L("ORGANIZER;CN=\"Mario Rossi\":mailto:mario.rossi@esempio.example");
        L("X-MICROSOFT-SKYPETEAMSMEETINGURL:https://riunioni.example/l/meetup-join/123");
        int limit = Invitation.MaxLength - 400;
        if (variant == "partecipanti")
            for (int i = 0; sb.Length < limit; i++) L($"ATTENDEE;CN=\"Persona {i}\";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:persona.{i}@esempio.example");
        else if (variant == "descrizione")
        {
            var line = new StringBuilder("DESCRIPTION:");
            while (line.Length < (limit - sb.Length) * 74 / 78) line.Append("Testo della riunione\\, con un a capo\\n e un link https://riunioni.example/x ");
            var text = line.ToString();
            sb.Append(text[..75]);
            for (int k = 75; k < text.Length; k += 74) sb.Append("\r\n ").Append(text[k..Math.Min(text.Length, k + 74)]);
            sb.Append("\r\n");
        }
        else // righe: vicino al limite di 5000 righe
            for (int i = 0; i < 4950; i++) L($"X-PROVA-{i};A=1:valore {i}");
        L("BEGIN:VALARM"); L("TRIGGER:-PT15M"); L("ACTION:DISPLAY"); L("END:VALARM");
        L("END:VEVENT"); L("END:VCALENDAR");
        return sb.ToString();
    }

    static string VCards(bool quotedPrintable)
    {
        var r = new Random(5);
        var sb = new StringBuilder();
        for (int i = 0; i < 5000 && sb.Length < 5 * 1024 * 1024 - 1200; i++)
        {
            sb.Append(quotedPrintable ? "BEGIN:VCARD\r\nVERSION:2.1\r\n" : "BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append($"N:Rossi{i};Mario;;;\r\nFN:Mario Rossi {i}\r\n");
            sb.Append($"EMAIL;TYPE=INTERNET:mario.rossi{i}@esempio.example\r\nitem1.EMAIL;TYPE=INTERNET:m.rossi{i}@prova.example\r\n");
            sb.Append($"TEL;TYPE=CELL:+39 333 000 {i:0000}\r\nTEL;TYPE=WORK:+39 02 0000 {i:0000}\r\nTEL;TYPE=HOME:+39 06 0000 {i:0000}\r\n");
            sb.Append($"ORG:Ditta di prova {i}\r\nTITLE:Responsabile\r\nADR;TYPE=WORK:;;Via Roma {i};Milano;;20100;Italia\r\nURL:https://ditta{i}.example\r\n");
            var note = Lorem(r, 80) + " Partita IVA 000000000" + (i % 10) + ". àèìòù";
            if (quotedPrintable)
            {
                var bytes = Encoding.UTF8.GetBytes(note);
                var qp = new StringBuilder("NOTE;ENCODING=QUOTED-PRINTABLE;CHARSET=UTF-8:");
                int col = qp.Length;
                foreach (var b in bytes)
                {
                    var piece = b is >= 33 and <= 126 and not (byte)'=' ? ((char)b).ToString() : $"={b:X2}";
                    if (col + piece.Length > 75) { qp.Append("=\r\n"); col = 0; }
                    qp.Append(piece);
                    col += piece.Length;
                }
                sb.Append(qp).Append("\r\n");
            }
            else sb.Append("NOTE:").Append(note.Replace(",", "\\,")).Append("\r\n");
            sb.Append("END:VCARD\r\n");
        }
        return sb.ToString();
    }

    static string Csv(int rows)
    {
        var sb = new StringBuilder("First Name,Last Name,E-mail Address,E-mail 2 Address,Mobile Phone,Business Phone,Company,Job Title,Notes\r\n");
        for (int i = 0; i < rows; i++)
            sb.Append($"Mario,Rossi {i},mario.rossi{i}@esempio.example,m.rossi{i}@prova.example,+39 333 000 {i:0000},+39 02 0000 {i:0000},\"Ditta, di prova {i}\",Responsabile,\"Nota con virgole, \"\"virgolette\"\" e\r\nun a capo\"\r\n");
        return sb.ToString();
    }

    static void OtherCases()
    {
        Header("3. Invitation, vCard e CSV, SearchTerms, Mailto, ContactLinks, SenderCheck");
        foreach (var v in new[] { "partecipanti", "descrizione", "righe" })
        {
            var ics = Ics(v);
            Measure("Invitation.Parse", $".ics da {ics.Length / 1024} KB ({v})", ics.Length, () => Invitation.Parse(ics) ?? throw new Exception("invito non letto"));
        }
        var vcf = VCards(false);
        Measure("ContactFiles.VCard", $"vCard 3.0, {vcf.Length / 1048576.0:0.0} MB", vcf.Length, () => ContactFiles.VCard(vcf).Count);
        var vqp = VCards(true);
        Measure("ContactFiles.VCard", $"vCard 2.1 quoted-printable, {vqp.Length / 1048576.0:0.0} MB", vqp.Length, () => ContactFiles.VCard(vqp).Count);
        var csv = Csv(5000);
        Measure("ContactFiles.Csv", "CSV di Outlook, 5000 contatti", csv.Length, () => ContactFiles.Csv(csv).Count);
        Console.Error.WriteLine($"controllo: vCard {ContactFiles.VCard(vcf).Count}, QP {ContactFiles.VCard(vqp).Count}, CSV {ContactFiles.Csv(csv).Count} contatti");

        var query = "fattura da:\"Mario Rossi\" a:info oggetto:ordine dal:1/3/2026 al:31/3/2026 ha:allegati è:nonletto";
        Measure("SearchTerms.Parse", "ricerca con 7 filtri (x1000)", query.Length, () => { object? o = null; for (int i = 0; i < 1000; i++) o = SearchTerms.Parse(query); return o; });
        var longQuery = string.Join(' ', Enumerable.Range(0, 2000).Select(i => i % 10 == 0 ? $"da:persona{i}" : "parola" + i));
        Measure("SearchTerms.Parse", "ricerca di 2000 parole, 200 da:", longQuery.Length, () => SearchTerms.Parse(longQuery));
        var terms = SearchTerms.Parse(query);
        var page = Enumerable.Range(0, 10_000).Select(i => new MessageSummary
        {
            Id = i.ToString(), FolderId = "INBOX", Date = new DateTime(2026, 3, 1).AddHours(i % 1000), IsRead = i % 3 == 0, IsFlagged = i % 7 == 0,
        }).ToList();
        Measure("SearchTerms.Matches", "10.000 messaggi", 0, () => page.Count(terms.Matches));
        Measure("SearchTerms.ToGmail/ToKql/Filter", "x1000", query.Length, () => { object? o = null; for (int i = 0; i < 1000; i++) o = terms.ToGmail() + terms.ToKql() + terms.Filter(); return o; });

        var mailto = "mailto:mario.rossi@esempio.example,maria.rossi@esempio.example?cc=info%40prova.example&subject=Ciao%20a%20tutti&body=" +
                     Uri.EscapeDataString(Repeat("Riga di testo con àèìòù.\r\n", 400));
        Measure("Mailto.Parse", $"link con testo da {mailto.Length / 1024} KB", mailto.Length, () => Mailto.Parse(mailto));
        var phones = Enumerable.Range(0, 4500).Select(i => i % 5 == 0 ? "1-800-FIORI" : $"+39 (02) {i:0000}-{i:000}.00").ToList();
        Measure("ContactLinks.NormalizePhone", "4500 numeri", 0, () => phones.Count(p => ContactLinks.NormalizePhone(p) != null));
        var urls = Enumerable.Range(0, 4500).Select(i => i % 3 == 0 ? $"www.ditta{i}.example/pagina?x={i}" : i % 3 == 1 ? $"tel:+3902{i:000000}" : ContactLinks.GoogleContactsUrl("people/c" + i)).ToList();
        Measure("ContactLinks.IsAllowed", "4500 indirizzi", 0, () => urls.Count(ContactLinks.IsAllowed));
        var auth = "mx.esempio.example; dkim=pass header.i=@esempio.example header.s=s1 header.b=abc; spf=pass (esempio.example: dominio di prova (commento (annidato))) smtp.mailfrom=prova@esempio.example; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from=esempio.example";
        Measure("SenderCheck.Parse", "intestazione vera (x1000)", auth.Length, () => { object? o = null; for (int i = 0; i < 1000; i++) o = SenderCheck.Parse(auth); return o; });
        var longAuth = Repeat("dkim=pass (" + new string('x', 50) + ") header.i=@esempio.example; ", 1000);
        Measure("SenderCheck.Parse", "intestazione da 1000 parti", longAuth.Length, () => SenderCheck.Parse(longAuth));
    }

    // ---------------------------------------------------------------- espressioni regolari

    sealed record Rx(string Where, string Name, Regex Regex, string Use, Func<int, string> Hostile, string HostileText);

    static Regex Generated(Type type, string method) =>
        (Regex)type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

    static List<Rx> Regexes()
    {
        var h = typeof(MailHtml);
        var c = typeof(ContactLinks);
        string R(string s, int n) => Repeat(s, Math.Max(1, n / s.Length));
        return
        [
            new("MailHtml.cs:201", "BlankLines", Generated(h, "BlankLines"), "Replace", n => R("\n\nx", n), "\"\\n\\nx\" ripetuto"),
            new("MailHtml.cs:248", "CutEntity", Generated(h, "CutEntity"), "Replace", n => R("&ab", n), "\"&ab\" ripetuto"),
            new("MailHtml.cs:251", "ImagePlaceholder", Generated(h, "ImagePlaceholder"), "Replace", n => R("[image:", n), "\"[image:\" ripetuto senza ]"),
            new("MailHtml.cs:254", "MarkdownLink", Generated(h, "MarkdownLink"), "Replace", n => new string('[', n), "\"[\" ripetuto"),
            new("MailHtml.cs:261", "CutMarkdownLink", Generated(h, "CutMarkdownLink"), "Replace", n => new string('[', n), "\"[\" ripetuto"),
            new("MailHtml.cs:268", "WebAddress", Generated(h, "WebAddress"), "Replace", n => R("( <\t", n), "\"( <\\t\" ripetuto"),
            new("MailHtml.cs:272", "AngleMailto", Generated(h, "AngleMailto"), "Replace", n => "<" + new string(' ', n) + "x", "\"<\", n spazi, \"x\""),
            new("MailHtml.cs:276", "EdgeBracket", Generated(h, "EdgeBracket"), "Replace", n => R(" <", n), "\" <\" ripetuto"),
            new("MailHtml.cs:279", "Invisible", Generated(h, "Invisible"), "Replace", n => new string('\u200B', n), "U+200B ripetuto"),
            new("MailHtml.cs:313", "SpacerName", Generated(h, "SpacerName"), "IsMatch", n => R("pixel", n), "\"pixel\" ripetuto senza .gif"),
            new("MailHtml.cs:316", "OwnDarkMode", Generated(h, "OwnDarkMode"), "IsMatch", n => R("<meta ", n), "\"<meta \" ripetuto senza >"),
            new("MailHtml.cs:353", "RemoteUrlFunction", Generated(h, "RemoteUrlFunction"), "IsMatch", n => "url(" + new string(' ', n) + "x", "\"url(\", n spazi, \"x\""),
            new("MailHtml.cs:363", "WhitespaceRun", Generated(h, "WhitespaceRun"), "Replace", n => R(" x", n), "\" x\" ripetuto"),
            new("MailHtml.cs:366", "UrlInText", Generated(h, "UrlInText"), "Replace", n => R("http://.", n), "\"http://.\" ripetuto"),
            new("MailHtml.cs:370", "RootSelectors", Generated(h, "RootSelectors"), "Replace", n => R("{html-", n), "\"{html-\" ripetuto"),
            new("MailHtml.cs:375", "CssUrl", Generated(h, "CssUrl"), "Replace", n => R("url(", n), "\"url(\" ripetuto senza )"),
            new("MailHtml.cs:378", "CssImport", Generated(h, "CssImport"), "Replace", n => R("@import", n), "\"@import\" ripetuto"),
            new("MailHtml.cs:382", "CssImageSet", Generated(h, "CssImageSet"), "Replace", n => R("image-set(", n), "\"image-set(\" ripetuto"),
            new("MailHtml.cs:389", "CssEscapedFunction", Generated(h, "CssEscapedFunction"), "Replace", n => new string('a', n) + " \\", "n lettere, uno spazio, una \\"),
            new("MailHtml.cs:423", "ColorDeclaration", Generated(h, "ColorDeclaration"), "Replace", n => "color" + new string(' ', n) + "x", "\"color\", n spazi, \"x\""),
            new("MailHtml.cs:426", "ColorToken", Generated(h, "ColorToken"), "Replace", n => R("url(", n), "\"url(\" ripetuto senza )"),
            new("MailHtml.cs:426", "ColorToken (rgb)", Generated(h, "ColorToken"), "Replace", n => R("rgb(", n), "\"rgb(\" ripetuto senza )"),
            new("MailHtml.cs:446", "CssBlock", Generated(h, "CssBlock"), "Replace", n => R("{}a", n), "\"{}a\" ripetuto"),
            new("ContactLinks.cs:42", "PersonId", Generated(c, "PersonId"), "IsMatch", n => "people/" + new string('a', n), "\"people/\" e n lettere"),
            new("ContactLinks.cs:45", "GoogleAddress", Generated(c, "GoogleAddress"), "IsMatch", n => ContactLinks.GoogleContactsUrl(null) + "person/" + new string('a', n), "indirizzo con n lettere"),
            new("ContactLinks.cs:48", "AccountEmail", Generated(c, "AccountEmail"), "IsMatch", n => new string('a', n) + "@", "n lettere e @"),
            new("ContactLinks.cs:51", "TelAddress", Generated(c, "TelAddress"), "IsMatch", n => "tel:+" + new string('1', n), "\"tel:+\" e n cifre"),
            new("Invitation.cs:122", "Text (\\\\n)", new Regex(@"\\([nN,;\\])"), "Replace", n => new string('\\', n), "\"\\\" ripetuto"),
            new("Invitation.cs:204", "^mailto:", new Regex("^mailto:", RegexOptions.IgnoreCase), "Replace", n => new string('m', n), "n lettere"),
            new("Invitation.cs:246", "Offset", new Regex(@"^([+-])(\d{2})(\d{2})"), "Match", n => "+" + new string('1', n), "\"+\" e n cifre"),
            new("Invitation.cs:282", "Duration", new Regex(@"^P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$"), "Match", n => "P" + new string('1', n) + "X", "\"P\", n cifre, \"X\" (nel codice solo fino a 64)"),
            new("Invitation.cs:298", "BYDAY", new Regex(@"^([+-]?\d*)([A-Z]{2})$"), "Match", n => new string('1', n) + "MOX", "n cifre e \"MOX\""),
            new("Invitation.cs:332", "link https", new Regex(@"https://[^\s<>""]+"), "Matches", n => R("https://", n), "\"https://\" ripetuto"),
            new("Invitation.cs:492", "link https (corpo)", new Regex(@"https://[^\s<>""']+"), "Matches", n => R("https:/ ", n), "\"https:/ \" ripetuto"),
            new("proposta", "SpacerName NonBacktracking", Generated(typeof(MailHtmlFix), "SpacerName"), "IsMatch", n => R("pixel", n), "\"pixel\" ripetuto senza .gif"),
            new("proposta", "OwnDarkMode NonBacktracking", Generated(typeof(MailHtmlFix), "OwnDarkMode"), "IsMatch", n => R("<meta ", n), "\"<meta \" ripetuto senza >"),
            new("proposta", "RemoteUrlFunction riscritta", Generated(typeof(MailHtmlFix), "RemoteUrlFunction"), "IsMatch", n => "url(" + new string(' ', n) + "x", "\"url(\", n spazi, \"x\""),
            new("proposta", "CssUrl riscritta", Generated(typeof(MailHtmlFix), "CssUrl"), "Replace", n => R("url(", n), "\"url(\" ripetuto senza )"),
            new("proposta", "CssUrl riscritta, apici", Generated(typeof(MailHtmlFix), "CssUrl"), "Replace", n => R("url('", n), "\"url('\" ripetuto"),
            new("proposta", "CssEscapedFunction NonBacktracking", Generated(typeof(MailHtmlFix), "CssEscapedFunction"), "Replace", n => new string('a', n) + " \\", "n lettere, uno spazio, una \\"),
            new("proposta", "ColorToken riscritta", Generated(typeof(MailHtmlFix), "ColorToken"), "Replace", n => R("url(", n), "\"url(\" ripetuto senza )"),
            new("SenderCheck.cs:46", "risultato", new Regex(@"^([A-Za-z][A-Za-z0-9-]*)\s*=\s*([A-Za-z]+)"), "Match", n => new string('a', n) + new string(' ', n) + "!", "n lettere, n spazi, \"!\""),
        ];
    }

    static double Time(Rx rx, string input)
    {
        var sw = Stopwatch.StartNew();
        sink = rx.Use switch
        {
            "Replace" => rx.Regex.Replace(input, ""),
            "IsMatch" => rx.Regex.IsMatch(input),
            "Match" => rx.Regex.Match(input).Success,
            _ => rx.Regex.Matches(input).Count,
        };
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Per ogni regex un ingresso scelto per metterla in difficoltà, a lunghezze doppie da 4.000 caratteri: mediana di 5 giri
    /// per lunghezza (1 sopra il secondo), fino a 256.000 caratteri o finché un giro passa 3 s. L'esponente è il log2 del
    /// rapporto fra gli ultimi due tempi: circa 1 lineare, circa 2 quadratico.
    /// </summary>
    static void RegexCases()
    {
        Console.WriteLine();
        Console.WriteLine("## 4. Espressioni regolari con ingressi ostili");
        Console.WriteLine("| dove | regex | uso | ingresso | ms a 4k | 8k | 16k | 32k | 64k | 128k | 256k | esponente |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var rx in Regexes())
        {
            Time(rx, rx.Hostile(100)); // JIT
            var cells = new List<string>();
            var measured = new List<(int N, double Ms)>();
            for (int n = 4000; n <= 256_000; n *= 2)
            {
                var input = rx.Hostile(n);
                var first = Time(rx, input);
                double ms = first;
                if (first < 1000)
                {
                    var t = new List<double> { first };
                    for (int i = 0; i < 4; i++) t.Add(Time(rx, input));
                    t.Sort();
                    ms = t[2];
                }
                measured.Add((input.Length, ms));
                cells.Add(ms < 0.1 ? $"{ms:0.000}" : ms < 10 ? $"{ms:0.00}" : $"{ms:0}");
                if (ms > 3000) break;
            }
            while (cells.Count < 7) cells.Add("-");
            var (n1, t1) = measured[^2];
            var (n2, t2) = measured[^1];
            double exponent = t1 > 0.05 ? Math.Log(t2 / t1) / Math.Log((double)n2 / n1) : double.NaN;
            Console.WriteLine($"| {rx.Where} | {rx.Name} | {rx.Use} | {rx.HostileText} | {string.Join(" | ", cells)} | {(double.IsNaN(exponent) ? "troppo veloce" : exponent.ToString("0.0"))} |");
        }
    }

    // ---------------------------------------------------------------- confronto con le correzioni proposte

    /// <summary>
    /// MailHtmlFix (copia di MailHtml con le correzioni proposte) sugli stessi ingressi: tempo e uscita uguale o no.
    /// I tempi dell'originale sono quelli delle sezioni 1 e 2 di questa stessa esecuzione.
    /// </summary>
    static void CompareCases()
    {
        Console.WriteLine();
        Console.WriteLine("## 6. Con le correzioni proposte (MailHtmlFix): stessi ingressi");
        Console.WriteLine("| funzione | caso | originale mediana ms | corretta mediana ms | uscita uguale |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var (name, html) in Inputs())
        {
            foreach (var dark in new[] { true, false })
            {
                var label = name + (dark ? ", scuro" : ", chiaro");
                var fixedMs = Measure("Prepare corretto", label, html.Length, () => MailHtmlFix.Prepare(html, dark, false, Cid));
                bool same = MailHtml.Prepare(html, dark, false, Cid).Fragment == MailHtmlFix.Prepare(html, dark, false, Cid).Fragment;
                Console.WriteLine($"| Prepare | {label} | {medians.GetValueOrDefault("Prepare|" + label):0.00} | {fixedMs:0.00} | {(same ? "sì" : "NO")} |");
            }
            var textMs = Measure("ToText corretto", name, html.Length, () => MailHtmlFix.ToText(html));
            bool sameText = MailHtml.ToText(html) == MailHtmlFix.ToText(html);
            Console.WriteLine($"| ToText | {name} | {medians.GetValueOrDefault("ToText|" + name):0.00} | {textMs:0.00} | {(sameText ? "sì" : "NO")} |");
        }
    }

    // ---------------------------------------------------------------- suggerimenti, a ogni lettera

    /// <summary>
    /// AddressBook.Suggest (casella Cerca e campi A, Cc, Ccn) con 5000 voci della rubrica e 4500 contatti, e il filtro di
    /// Persone (Contact.Matches e l'ordine per nome di BuildEntries, PeopleView.Sections.cs:95) sugli stessi 4500 contatti,
    /// per ogni lettera di "mario r". Cultura it-IT, come sul PC dell'autore.
    /// </summary>
    static void SuggestCases()
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
        var r = new Random(7);
        // Solo nomi finti: Mario e Maria Rossi più nomi numerati, con la stessa varietà di 12 nomi e 10 cognomi.
        string[] names = ["Mario", "Maria", .. Enumerable.Range(1, 10).Select(k => "Nome" + k)];
        string[] surnames = ["Rossi", .. Enumerable.Range(1, 9).Select(k => "Cognome" + k)];
        for (int i = 0; i < 5000; i++)
        {
            var n = names[r.Next(names.Length)];
            var s = surnames[r.Next(surnames.Length)];
            var email = i % 10 == 0 ? $"no-reply{i}@servizio{i % 300}.example" : $"{n.ToLowerInvariant()}.{s.ToLowerInvariant()}{i}@dominio{i % 300}.example";
            AddressBook.Book[email] = new AddressBook.Entry { Name = $"{n} {s}", Email = email, Received = r.Next(50), Sent = r.Next(5), LastReceived = DateTime.Now.AddDays(-r.Next(700)) };
        }
        var contacts = new List<Contact>();
        for (int i = 0; i < 4500; i++)
        {
            var n = names[r.Next(names.Length)];
            var s = surnames[r.Next(surnames.Length)];
            contacts.Add(new Contact
            {
                GivenName = n, Surname = s, Emails = [$"{n.ToLowerInvariant()}.{s.ToLowerInvariant()}.c{i}@ditta{i % 200}.example", $"{n[0]}{s}{i}@posta.example".ToLowerInvariant()],
                Company = $"Ditta di prova {i % 200}", JobTitle = "Responsabile", Mobile = $"+39 333 000 {i:0000}", Notes = Lorem(r, 30),
            });
        }
        AddressBook.Contacts = () => contacts.ToList(); // ForSuggestions copia l'elenco a ogni chiamata (Contacts.cs:263)
        Header("5. Suggerimenti a ogni lettera: AddressBook.Suggest (5000 voci, 4500 contatti) e ricerca di Persone (4500 contatti)");
        var typed = "mario r";
        for (int k = 1; k <= typed.Length; k++)
        {
            var q = typed[..k];
            if (q.EndsWith(' ')) continue;
            Measure("AddressBook.Suggest", $"\"{q}\", 8 proposte", 0, () => AddressBook.Suggest(q, 8));
            Measure("Persone: Matches + OrderBy", $"\"{q}\"", 0, () => contacts.Where(c => c.Matches(q)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        }
    }

    /// <summary>
    /// Per la verifica di V-01 su Windows: profondo.eml (8.000 &lt;div&gt; non chiusi, da aprire con --eml nella versione di
    /// prova e poi Rispondi), profondo-link.html (un link con 12.000 &lt;span&gt;, per Missiva.exe --page) e
    /// minori.html (40.000 "a &lt; b", per V-02). Solo nomi e indirizzi finti.
    /// </summary>
    static int ScriviEml(string dir)
    {
        var html = Wrap(Repeat("<div>x", 8000));
        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress("Mario Rossi", "mario.rossi@esempio.example"));
        message.To.Add(new MimeKit.MailboxAddress("Maria Rossi", "maria.rossi@esempio.example"));
        message.Subject = "Prova di annidamento";
        message.Body = new MimeKit.TextPart("html") { Text = html };
        message.WriteTo(Path.Combine(dir, "profondo.eml"));
        File.WriteAllText(Path.Combine(dir, "profondo-link.html"), "<html><body><a href=\"https://news.example/\">" + Repeat("<span>x", 12000) + "</a></body></html>");
        File.WriteAllText(Path.Combine(dir, "minori.html"), Wrap("<p>" + Repeat("a < b ", 40000) + "</p>"));
        Console.WriteLine("scritti profondo.eml, profondo-link.html, minori.html in " + Path.GetFullPath(dir));
        return 0;
    }

    // ---------------------------------------------------------------- DOM profondo, in un processo a parte

    /// <summary>
    /// Prepare (o ToText) su N &lt;div&gt; non chiusi, in un thread con lo stack dato (KB): un overflow dello stack chiude il
    /// processo, quindi si prova in un processo a parte. Codice d'uscita 0 se finisce.
    /// </summary>
    static int Deep(int n, int stackKb, string mode)
    {
        var html = Wrap(Repeat("<div>x", n));
        int code = 1;
        var thread = new Thread(() =>
        {
            var sw = Stopwatch.StartNew();
            sink = mode switch
            {
                "scuro" => MailHtml.Prepare(html, true, false, Cid),
                "chiaro" => MailHtml.Prepare(html, false, false, Cid),
                "analisi" => new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html),
                "link" => MailHtml.Prepare("<html><body><a href=\"https://x.example/\">" + Repeat("<span>x", n) + "</a></body></html>", false, false, Cid),
                "testo-corretto" => MailHtmlFix.ToText(html),
                "link-corretto" => MailHtmlFix.Prepare("<html><body><a href=\"https://x.example/\">" + Repeat("<span>x", n) + "</a></body></html>", false, false, Cid),
                _ => MailHtml.ToText(html),
            };
            Console.WriteLine($"profondo {n}, stack {stackKb} KB, {mode}: finito in {sw.Elapsed.TotalMilliseconds:0} ms");
            code = 0;
        }, stackKb * 1024);
        thread.Start();
        thread.Join();
        return code;
    }
}
```

</details>
<details><summary>mailhtmlfix.patch (le correzioni proposte di V-01, V-02, V-03, V-04 e V-07, da applicare alla copia MailHtmlFix.cs)</summary>

```diff
--- a/src/MailHtmlFix.cs
+++ b/src/MailHtmlFix.cs
@@ -8,6 +8,7 @@
 
 namespace Missiva;
 
+// Copia di MailHtml.cs con le correzioni proposte nel rapporto (V-01..V-04), solo per misurarle nel banco.
 /// <summary>
 /// Prepara l'HTML di un'email per il riquadro di lettura (WebView2 con JavaScript spento e rete bloccata).
 /// Toglie tutto ciò che può eseguire codice o raccogliere dati (script, frame, moduli), blocca le risorse esterne
@@ -15,7 +16,7 @@
 /// controllo antiphishing e, nel tema scuro, adatta i colori come fa Outlook. Il risultato è un frammento da mettere
 /// in una shadow root: gli stili dell'email restano isolati da quelli di Missiva e delle altre email.
 /// </summary>
-public static partial class MailHtml
+public static partial class MailHtmlFix
 {
     public static readonly Color DarkPaper = Color.FromRgb(0x29, 0x29, 0x29);
     /// <summary>La luminosità (HSL) di <see cref="DarkPaper"/>, a cui arrivano i fondi bianchi delle email nel tema scuro.</summary>
@@ -45,10 +46,11 @@
         // (Amazon: riquadri #F0F2F2 con i link azzurri #6ED6E6, illeggibili).
         bool sheetsDark = dark && !OwnDarkMode().IsMatch(html);
 
-        var dom = new HtmlParser().ParseDocument(html);
+        var dom = new HtmlParser().ParseDocument(EscapeStrayLt(html));
         foreach (var e in dom.QuerySelectorAll(string.Join(",", Removed)).ToList()) e.Remove();
 
         int blocked = 0;
+        var keepSet = new HashSet<IElement>();
         var links = new List<Link>();
         var styles = new StringBuilder();
         // Elementi con un fondo vivace lasciato com'è (i pulsanti): il testo dentro mantiene il suo colore.
@@ -68,7 +70,8 @@
             if (dark)
             {
                 if (IsVivid(e.GetAttribute("bgcolor")) || IsVivid(BackgroundIn(e.GetAttribute("style")))) vivid.Add(e);
-                for (var p = e; p != null && !keepText; p = p.ParentElement) keepText = vivid.Contains(p);
+                keepText = vivid.Contains(e) || e.ParentElement is { } parent && keepSet.Contains(parent);
+                if (keepText) keepSet.Add(e);
             }
             if (e.GetAttribute("style") is { Length: > 0 } style)
                 e.SetAttribute("style", Css(style, dark, allowRemote, cidUrl, ref blocked, keepText));
@@ -121,7 +124,7 @@
                     if (LinkTarget(a.Value) is { } href)
                     {
                         a.Value = href;
-                        links.Add(new Link(href, WhitespaceRun().Replace(e.TextContent, " ").Trim()));
+                        links.Add(new Link(href, WhitespaceRun().Replace(TextOf(e), " ").Trim()));
                     }
                     else e.RemoveAttribute(a.Name);
                 }
@@ -185,19 +188,84 @@
     /// Il testo di un'email HTML, per la parte di solo testo delle risposte: gli spazi come li mostra il browser, un a
     /// capo per ogni br e per ogni paragrafo, riga o cella di tabella.
     /// </summary>
+    static readonly HashSet<string> Blocks = ["p", "div", "tr", "li", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "hr"];
+
+    /// <summary>Lo stesso testo di prima, in una visita con una pila esplicita: niente ricorsione, niente Closest per ogni testo.</summary>
     public static string ToText(string html)
     {
-        var dom = new HtmlParser().ParseDocument(html);
-        foreach (var e in dom.QuerySelectorAll("script,style,head,title").ToList()) e.Remove();
-        foreach (var t in dom.Body?.Descendants<IText>().ToList() ?? new())
-            if (t.ParentElement?.Closest("pre") == null) t.TextContent = WhitespaceRun().Replace(t.TextContent, " ");
-        foreach (var br in dom.QuerySelectorAll("br").ToList()) br.Replace(dom.CreateTextNode("\n"));
-        foreach (var cell in dom.QuerySelectorAll("td,th").ToList()) cell.Append(dom.CreateTextNode(" "));
-        foreach (var block in dom.QuerySelectorAll("p,div,tr,li,h1,h2,h3,h4,h5,h6,blockquote,table,hr").ToList()) block.Append(dom.CreateTextNode("\n"));
-        var lines = (dom.Body?.TextContent ?? "").Replace(' ', ' ').Split('\n').Select(l => l.Trim());
+        var dom = new HtmlParser().ParseDocument(EscapeStrayLt(html));
+        var sb = new StringBuilder();
+        if (dom.Body is { } root)
+        {
+            // (nodo, dentro un pre, uscita): all'uscita di un elemento lo spazio delle celle e l'a capo dei blocchi.
+            var stack = new Stack<(INode Node, bool Pre, bool Exit)>();
+            for (int i = root.ChildNodes.Length - 1; i >= 0; i--) stack.Push((root.ChildNodes[i], root.LocalName == "pre", false));
+            while (stack.Count > 0)
+            {
+                var (node, pre, exit) = stack.Pop();
+                if (node is IText t) { sb.Append(pre ? t.Data : WhitespaceRun().Replace(t.Data, " ")); continue; }
+                if (node is not IElement el) continue;
+                var name = el.LocalName;
+                if (exit)
+                {
+                    if (name is "td" or "th") sb.Append(' ');
+                    if (Blocks.Contains(name)) sb.Append('\n');
+                    continue;
+                }
+                if (name is "script" or "style" or "head" or "title") continue;
+                if (name == "br") { sb.Append('\n'); continue; }
+                bool inPre = pre || name == "pre";
+                stack.Push((el, inPre, true));
+                for (int i = el.ChildNodes.Length - 1; i >= 0; i--) stack.Push((el.ChildNodes[i], inPre, false));
+            }
+        }
+        var lines = sb.ToString().Replace(' ', ' ').Split('\n').Select(l => l.Trim());
         return BlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
     }
 
+    /// <summary>
+    /// Un "<" che non apre un tag (non seguito da una lettera, "/", "!" o "?") diventa "&lt;", fuori dai fogli di stile: per
+    /// l'HTML è lo stesso testo, ma AngleSharp 1.8 su ognuno ricopia il testo accumulato (quadratico).
+    /// </summary>
+    static string EscapeStrayLt(string html)
+    {
+        StringBuilder? sb = null;
+        int last = 0;
+        for (int i = html.IndexOf('<'); i >= 0 && i < html.Length; i = html.IndexOf('<', i + 1))
+        {
+            char next = i + 1 < html.Length ? html[i + 1] : ' ';
+            if (char.IsAsciiLetter(next) || next is '/' or '!' or '?')
+            {
+                if (i + 6 <= html.Length && string.Compare(html, i + 1, "style", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
+                {
+                    int end = html.IndexOf("</style", i, StringComparison.OrdinalIgnoreCase);
+                    if (end < 0) break;
+                    i = end;
+                }
+                continue;
+            }
+            (sb ??= new StringBuilder(html.Length + 64)).Append(html, last, i - last).Append("&lt;");
+            last = i + 1;
+        }
+        return sb == null ? html : sb.Append(html, last, html.Length - last).ToString();
+    }
+
+    /// <summary>Il testo di un elemento, come TextContent ma con una pila invece della ricorsione.</summary>
+    static string TextOf(IElement e)
+    {
+        var sb = new StringBuilder();
+        var stack = new Stack<INode>();
+        for (int i = e.ChildNodes.Length - 1; i >= 0; i--) stack.Push(e.ChildNodes[i]);
+        while (stack.Count > 0)
+        {
+            var node = stack.Pop();
+            if (node is IText t) sb.Append(t.Data);
+            else if (node is IElement child)
+                for (int i = child.ChildNodes.Length - 1; i >= 0; i--) stack.Push(child.ChildNodes[i]);
+        }
+        return sb.ToString();
+    }
+
     [GeneratedRegex(@"\n{3,}")]
     private static partial Regex BlankLines();
 
@@ -310,10 +378,10 @@
     static bool IsSpacer(IElement e, string src) =>
         Number(e.GetAttribute("width")) is > 0 and <= 8 || Number(e.GetAttribute("height")) is > 0 and <= 8 || SpacerName().IsMatch(src);
 
-    [GeneratedRegex(@"(?:spacer|blank|transparent|pixel|clear)[\w-]*\.(?:gif|png)", RegexOptions.IgnoreCase)]
+    [GeneratedRegex(@"(?:spacer|blank|transparent|pixel|clear)[\w-]*\.(?:gif|png)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
     private static partial Regex SpacerName();
 
-    [GeneratedRegex(@"prefers-color-scheme\s*:\s*dark|<meta[^>]+(?:supported-)?color-scheme[^>]+dark", RegexOptions.IgnoreCase)]
+    [GeneratedRegex(@"prefers-color-scheme\s*:\s*dark|<meta[^>]+(?:supported-)?color-scheme[^>]+dark", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
     private static partial Regex OwnDarkMode();
 
     /// <summary>
@@ -350,7 +418,7 @@
     static bool IsPageUrl(string value) => value.TrimStart().StartsWith("https://" + PageHost + "/", StringComparison.OrdinalIgnoreCase);
 
     /// <summary>url(...) verso un indirizzo esterno, fuori dai fogli di stile (attributi dei disegni SVG come fill o filter).</summary>
-    [GeneratedRegex(@"url\(\s*['""]?\s*(?:https?:)?//", RegexOptions.IgnoreCase)]
+    [GeneratedRegex(@"url\([\s'""]*(?:https?:)?//", RegexOptions.IgnoreCase)]
     private static partial Regex RemoteUrlFunction();
 
     /// <summary>Immagini di 1 o 2 pixel: servono solo a sapere se hai aperto il messaggio.</summary>
@@ -372,7 +440,7 @@
 
     // ---------------------------------------------------------------- fogli di stile
 
-    [GeneratedRegex(@"url\(\s*(['""]?)(?<url>[^'"")]*)\1\s*\)", RegexOptions.IgnoreCase)]
+    [GeneratedRegex(@"url\(\s*(?:'(?<url>[^']*)'|""(?<url>[^""]*)""|(?<url>[^'""() \t\r\n\f]*))\s*\)", RegexOptions.IgnoreCase)]
     private static partial Regex CssUrl();
 
     [GeneratedRegex(@"@import[^;]*;?", RegexOptions.IgnoreCase)]
@@ -386,7 +454,7 @@
     /// Funzioni con il nome scritto con i codici di escape del CSS (u\72l( è url(): nessuna email vera lo fa, ma così un
     /// indirizzo sfuggirebbe a CssUrl.
     /// </summary>
-    [GeneratedRegex(@"(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\\(?:[0-9a-fA-F]{1,6}\s?|[^\r\n0-9a-fA-F])(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\(")]
+    [GeneratedRegex(@"(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\\(?:[0-9a-fA-F]{1,6}\s?|[^\r\n0-9a-fA-F])(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\(", RegexOptions.NonBacktracking)]
     private static partial Regex CssEscapedFunction();
 
     /// <summary>Foglio di stile o attributo style: niente @import, url() esterni solo se consentiti, colori per il tema scuro.</summary>
@@ -423,7 +491,7 @@
     [GeneratedRegex(@"(?<![-\w])(?<prop>background-color|background|border(?:-(?:top|right|bottom|left))?(?:-color)?|outline(?:-color)?|color)\s*:\s*(?<value>[^;{}""]+)", RegexOptions.IgnoreCase)]
     private static partial Regex ColorDeclaration();
 
-    [GeneratedRegex(@"url\([^)]*\)|#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)|hsla?\([^)]*\)|[a-zA-Z]+", RegexOptions.IgnoreCase)]
+    [GeneratedRegex(@"url\([^()]*(?:\([^()]*\)[^()]*)*\)|#[0-9a-fA-F]{3,8}\b|rgba?\([^()]*\)|hsla?\([^()]*\)|[a-zA-Z]+", RegexOptions.IgnoreCase)]
     private static partial Regex ColorToken();
 
     /// <summary>
@@ -477,8 +545,15 @@
             : $"rgba({adapted.R},{adapted.G},{adapted.B},{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";
     });
 
+    /// <summary>I nomi di colore che ColorConverter conosce (in Missiva: dai nomi di System.Windows.Media.Colors).</summary>
+    static readonly HashSet<string> ColorNames = new(Enum.GetValues<System.Drawing.KnownColor>()
+        .Where(k => !System.Drawing.Color.FromKnownColor(k).IsSystemColor && k != System.Drawing.KnownColor.RebeccaPurple).Select(k => k.ToString()), StringComparer.OrdinalIgnoreCase);
+
     static Color? ParseColor(string value)
     {
+        if (value.StartsWith('#') ? !(value.Length is 4 or 5 or 7 or 9 && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
+            : !(value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')')) && !ColorNames.Contains(value))
+            return null; // non è un colore: niente FormatException
         try
         {
             if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
```

</details>
<details><summary>Il progetto di A.8: lt.csproj</summary>

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Optimize>true</Optimize></PropertyGroup>
  <ItemGroup><PackageReference Include="AngleSharp" Version="1.8.2" /></ItemGroup>
</Project>
```

</details>
<details><summary>Il progetto di A.8 (solo AngleSharp e i "&lt;")</summary>

```csharp
using System.Diagnostics;
using AngleSharp.Html.Parser;
string W(string b) => "<html><body><p>" + b + "</p></body></html>";
string Rep(string s, int n) => string.Concat(Enumerable.Repeat(s, n));
var cases = new (string, Func<int, string>)[] {
  ("<<<", n => W(new string('<', n))),
  ("< spazio", n => W(Rep("< ", n))),
  ("<1", n => W(Rep("<1", n))),
  ("a<b>", n => W(Rep("a<b>", n))),
  ("a < b", n => W(Rep("a < b ", n))),
  ("<= confronto", n => W(Rep("x <= y ", n))),
  ("<< in style", n => "<html><head><style>" + new string('<', n) + "</style></head><body>x</body></html>"),
  ("<< in attributo", n => "<html><body><div title=\"" + new string('<', n) + "\">x</div></body></html>"),
  ("&lt; al posto di <", n => W(Rep("&lt;", n))),
};
new HtmlParser().ParseDocument(W("x"));
foreach (var (name, f) in cases)
{
  var line = name + ":";
  foreach (var n in new[] { 5000, 10000, 20000, 40000 })
  {
    var html = f(n);
    for (int w = 0; w < 2; w++) new HtmlParser().ParseDocument(html);
    GC.Collect();
    long a = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    new HtmlParser().ParseDocument(html);
    line += $"  n={n}: {sw.Elapsed.TotalMilliseconds:0.0} ms, {(GC.GetAllocatedBytesForCurrentThread() - a) / 1048576.0:0.0} MB";
  }
  Console.WriteLine(line);
}
```

</details>
<details><summary>Il progetto della sostituzione dei &lt;br&gt; (V-09; stesso csproj di A.8)</summary>

```csharp
using System.Diagnostics;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
// Il passo di MailHtml.ToText (Core/MailHtml.cs:194): ogni <br> sostituito da un nodo di testo, con N <br> nello stesso genitore.
foreach (var n in new[] { 1000, 2000, 4000, 8000, 16000 })
{
    var html = "<html><body><div>" + string.Concat(Enumerable.Repeat("riga di testo<br>", n)) + "</div></body></html>";
    double best = double.MaxValue;
    for (int k = 0; k < 3; k++)
    {
        var dom = new HtmlParser().ParseDocument(html);
        var sw = Stopwatch.StartNew();
        foreach (var br in dom.QuerySelectorAll("br").ToList()) br.Replace(dom.CreateTextNode("\n"));
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{n} <br> in un div ({html.Length / 1024} KB): sostituzione {best:0.0} ms");
}
```

</details>
