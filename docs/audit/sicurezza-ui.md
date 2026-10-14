# Revisione di sicurezza dell'interfaccia (14 ottobre 2026)

Cinque correzioni proposte da un revisore esterno che ha letto i sorgenti dell'interfaccia (`Program.cs`, `UI\*.cs`,
`UI\*.xaml`, `installa.ps1` e `disinstalla.ps1`, commit `df0cd97`), senza eseguire nulla. Il testo è il suo, com'è
arrivato (in due parti: la prima si fermava a metà del secondo punto); le sigle U-01..U-05 sono aggiunte qui per
poterli citare nell'esito. Come per gli altri rapporti, ogni punto va verificato sul codice e con una prova prima di
toccarlo: l'esito è in `ESITO.md`.

## Il testo del revisore

Piano di correzione vulnerabilità. Audit Sicurezza UI (`Core/MailHtml.cs`, `UI/*.cs`).

Applica le seguenti 5 correzioni di sicurezza emerse dall'audit sui file dell'interfaccia utente (`UI`), mantenendo
intatto lo stile del codice, i commenti in italiano e tutti i test esistenti.

### U-01. [MEDIA] Prevenzione di Declarative Shadow DOM Breakout (`Core/MailHtml.cs` e `UI/ReadingPage.cs`)

**Problema.** In `ReadingPage.Body`, l'HTML sanitizzato dell'email viene inserito dentro
`<template shadowrootmode="open">...{prepared.Fragment}</template>`. Anche se `"template"` è nella lista `Removed` di
`MailHtml.cs`:

1. `MailHtml.InnerHtml` conserva ed emette i commenti HTML
   (`case IComment comment: html.Append(formatter.Comment(comment));`) e ha un ramo per `IHtmlTemplateElement`. I
   commenti HTML e i contesti foreign (`<svg>`, `<math>`) sono il vettore principale per mutation XSS (mXSS) e
   desincronizzazione tra il serializer di AngleSharp e il parser HTML di Chromium.
2. Se per qualsiasi discrepanza di serializzazione o dentro un blocco `<style>` / attributo finisse la sequenza
   `</template`, il contenuto dell'email uscirebbe dalla Shadow Root dichiarativa ed entrerebbe nel DOM principale di
   `ReadingPage`, permettendo agli stili dell'email di falsificare le fasce di sicurezza (`.warn.ok`, `.warn.danger`)
   o sovrapporre link malevoli all'interfaccia del riquadro di lettura.

**Correzioni richieste.**

1. In `Core/MailHtml.cs` (metodo `InnerHtml`):
   - **Non** serializzare i commenti HTML: sostituire
     `case IComment comment: html.Append(formatter.Comment(comment)); break;` con `case IComment: break;`.
   - Rimuovere la serializzazione del contenuto di eventuali `IHtmlTemplateElement` residui in `InnerHtml` (un
     `<template>` non deve mai essere espanso).
2. In `Core/MailHtml.cs` o `UI/ReadingPage.cs`:
   - Assicurarsi che né nei fogli di stile estratti né nel `Fragment` finale possa mai comparire la sequenza di
     chiusura `</template` (case-insensitive), ad esempio neutralizzandola o verificando che
     `.Replace("</", "\\3C/")` copra qualsiasi occorrenza e aggiungendo una guardia finale sul frammento prima
     dell'inserimento in `<template shadowrootmode="open">`.

### U-02. [MEDIA] Conferma sugli inoltri esterni preesistenti o su regole riattivate (`UI/RuleWindow.cs` e `UI/RulesView.cs`)

**Problema.**

1. In `RuleWindow.SaveAsync` (`UI/RuleWindow.cs`), la lista `outside` calcola
   `Rules.ExternalForwards(rule, provider.Account)` filtrando via gli indirizzi già presenti in `editing`:
   `.Where(e => editing == null || !Rules.ExternalForwards(editing, provider.Account).Contains(e, StringComparer.OrdinalIgnoreCase)).ToList()`.
   Se una regola preesistente aveva già indirizzi di inoltro esterno (inclusi `RedirectTo` o `ForwardAsAttachmentTo`
   che Missiva non mostra nei campi ma preserva in `editing.Copy()`), oppure se la regola era spenta
   (`editing.Enabled == false`) e viene modificata/salvata, `outside` scarta gli indirizzi già presenti in `editing`
   e non mostra la finestra di conferma `"La regola manda la tua posta fuori"`.
