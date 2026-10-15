# Gmail: registrare la tua app su Google Cloud

Con Gmail l'accesso è come nel nuovo Outlook: si apre il browser, scegli l'account Google e consenti a Missiva di
usare la posta. Google però lo permette solo alle app registrate su Google Cloud, e una registrazione unica dentro
Missiva, valida per tutti, dovrebbe prima passare la verifica che Google chiede alle app che leggono la posta.
Missiva non l'ha fatta. Quindi ognuno registra la propria "app": è gratis, si fa una volta sola in una decina di
minuti, e vale per tutti i propri account Gmail.

Quello che registri è tuo: l'ID e il segreto restano sul tuo PC e Missiva parla direttamente con Google, senza
nessun server in mezzo.

## I passi

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

## Nel browser, al primo accesso

Google avvisa che non ha verificato l'app: è normale, perché l'app è tua e non è passata dalla verifica di Google.
Premi Avanzate (Advanced), poi Vai a Missiva (Go to Missiva), e consenti l'accesso lasciando spuntata la casella
della posta.

Dal secondo account Gmail in poi non c'è più nulla da registrare: Missiva usa lo stesso ID client.

## Calendario e contatti di Google (facoltativi)

Si aggiungono dopo, quando servono, allo stesso progetto:

| Per | Attiva | Aggiungi l'ambito (come al passo 5) | Poi in Missiva |
|---|---|---|---|
| Calendario | [Google Calendar API](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com) | `https://www.googleapis.com/auth/calendar` | Calendario, "Consenti l'accesso al calendario" |
| Contatti, in sola lettura | [People API](https://console.cloud.google.com/apis/library/people.googleapis.com) | `https://www.googleapis.com/auth/contacts.readonly` | Persone, "Consenti l'accesso ai contatti" |
| Contatti, anche modificarli | la stessa | `https://www.googleapis.com/auth/contacts` | Persone, "Consenti la modifica dei contatti" |

A ogni nuovo accesso, nella pagina di Google lascia spuntate tutte le caselle: se ne togli una quel permesso si
perde, e Missiva lo dice.

## Se qualcosa non va

- **"Accesso bloccato" con l'errore 403 `access_denied`**: l'app è ancora "In test" e il tuo account non è fra gli
  utenti di prova. Pubblica l'app (passo 4).
- **Dopo una settimana Missiva chiede di accedere di nuovo**: stessa causa, l'app è "In test". Pubblicala e poi, in
  Missiva, Accedi di nuovo.
- **Missiva dice che mancano ID client e client secret**: vanno incollati tutti e due nel riquadro "App Google di
  Missiva" della finestra Aggiungi account.
- **Google Workspace, `admin_policy_enforced`**: l'amministratore del dominio blocca le app che non ha reso
  attendibili, oppure ha spento IMAP. Missiva lo dice e spiega che cosa chiedergli: consentire l'app con il suo ID
  client nella Console di amministrazione, o riattivare IMAP.

## Che cosa resta sul PC, e come togliere l'accesso

Missiva non vede la password di Google. Conserva il token di accesso e il client secret cifrati con la protezione
dati di Windows, leggibili solo dal tuo utente. Rimuovendo l'account, Missiva chiede a Google di revocare
l'autorizzazione; se la richiesta non riesce lo dice, e l'autorizzazione si toglie a mano da
https://myaccount.google.com/permissions.
