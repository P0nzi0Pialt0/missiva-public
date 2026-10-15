# Missiva

Client di posta per Windows 11, sul modello del nuovo Outlook ma nativo: interfaccia WPF con lo stile Fluent di
Windows, scrittura con editor nativo e controllo ortografico italiano. Solo il contenuto delle email è impaginato da
WebView2, chiuso: niente JavaScript e niente Internet, come un visualizzatore e non come un browser.

Posta per account Microsoft (Outlook.com, Hotmail, Live, Microsoft 365), Gmail e Google Workspace, e per le altre
caselle con IMAP e SMTP (Aruba, Register.it, OVH, Zoho, posta certificata). Calendario e contatti per gli account
Microsoft e Google.

Il manuale completo, con tutto quello che Missiva fa e quello che manca, è in [LEGGIMI.md](LEGGIMI.md).

## A che punto è

- Versione 0.3. È un progetto personale, usato tutti i giorni da chi l'ha scritto: funziona, ma non ha garanzie né
  assistenza.
- L'interfaccia è solo in italiano.
- Si installa da un rilascio già compilato, senza altro da installare prima, e poi si aggiorna da sola; oppure si
  compila sul proprio PC (un comando). Tutti e due i modi sono sotto.
- Gli account Microsoft personali funzionano subito. Per Gmail serve una registrazione gratuita su Google Cloud, da
  fare una volta: la guida è più sotto.

## Installare

### Da un rilascio

Serve Windows 11, e nient'altro. Dall'[ultimo rilascio](../../releases/latest) scarica `Missiva-0.3.N.zip` ed
estrailo in una cartella; in Esplora file, con il tasto destro dentro quella cartella, scegli Apri nel Terminale, e lì
lancia:

```
powershell -ExecutionPolicy Bypass -File installa.ps1
```

Lo script prova che l'eseguibile parta e lo mette in `%LOCALAPPDATA%\Programs\Missiva`, senza permessi di
amministratore. Missiva compare nel menu Start e in Impostazioni, App installate, da dove si disinstalla. Missiva non
ha un certificato di firma del codice: la prima volta che la apri Windows può avvisare che "ha protetto il PC"
(Ulteriori informazioni, Esegui comunque).

Da lì **si aggiorna da sola**: quando esce una versione nuova una barra in fondo all'elenco dei messaggi lo dice, e
Aggiorna la scarica, la controlla e riapre Missiva aggiornata. Accetta solo aggiornamenti firmati con la chiave di chi
pubblica Missiva. Come funziona, che cosa controlla e che cosa vede GitHub: [docs/aggiornamenti.md](docs/aggiornamenti.md).

### Dai sorgenti

