#if DEBUG
namespace Missiva;

/// <summary>
/// Gruppo "lingua-impostazioni" (Missiva in inglese, 21 ottobre 2026): i controlli dell'inglese dell'area "impostazioni", scritti da
/// chi la traduce, uno per ogni logica nuova in inglese (date, ricorrenze, plurali, parole chiave, formati, testi composti),
/// con Lingua.Force("en") e l'italiano di nuovo in un finally (Lingua.Force("it")): un gruppo che lascia l'inglese è un
/// controllo non passato. Le traduzioni stanno in Assets/lingue/en/impostazioni.json; che ogni testo dei sorgenti ne abbia
/// una lo controlla già il gruppo "lingua". Da solo con "Missiva.exe --prova-audit risultato.txt lingua-impostazioni".
/// Già registrato in ProvaBozze.Gruppi.cs: non serve toccarlo. Vuoto non è un errore (zero controlli).
/// </summary>
static partial class ProvaBozze
{
    static void LanguageSettingsTests()
    {
        Report.Add("--- Lingua, area impostazioni: l'inglese");
        try
        {
            LisTemplates();
            LisStartupTexts();
            LisBackupTexts();
            LisAccountTexts();
            LisVersion();
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>
    /// La versione in Informazioni (SettingsWindow.VersionText): il testo che la compilazione scrive in italiano, in inglese
    /// con le parole e la data breve della cultura inglese; in italiano e in una forma che non conosce resta com'è.
    /// </summary>
    static void LisVersion()
    {
        const string clean = "0.3.525 del 14/10/2026 (8c444de)", changed = clean + ", con modifiche non ancora in un commit";
        string italian = UI.SettingsWindow.VersionText(changed), british, american, number;
        try
        {
            Lingua.Force("en", "en-GB");
            british = UI.SettingsWindow.VersionText(changed);
            number = UI.SettingsWindow.VersionText("0.3.0");
            Lingua.Force("en", "en-US");
            american = UI.SettingsWindow.VersionText(clean);
        }
        finally
        {
            Lingua.Force("it");
        }
        Check(italian == changed && british == "0.3.525 (14/10/2026, 8c444de), with changes not yet committed" && american == "0.3.525 (10/14/2026, 8c444de)" && number == "0.3.0",
              $"lingua-impostazioni, la versione in Informazioni: in italiano com'è (\"{italian}\"), in inglese \"{british}\" (en-GB) e \"{american}\" (en-US), il solo numero com'è");
    }

    static readonly System.Globalization.CultureInfo LisItalian = System.Globalization.CultureInfo.GetCultureInfo("it-IT"), LisBritish = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// I testi che nascono all'avvio, prima che la lingua sia scelta (App.LoadSettings: Backup.ApplyPending e Store.Load
    /// girano prima di Lingua.Start): l'esito di un'importazione in attesa, riuscita (con le note: una voce larga delle
    /// immagini non importata, una regola accesa) e non riuscita (un file in attesa che non si legge), e l'avviso delle
    /// impostazioni rovinate. Nascono in italiano e si leggono dopo in inglese: motivo, note, titolo e testo escono in
    /// inglese; letti in italiano sono quelli di sempre. Cartelle temporanee, dati finti.
    /// </summary>
    static void LisStartupTexts()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-lingua-impostazioni-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var theirs = new Settings
            {
                Accounts = [Copy2Imap("imap-prova")],
                Rules = [new MailRule { AccountId = "imap-prova", Name = "Pulizia", Delete = true }],
                ImageSenders = ["co.uk", "gmail.example", "fornitore-prova.example"],
            };
            var good = Path.Combine(root, "riuscita");
            Copy2Pending(good, Copy2Content(theirs));
            string how = Copy2Apply(good, out var applied);
            var broken = Path.Combine(root, "rotta");
            Directory.CreateDirectory(broken);
            File.WriteAllBytes(Path.Combine(broken, Backup.PendingName), [1, 2, 3]);
            string brokenHow = Copy2Apply(broken, out var failed);
            var recovery = new StoreRecovery(StoreRecovery.Invalid, Path.Combine(root, "impostazioni-rovinate.json"), new DateTime(2026, 10, 12, 9, 30, 0), null, false, Path.Combine(root, "impostazioni.json"));
            var other = new StoreRecovery("IOException: in uso", null, null, null, true, Path.Combine(root, "impostazioni.json"));

            string itBody = applied?.Body ?? "", itNotes = string.Join(" | ", applied?.Notes ?? []), itError = failed?.Error ?? "", itRecovery = recovery.Body;
            string enBody, enNotes, enError, enFailed, enHeading, enRecovery, enRecoveryHeading, enOther;
            try
            {
                Lingua.Force("en", "en-GB");
                (enBody, enNotes, enError, enFailed, enHeading) = (applied?.Body ?? "", string.Join(" | ", applied?.Notes ?? []), failed?.Error ?? "", failed?.Body ?? "", failed?.Heading ?? "");
                (enRecovery, enRecoveryHeading, enOther) = (recovery.Body, recovery.Heading, other.Body);
            }
            finally
            {
                Lingua.Force("it");
            }
            var created = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc).ToLocalTime();
            Check(how == "applicata" && brokenHow == "non applicata"
                  && itBody.StartsWith($"Missiva è ripartita con i dati della copia del {created.ToString("d MMMM yyyy 'alle' HH:mm", LisItalian)}.") && itBody.Contains("mario@studio-prova.example chiede di nuovo l'accesso")
                  && itNotes.Contains("dell'elenco dei mittenti di cui mostrare sempre le immagini") && itNotes.EndsWith("La copia aveva una regola di Missiva accesa: vale da subito per i messaggi che arrivano. È in Impostazioni, Posta, Regole.")
                  && itError.StartsWith("il file dell'importazione in attesa non si leggeva (") && itError.EndsWith("Non è cambiato nulla: rifai l'importazione da Impostazioni, Generali, Copia di sicurezza.")
                  && enBody.StartsWith($"Missiva restarted with the data from the backup of {created.ToString("d MMMM yyyy 'at' HH:mm", LisBritish)}.") && enBody.Contains("mario@studio-prova.example needs you to sign in again")
                  && enNotes.Contains("in the list of senders whose images are always shown") && enNotes.EndsWith("The backup had a Missiva rule turned on: it applies right away to incoming messages. It's in Settings, Mail, Rules.")
                  && enBody.Contains(enNotes.Split(" | ")[^1])
                  && enError.StartsWith("the pending import file couldn't be read (") && enHeading == "Import failed" && enFailed == "The backup wasn't imported: " + enError
                  && itRecovery.StartsWith("Il file delle impostazioni era rovinato (il contenuto non è valido). Missiva è ripartita dalla copia di riserva del 12 ottobre 2026 alle 09:30")
                  && enRecovery.StartsWith("The settings file was damaged (the content isn't valid). Missiva restarted from the spare copy of your settings from 12 October 2026 at 09:30")
                  && enRecoveryHeading == "Missiva's settings couldn't be read" && recovery.Why == "il contenuto non è valido"
                  && enOther.StartsWith("The settings file can't be read or moved, maybe because another program is using it (IOException: in uso)."),
                  "lingua-impostazioni, i testi che nascono all'avvio prima della lingua (l'esito di un'importazione in attesa, riuscita con le sue note e non riuscita con il motivo, " +
                  "e l'avviso delle impostazioni rovinate) si calcolano quando si leggono: in inglese escono in inglese, in italiano come prima, e Why resta il motivo italiano; " +
                  $"un motivo che non è di Store (il testo di un'eccezione) resta com'è [\"{enBody.Split('\n')[0]}\" | \"{enError}\" | \"{enRecovery.Split('\n')[0]}\"]");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// La copia di sicurezza in inglese: il riassunto (uno e tanti), la conferma dell'importazione con la data nella lingua di
    /// Missiva, il testo dopo l'esportazione con una password IMAP inclusa, e i dati di prima di un'importazione con le date.
    /// </summary>
    static void LisBackupTexts()
    {
        var created = new DateTime(2026, 9, 27, 12, 30, 0, DateTimeKind.Utc);
        var summary = new BackupSummary(created, [Copy2Imap("imap-prova")], 2, 1, 0, 0, 0, 0, 0, 0, 1);
        string[] aside = ["impostazioni-prima-dell-importazione-20261012-093000.json", "contatti-prima-dell-importazione-20261012-093001.dat"];
        // Una copia vera, letta come all'importazione: le righe degli account le scrive Backup.Describe, nella lingua di adesso.
        string readHow = Copy2Read(Copy2Content(new Settings { Accounts = [Copy2Imap("imap-prova")], NotificationsOff = ["imap-prova"] }), out var data, out _);
        string itContents = summary.Contents, itAside = UI.BackupUi.AsideLine(aside), itExported = UI.BackupUi.ExportedText(@"C:\Copie\Missiva.missiva-copia", summary, []);
        var itConfirm = UI.BackupUi.ConfirmText(summary, "0.3.1", hasData: true);
        string enContents, enAside, enExported, enQuestion;
        (string Heading, string Body, string Primary) enConfirm;
        try
        {
            Lingua.Force("en", "en-GB");
            enContents = summary.Contents;
            enAside = UI.BackupUi.AsideLine(aside);
            enExported = UI.BackupUi.ExportedText(@"C:\Copie\Missiva.missiva-copia", summary, ["A template can't be read on this PC anymore: it isn't in the backup."]);
            enConfirm = data == null ? ("", "", "") : UI.BackupUi.ConfirmText(Backup.Describe(data), "0.3.1", hasData: true);
            enQuestion = UI.BackupUi.AsideQuestion(aside).Heading;
        }
        finally
        {
            Lingua.Force("it");
        }
        var local = created.ToLocalTime();
        var read = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc).ToLocalTime(); // la data di Copy2Content
        Check(itContents == "1 account (mario@studio-prova.example), 2 firme, 1 modello, e le impostazioni"
              && enContents == "1 account (mario@studio-prova.example), 2 signatures, 1 template, and the settings"
              && itConfirm.Heading == "Sostituire tutto con questa copia?" && itConfirm.Body.StartsWith($"Copia del {local.ToString("d MMMM yyyy 'alle' HH:mm", LisItalian)}, fatta con Missiva 0.3.1.\nContiene: 1 account")
              && readHow == "letta" && enConfirm.Heading == "Replace everything with this backup?" && enConfirm.Primary == "Replace everything"
              && enConfirm.Body.StartsWith($"Backup from {read.ToString("d MMMM yyyy 'at' HH:mm", LisBritish)}, made with Missiva 0.3.1.\nIt contains: 1 account (mario@studio-prova.example), and the settings.")
              && enConfirm.Body.Contains("\n\nAccounts:\n- mario@studio-prova.example: IMAP, incoming mail from 127.0.0.1:993, outgoing from 127.0.0.1:465, notifications off\n\n")
              && enConfirm.Body.Contains("\n\nSign-ins aren't in the backup: accounts that aren't already signed in on this PC will ask you to sign in again, with the bar under the account (Sign in again or Update password).\n\n")
              && enConfirm.Body.EndsWith("\n\nTo finish, Missiva closes and opens again: your email downloads again from the servers.")
              && itExported.Contains("tranne l'account IMAP di cui hai incluso la password.") && enExported.Contains("except the IMAP account whose password you included.")
              && enExported.EndsWith("\n\nWarning: A template can't be read on this PC anymore: it isn't in the backup.")
              && itAside.Contains("prima di un'importazione (2 file, messi da parte il 12 ottobre 2026)") && enAside.Contains("before an import (2 files, set aside on 12 October 2026)")
              && enQuestion == "Delete the data from before the import?",
              $"lingua-impostazioni, la copia di sicurezza in inglese: riassunto (\"{enContents}\"), conferma con la data in inglese, testo dopo l'esportazione, dati di prima (\"{enAside[..Math.Min(90, enAside.Length)]}...\"); in italiano come prima");
    }