2. In `RulesView.RunNowAsync` ("Esegui ora", `UI/RulesView.cs`): quando si esegue manualmente una regola, se la
   regola sposta nel Cestino o elimina i messaggi (`active.Delete`), conviene che il messaggio di conferma
   `Dialog.Ask` lo metta chiaramente in evidenza.

**Correzioni richieste.**

1. In `UI/RuleWindow.SaveAsync`: mostrare la conferma `"La regola manda la tua posta fuori"` per tutti gli indirizzi
   esterni di inoltro/reindirizzamento (`Rules.ExternalForwards(rule, provider.Account)`) anche quando
   `editing != null && !editing.Enabled` (regola spenta che viene salvata/modificata), oppure quando la regola
   mantiene inoltri nascosti (`RedirectTo` / `ForwardAsAttachmentTo`).

### U-03. [BASSA / MEDIA] Indurimento della scrittura di allegati in `%TEMP%\Missiva` e `SaveFileDialog` (`UI/ReadingView.xaml.cs` e `UI/P7mWindow.cs`)

**Problema.** In `ReadingView.OpenAttachmentAsync` e `P7mWindow.Open`:

1. La sottocartella temporanea usa solo 8 caratteri esadecimali (32 bit): `Guid.NewGuid().ToString("N")[..8]`.
2. Viene usato `File.WriteAllBytes` / `File.WriteAllBytesAsync` invece di una scrittura protetta da symlink/reparse
   point.
3. Se `Safety.MarkFromInternet(path)` restituisce `false` e l'utente annulla in `Dialog.ConfirmOpenUnmarked`, il file
   con il nome ed estensione finali è già stato scritto su disco prima di essere rimosso dal `Directory.Delete`.

**Correzioni richieste.**

1. In `UI/ReadingView.xaml.cs` (`OpenAttachmentAsync`) e `UI/P7mWindow.cs` (`Open`):
   - Usare un nome di cartella temporanea completo a 128 bit (`Guid.NewGuid().ToString("N")`) invece di `[..8]`.
   - Verificare che `Safety.TempDir` e la sottocartella appena creata non siano reparse point
     (`(File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0`).
   - Nel salvataggio su disco (`save == true` e `SaveAllAsync`), usare `SafeFile.Write(path, bytes)` (o verificare
     l'assenza di reparse point) prima di chiamare `Safety.MarkFromInternet(path)`.

### U-04. [BASSA] Codifica HTML difensiva in `ReadingPage.Empty` (`UI/ReadingPage.cs`)

**Problema.** In `ReadingPage.Empty(bool light, EmptyLook look)` (`UI/ReadingPage.cs`), i campi `look.Font`,
`look.Glyph` e `look.Text` vengono interpolati direttamente negli attributi `style="..."` e nel corpo del `<div>`
senza passare per `E(...)` (`WebUtility.HtmlEncode`). Sono gli unici parametri dinamici in `ReadingPage.cs` non
passati per `E(...)`.

**Correzioni richieste.**

- In `UI/ReadingPage.cs` (`Empty`), avvolgere `look.Font`, `look.Glyph` e `look.Text` con `E(...)`.

### U-05. [BASSA] Percorso assoluto di `explorer.exe` e controllo dello schema URI in `AccountWindow` (`UI/BackupWindows.cs` e `UI/AccountWindow.xaml.cs`)

**Problema.**

1. In `BackupExportWindow.ExportAsync` (`UI/BackupWindows.cs`), viene chiamato
   `System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")` usando il nome relativo `"explorer.exe"`
   e interpolando `path` nella stringa degli argomenti.
2. In `AccountWindow.OnLink` (`UI/AccountWindow.xaml.cs`), viene chiamato
   `Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })` senza controllare che
   `e.Uri.Scheme == Uri.UriSchemeHttps` prima di invocare `UseShellExecute = true`.

**Correzioni richieste.**

1. In `UI/BackupWindows.cs` (`ExportAsync`): usare
   `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")` e assicurarsi che
   `path` non contenga virgolette.
2. In `UI/AccountWindow.xaml.cs` (`OnLink`): aggiungere il controllo
   `if (e.Uri is not { IsAbsoluteUri: true } uri || uri.Scheme != Uri.UriSchemeHttps) return;` prima di
   `Process.Start`.