Servono Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download) e PowerShell 7 (il comando `pwsh`: su
Windows non c'è di serie, si installa dal Microsoft Store o con `winget install Microsoft.PowerShell`).

Scarica i sorgenti (pulsante Code, Download ZIP, oppure `git clone`) e dalla loro cartella lancia:

```
pwsh -ExecutionPolicy Bypass -File installa.ps1
```

Lo script compila Missiva, prova che l'eseguibile parta e lo mette in `%LOCALAPPDATA%\Programs\Missiva`, senza
permessi di amministratore. Missiva compare nel menu Start e in Impostazioni, App installate, da dove si disinstalla.
Per aggiornarla si rilancia lo stesso comando: impostazioni e account restano. Compilata così non si aggiorna da sola
(non sa da dove prendere gli aggiornamenti, e non accetta gli eseguibili di nessuno).

Per aprire con Missiva i link di posta (mailto:) delle pagine web, dopo l'installazione sceglila in Impostazioni, App,
App predefinite, Missiva: Windows non lascia farlo ai programmi.

## Aggiungere un account

Aggiungi account parte dall'indirizzo: Missiva riconosce da sola il tipo di account e lo propone.

- **Outlook.com, Hotmail, Live**: Accedi con Microsoft, si apre la finestra account di Windows. Nient'altro da fare.
- **Microsoft 365 di lavoro o della scuola**: lo stesso. Se l'organizzazione non lascia agli utenti il consenso alle
  app, Missiva lo spiega e prepara il link da mandare all'amministratore. I dettagli sono in
  [LEGGIMI.md](LEGGIMI.md).
- **Gmail e Google Workspace**: la prima volta serve la guida qui sotto.
- **Altre caselle (IMAP e SMTP)**: indirizzo e password; Missiva cerca da sola i server e li mostra prima
  dell'accesso.

## Gmail: registrare la tua app su Google Cloud

Con Gmail l'accesso è come nel nuovo Outlook: si apre il browser, scegli l'account Google e consenti a Missiva di
usare la posta. Google però lo permette solo alle app registrate su Google Cloud, e una registrazione unica dentro
Missiva, valida per tutti, dovrebbe prima passare la verifica che Google chiede alle app che leggono la posta.
Missiva non l'ha fatta. Quindi ognuno registra la propria "app": è gratis, si fa una volta sola in una decina di
minuti, e vale per tutti i propri account Gmail.

Quello che registri è tuo: l'ID e il segreto restano sul tuo PC e Missiva parla direttamente con Google, senza
nessun server in mezzo.

### I passi

1. **Crea un progetto.** Apri https://console.cloud.google.com/projectcreate con il tuo account Google e crea un
   progetto di nome Missiva.
2. **Attiva la Gmail API** nel progetto: https://console.cloud.google.com/apis/library/gmail.googleapis.com, poi
   Abilita (Enable).
3. **Presenta l'app.** Apri https://console.cloud.google.com/auth/overview e premi Inizia (Get started). Nome
   dell'app: Missiva. Email di assistenza: la tua. Pubblico (Audience): Esterno (External). Email di contatto: la
   tua. Accetta e crea.
4. **Pubblica l'app.** In Pubblico (Audience) premi Pubblica app (Publish app) e conferma. Non saltare questo passo:
   se l'app resta "In test", Google fa scadere l'accesso ogni 7 giorni.
5. **Dai all'app il permesso della posta.** In Accesso ai dati (Data access) premi Aggiungi o rimuovi ambiti (Add or
   remove scopes) e spunta `https://mail.google.com/`. Se non compare nell'elenco, incollalo in fondo, in Aggiungi
   manualmente gli ambiti. Poi Aggiorna (Update) e Salva (Save).
6. **Crea il client.** In Client (Clients) premi Crea client (Create client). Tipo di applicazione: App desktop
   (Desktop app). Nome: Missiva. Crea. Copia subito **ID client** e **client secret**, oppure scarica il file JSON:
   il secret si vede solo in quel momento. L'ID client finisce con `.apps.googleusercontent.com`.
7. **Mettili in Missiva.** Aggiungi account, scrivi l'indirizzo Gmail (o scegli Google), apri "App Google di
   Missiva", incolla ID client e client secret e premi Accedi con Google.

### Nel browser, al primo accesso

Google avvisa che non ha verificato l'app: è normale, perché l'app è tua e non è passata dalla verifica di Google.
Premi Avanzate (Advanced), poi Vai a Missiva (Go to Missiva), e consenti l'accesso lasciando spuntata la casella
della posta.

Dal secondo account Gmail in poi non c'è più nulla da registrare: Missiva usa lo stesso ID client.

### Calendario e contatti di Google (facoltativi)

Si aggiungono dopo, quando servono, allo stesso progetto:

| Per | Attiva | Aggiungi l'ambito (come al passo 5) | Poi in Missiva |
|---|---|---|---|
| Calendario | [Google Calendar API](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com) | `https://www.googleapis.com/auth/calendar` | Calendario, "Consenti l'accesso al calendario" |
| Contatti, in sola lettura | [People API](https://console.cloud.google.com/apis/library/people.googleapis.com) | `https://www.googleapis.com/auth/contacts.readonly` | Persone, "Consenti l'accesso ai contatti" |
| Contatti, anche modificarli | la stessa | `https://www.googleapis.com/auth/contacts` | Persone, "Consenti la modifica dei contatti" |

A ogni nuovo accesso, nella pagina di Google lascia spuntate tutte le caselle: se ne togli una quel permesso si
perde, e Missiva lo dice.

### Se qualcosa non va

- **"Accesso bloccato" con l'errore 403 `access_denied`**: l'app è ancora "In test" e il tuo account non è fra gli
  utenti di prova. Pubblica l'app (passo 4).
- **Dopo una settimana Missiva chiede di accedere di nuovo**: stessa causa, l'app è "In test". Pubblicala e poi, in
  Missiva, Accedi di nuovo.
- **Missiva dice che mancano ID client e client secret**: vanno incollati tutti e due nel riquadro "App Google di
  Missiva" della finestra Aggiungi account.
- **Google Workspace, `admin_policy_enforced`**: l'amministratore del dominio blocca le app che non ha reso
  attendibili, oppure ha spento IMAP. Missiva lo dice e spiega che cosa chiedergli: consentire l'app con il suo ID
  client nella Console di amministrazione, o riattivare IMAP.

### Che cosa resta sul PC, e come togliere l'accesso

Missiva non vede la password di Google. Conserva il token di accesso e il client secret cifrati con la protezione
dati di Windows, leggibili solo dal tuo utente. Rimuovendo l'account, Missiva chiede a Google di revocare
l'autorizzazione; se la richiesta non riesce lo dice, e l'autorizzazione si toglie a mano da
https://myaccount.google.com/permissions.

## Sicurezza, in breve

- Nessun codice delle email viene eseguito: JavaScript è spento nel riquadro di lettura, e script, frame e moduli
  sono tolti dall'email prima di mostrarla.
- Il riquadro non si collega a Internet da solo: passano solo le immagini dei mittenti per cui hai premuto Mostra
  immagini. Così il mittente non sa se e quando hai letto.
- I link si aprono nel tuo browser, mai dentro Missiva, con un avviso quando il testo mostra un sito e il link porta
  altrove.
- Password e accessi restano sul PC, cifrati con la protezione dati di Windows. Non c'è nessun server di Missiva: il
  programma parla solo con i server della tua posta.

Il resto è nella sezione Sicurezza di [LEGGIMI.md](LEGGIMI.md).

## Per chi guarda il codice

- `Core` è la posta (Microsoft Graph, Gmail e IMAP, la pulizia dell'HTML, la copia sul PC), `UI` l'interfaccia WPF,
  `strumenti` gli script per provarla.
- Le prove automatiche stanno in `Core/ProvaBozze*.cs` e girano dentro l'eseguibile di Debug, con server finti sul PC
  e senza rete: `dotnet build`, poi `Missiva.exe --prova-bozze risultato.txt` con la variabile `MISSIVA_DATA` che
  indica una cartella vuota. Senza quella variabile la versione di Debug non parte, per non toccare la posta vera.
- `docs/audit` contiene gli audit di sicurezza, di velocità, del codice e delle funzioni fatti su Missiva, e in
  `ESITO.md` che cosa ne è stato di ogni punto: confermato o no, corretto o lasciato, con le misure. Sono pubblici
  apposta. Citano alcuni appunti di lavoro (`CLAUDE.md`, `docs/storia`) che in questo repository non ci sono.

## Licenza

Missiva è distribuita con la licenza MIT: il testo è in [LICENSE](LICENSE). In breve, puoi usarla, modificarla e
ridistribuirla liberamente, lasciando la nota di copyright, e viene fornita così com'è, senza garanzie.

I componenti di terzi che Missiva usa, con le loro licenze, sono in [TERZE-PARTI.md](TERZE-PARTI.md).