    /// <summary>
    /// Gli account in inglese: il tipo (Name, Describe), gli errori di Microsoft con il motivo del consenso di base ("read and
    /// send email") e uno dato da chi chiama, la mail all'amministratore (oggetto e testo in inglese), l'avviso della revoca di
    /// Google non confermata, i permessi di Google tolti (al più due, uniti con "and").
    /// </summary>
    static void LisAccountTexts()
    {
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = MicrosoftAuth.Organizations, ClientId = "11111111-2222-3333-4444-555555555555" };
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example" };
        const string mail = "https://mail.google.com/ openid email";
        Exception Consent() => new Microsoft.Identity.Client.MsalServiceException("invalid_grant", "AADSTS65001: consent required. Trace ID: 0");
        string itExplain = MicrosoftAuth.Explain(Consent(), work.Email, "app-prova").Text;
        string name, describe, explain, calendar, mailto, revoked, unknown;
        List<string> lost;
        try
        {
            Lingua.Force("en", "en-GB");
            name = AccountTypes.Name(AccountType.Microsoft365);
            describe = AccountTypes.Describe(work);
            explain = MicrosoftAuth.Explain(Consent(), work.Email, "app-prova").Text;
            calendar = MicrosoftAuth.Explain(Consent(), work.Email, "app-prova", "use the calendar").Text;
            unknown = MicrosoftAuth.Explain(new InvalidOperationException("boom"), "", "app-prova").Text;
            mailto = Uri.UnescapeDataString(UI.AdminConsentWindow.MailtoFor(work.Email, "https://login.example/consenso"));
            revoked = GoogleAuth.RevocationNotice(gmail, GoogleRevocation.Failed)?.Heading ?? "";
            lost = GoogleAuth.Lost($"{mail} {GoogleAuth.CalendarScope} {GoogleAuth.ContactsWriteScope}", mail);
        }
        finally
        {
            Lingua.Force("it");
        }
        Check(name == "Microsoft 365 (work or school)" && describe == "Microsoft 365 (work or school), organization's app 11111111-2222-3333-4444-555555555555"
              && itExplain.Contains("nessuno ha dato il consenso all'app per leggere e inviare la posta")
              && explain.StartsWith("Your organization hasn't approved Missiva yet: no one has given the app consent to read and send email.") && calendar.Contains("consent to use the calendar.")
              && unknown == "Sign-in failed: boom"
              && mailto.StartsWith("mailto:?subject=Approval for Missiva in Microsoft 365&body=Hi,\n\nTo use Missiva (an email app for Windows) with my work account mario.rossi@studio-prova.example, I need approval")
              && mailto.Contains("\n\nhttps://login.example/consenso\n\nThanks")
              && revoked == "Account removed, but Google access may remain" && lost is ["the calendar", "contacts"],
              $"lingua-impostazioni, gli account in inglese: tipo (\"{describe}\"), errori di Microsoft con il motivo del consenso (\"{explain[..Math.Min(80, explain.Length)]}...\"), la mail all'amministratore in inglese, " +
              $"la revoca di Google non confermata, i permessi tolti ({string.Join(", ", lost)}); in italiano il motivo di base è quello di prima");
    }

    /// <summary>
    /// I segnaposto dei modelli (Templates.Fill): {nome} e {data} come sempre, e anche {name} e {date}, in tutte e due le
    /// lingue e senza badare alle maiuscole; la data nella lingua di Missiva ("27 settembre 2026", "27 September 2026" con
    /// en-GB e anche con en-US, perché il formato è fisso). Senza nome {nome} e {name} restano. La nota del pannello vuoto
    /// in inglese parla di {name} e {date}.
    /// </summary>
    static void LisTemplates()
    {
        var day = new DateTime(2026, 9, 27);
        const string text = "Gentile {nome}, {name}: oggi {data}, today {date}. {NOME} {Date}";
        var italian = UI.Templates.Fill(text, "Mario", day);
        var nameless = UI.Templates.Fill("{nome} {name} {data}", null, day);
        var italianNote = UI.Templates.PanelNote(0, 0, 0, false);
        string british, american, englishNote;
        try
        {
            Lingua.Force("en", "en-GB");
            british = UI.Templates.Fill(text, "Mario", day);
            englishNote = UI.Templates.PanelNote(0, 0, 0, false);
            Lingua.Force("en", "en-US");
            american = UI.Templates.Fill("{date} {data}", null, day);
        }
        finally
        {
            Lingua.Force("it");
        }
        Check(italian == "Gentile Mario, Mario: oggi 27 settembre 2026, today 27 settembre 2026. Mario 27 settembre 2026"
              && nameless == "{nome} {name} 27 settembre 2026"
              && british == "Gentile Mario, Mario: oggi 27 September 2026, today 27 September 2026. Mario 27 September 2026"
              && american == "27 September 2026 27 September 2026"
              && italianNote.Contains("{nome} diventa il nome del primo destinatario, {data} la data di oggi")
              && englishNote.Contains("{name}") && englishNote.Contains("{date}") && !englishNote.Contains("{nome}"),
              $"lingua-impostazioni, i modelli: {{nome}}/{{name}} e {{data}}/{{date}} in tutte e due le lingue, la data nella lingua di Missiva " +
              $"(\"{italian}\"; \"{british}\"; en-US \"{american}\"; senza nome \"{nameless}\")");
    }
}
#endif
