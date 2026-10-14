# Revisione di sicurezza del modulo Core (13 ottobre 2026)

Sei consigli di un revisore esterno che ha letto i sorgenti di `Core` (commit `e120133`, senza i file delle prove né i
dati di esempio), senza eseguire nulla. Il testo è il suo, com'è arrivato; le sigle K-01..K-06 sono aggiunte qui per
poterli citare nell'esito. Come per gli altri rapporti, ogni punto va verificato sul codice e con una prova prima di
toccarlo: l'esito è in `ESITO.md`.

## Il testo del revisore

Il codice di `Missiva` dimostra una maturità architetturale sul piano della sicurezza raramente riscontrabile in
applicazioni desktop analoghe. Le contromisure già attive coprono in modo granulare minacce avanzate (AppContainer +
JobObject per PDF/immagini, anti-SSRF con validazione a livello di socket e blocco NAT64/6to4, protezione NTLM/SMB su
font WPF ed estensioni `.library-ms`/`.search-ms`, parser AST per SVG e CSS, cifratura DPAPI ubiqua e `AesGcm` con
azzeramento memoria per i backup).

Per portare la sicurezza del modulo `Core` alla perfezione, ti consiglio di intervenire in ordine di priorità su
questi 6 punti concreti:

1. **K-01.** `Safety.SafeFileName` (`Safety.cs`): Spostare il troncamento `clean[^150..]` prima del controllo su
   `ReservedNames` e rifare `.TrimStart('.', ' ')` dopo il taglio, così un nome di >150 caratteri non può mai
   trasformarsi in `NUL.txt` o `CON.txt` dopo il taglio.
2. **K-02.** Suffissi `.gov.it` / `.edu.it` (`SenderLogos.cs`, `Settings.cs`, `Safety.cs`): Aggiungere `"gov.it"` e
   `"edu.it"` a `SenderLogos.TwoLevel` (e `"edu"` se mancante) in modo che i domini di terzo livello istituzionali
   italiani non vengano accorpati sotto `gov.it` o `edu.it` per i loghi, i mittenti attendibili delle immagini e gli
   avvisi anti-phishing sui link.
3. **K-03.** Escaping degli ID nelle URL di `GraphProvider` (`GraphProvider.cs`): Applicare
   `Uri.EscapeDataString(...)` a tutti i `message.Id`, `folder.Id` e `attachment.Id` interpolati nei path delle
   richieste Graph.
4. **K-04.** Sanitizzazione dell'HTML citato (`EmlProvider.cs`, `MimeOptions.cs`): Passare `body.Html` /
   `quoted.Html` da `MailHtml.Prepare` (o da un metodo di pulizia equivalente) prima di concatenarlo nel
   `<blockquote>` delle risposte/inoltri, evitando di rispedire tag `<script>`, `<form>` o tracker presenti nel
   messaggio originale.
5. **K-05.** Controllo schemi URI in `FlowToHtml` e `Mailto` (`FlowToHtml.cs`, `Mailto.cs`): Verificare che
   `Hyperlink.NavigateUri` in `FlowToHtml` accetti solo `http`, `https` o `mailto`, e ignorare o segnalare il
   parametro `bcc=` nei link `mailto:` aperti dalle email.
6. **K-06.** Multi-Signer in `P7m.Check` (`P7m.cs`): Verificare tutti gli elementi di `cms.SignerInfos` (non solo
   `SignerInfos[0]`) con `Pec.Check` quando una busta `.p7m` contiene più firme parallele.
