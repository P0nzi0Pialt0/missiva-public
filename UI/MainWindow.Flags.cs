using System.Windows;
using System.Windows.Controls;

namespace Missiva.UI;

/// <summary>
/// Contrassegna con scadenza e promemoria, come il nuovo Outlook e l'Outlook classico: il sottomenu di Contrassegna (Oggi,
/// Domani, Questa settimana, Settimana prossima, Nessuna data, Data personalizzata, Imposta promemoria, Segna come
/// completato, Cancella contrassegno), la scadenza sulla riga e i promemoria come notifiche, anche con Missiva
/// nell'area di notifica. Graph scrive le date nel contrassegno; per Gmail e IMAP il server ha solo \Flagged e le date le
/// tiene Missiva (Flags, Settings.Flags cifrati come i Posposti). Nella prova le scritture vanno solo nel registro.
/// </summary>
public partial class MainWindow
{
    MenuItem FlagMenu(List<MessageItem> items)
    {
        bool allFlagged = items.All(m => m.IsFlagged);
        var menu = MenuEntry(allFlagged ? T("Contrassegno") : T("Contrassegna"), "", ContextMenus.Brush(ContextMenus.Tone.Red));
        void Option(string text, string? when, Func<Task> action)
        {
            var header = new DockPanel();
            if (when != null)
            {
                var right = new TextBlock { Text = when, Margin = new Thickness(24, 0, 0, 0) };
                right.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                DockPanel.SetDock(right, Dock.Right);
                header.Children.Add(right);
            }
            header.Children.Add(new TextBlock { Text = text });
            var item = new MenuItem { Header = header };
            System.Windows.Automation.AutomationProperties.SetName(item, text);
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }
        foreach (var choice in Flags.Choices(DateTime.Now))
            Option(choice.Name, FlagDay(choice.Due), () => FlagWithDueAsync(items, choice.Start, choice.Due, choice.Name)); // già nella lingua di Missiva (Flags.Choices)
        Option(T("Nessuna data"), null, () => FlagWithDueAsync(items, null, null, "Nessuna data"));
        Option(T("Data personalizzata…"), null, async () =>
        {
            var dialog = new SnoozeWindow(T("Scadenza"), T("Scadenza il"), T("Contrassegna"), withTime: false) { Owner = this };
            if (dialog.ShowDialog() == true) await FlagWithDueAsync(items, null, dialog.Until.Date, "Data personalizzata");
        });
        menu.Items.Add(new Separator());
        Option(T("Imposta promemoria…"), null, async () =>
        {
            var dialog = new SnoozeWindow(T("Promemoria"), T("Promemoria il"), T("Imposta")) { Owner = this };
            if (dialog.ShowDialog() == true) await SetFlagReminderAsync(items, dialog.Until);
        });
        if (items.Any(m => m.IsFlagged))
        {
            menu.Items.Add(new Separator());
            Option(T("Segna come completato"), null, () => CompleteFlagsAsync(items));
            Option(T("Cancella contrassegno"), null, () => ClearFlagsAsync(items));
        }
        return menu;
    }

    /// <summary>
    /// Il giorno accanto a una scelta di Contrassegna: in italiano "mar 21/10", come sempre; in inglese con il mese in lettere,
    /// nell'ordine della cultura ("Tue 21 Oct", "Tue Oct 21"), perché 21/10 e 10/21 non si confondano.
    /// </summary>
    internal static string FlagDay(DateTime due) =>
        due.ToString(Lingua.English ? "ddd " + Lingua.Culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM") : "ddd d/MM", Lingua.Culture);

    /// <summary>Contrassegnati con una scadenza (o senza data): Graph la scrive nel contrassegno, per Gmail e IMAP la tiene Missiva.</summary>
    async Task FlagWithDueAsync(List<MessageItem> items, DateTime? start, DateTime? due, string choice)
    {
        if (items.Count == 0) return;
        Log.Write($"contrassegno: {choice} per {items.Count} messaggi" + (due is { } d ? $", scadenza {d:dd/MM/yyyy}" : ""));
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var account = group.Key.Provider.Account;
            var messages = Targets(group);
            var write = messages.Where(m => Flags.NeedsWrite(account, m, due)).ToList(); // niente richieste senza modifiche
            foreach (var m in messages)
            {
                var reminder = Flags.Find(account, m)?.Reminder;
                if (account.Kind == AccountKind.Microsoft) m.FlagDue = due;
                Flags.Remember(account, m, start, due, reminder);
            }
            foreach (var item in group) item.IsFlagged = true;
            if (write.Count > 0) await RunAsync("contrassegno", () => group.Key.Provider.SetFlagDueAsync(write, start, due));
        }
        Store.Save();
        AfterFlagChange(items);
    }

    /// <summary>Imposta promemoria: all'ora scelta una notifica, come Outlook; il messaggio si contrassegna se non lo era.</summary>
    async Task SetFlagReminderAsync(List<MessageItem> items, DateTime at)
    {
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var account = group.Key.Provider.Account;
            var messages = Targets(group);
            var unflagged = messages.Where(m => !m.IsFlagged).ToList();
            foreach (var m in messages)
            {
                var known = Flags.Find(account, m);
                Flags.Remember(account, m, known?.Start, known?.Due ?? m.FlagDue, at);
            }
            foreach (var item in group) item.IsFlagged = true;
            if (unflagged.Count > 0) await RunAsync("contrassegno", () => group.Key.Provider.SetFlagAsync(unflagged, true));
        }
        Store.Save();
        Log.Write($"contrassegno: promemoria il {at:dd/MM/yyyy HH:mm} per {items.Count} messaggi");
        AfterFlagChange(items);
    }

    /// <summary>Segna come completato: Graph lo tiene fra i completati (complete), Gmail e IMAP tolgono \Flagged; date e promemoria escono.</summary>
    async Task CompleteFlagsAsync(List<MessageItem> items)
    {
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var messages = Targets(group).Where(m => m.IsFlagged).ToList();
            foreach (var m in messages)
            {
                Flags.Forget(group.Key.Provider.Account, m);
                m.FlagDue = null;
            }
            foreach (var item in group) item.IsFlagged = false;
            if (messages.Count > 0) await RunAsync("contrassegno", () => group.Key.Provider.CompleteFlagAsync(messages));
        }
        Store.Save();
        AfterFlagChange(items);
    }

    /// <summary>Cancella contrassegno: niente più contrassegno, date né promemoria.</summary>
    async Task ClearFlagsAsync(List<MessageItem> items)
    {
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var messages = Targets(group);
            foreach (var m in messages)
            {
                Flags.Forget(group.Key.Provider.Account, m);
                m.FlagDue = null;
            }
            foreach (var item in group) item.IsFlagged = false;
            // Senza rete in coda, come il contrassegno (ActionQueue): le date e il promemoria di Missiva sono già tolti.
            await RunOrQueueAsync(group.Key, "contrassegno", () => group.Key.Provider.SetFlagAsync(messages, false), () => Enqueue(group.Key, messages, QueuedKind.Unflag));
        }
        Store.Save();
        AfterFlagChange(items);
    }

    /// <summary>Le righe mostrano la scadenza nuova; in Contrassegnati di tutti gli account l'elenco si rifà, per scadenza.</summary>
    void AfterFlagChange(List<MessageItem> items)
    {
        foreach (var item in items) item.RefreshFlag();
        if (unified is { View: UnifiedView.Flagged } && !searching) reloading = ReloadAsync();
    }

    // ---------------------------------------------------------------- promemoria dei contrassegni

    /// <summary>
    /// Ogni minuto (con il controllo di Posponi): i promemoria dei contrassegni arrivati, come notifiche con Posponi
    /// promemoria e Segna come completato, anche con la finestra nell'area di notifica; ognuno una volta sola (il
    /// promemoria si toglie). Nella versione di prova solo "prova: notificherei il promemoria ..." nel registro.
    /// </summary>
    void CheckFlagReminders(DateTime now)
    {
        var due = TakeDueReminders(S.Flags, now, id => accounts.Any(a => a.Provider.Account.Id == id));
        if (due.Count == 0) return;
        foreach (var f in due)
        {
            var account = accounts.First(a => a.Provider.Account.Id == f.AccountId);
            var when = f.Due is { } d ? Flags.DueText(d, DateTime.Today).ToLowerInvariant() : T("senza scadenza");
            if (Testing)
            {
                Log.Write($"prova: notificherei il promemoria del contrassegno \"{f.Subject}\" di {f.From} ({when}, {account.Header})");
                continue;
            }
            try
            {
                var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddArgument("account", f.AccountId).AddArgument("flag", f.Key).AddArgument("flagmessage", f.MessageId).AddArgument("flagfolder", f.FolderId)
                    .AddText(f.Subject.Length > 0 ? f.Subject : T("(nessun oggetto)"))
                    .AddText($"{f.From} · {when}")
                    .AddAttributionText(account.Header)
                    .AddButton(new Microsoft.Toolkit.Uwp.Notifications.ToastButton().SetContent(T("Posponi promemoria")).AddArgument("action", "flagsnooze").SetBackgroundActivation())
                    .AddButton(new Microsoft.Toolkit.Uwp.Notifications.ToastButton().SetContent(T("Segna come completato")).AddArgument("action", "flagdone").SetBackgroundActivation());
                App.Show(toast);
                Log.Write($"promemoria del contrassegno di {Log.Private(f.Subject)} ({account.Header})");
            }
            catch (Exception ex)
            {
                Log.Error("promemoria del contrassegno", ex);
            }
        }
        Store.Save();
    }

    /// <summary>
    /// I promemoria dei contrassegni arrivati, tolti dalle loro voci: solo quelli degli account che ci sono. La voce di un
    /// account che non è (o non è ancora) fra gli account tiene il suo promemoria, per quando l'account ci sarà: prima si
    /// azzerava senza notifica, e il salvataggio subito dopo lo toglieva dal file (audit dell'11 ottobre, C-58).
    /// </summary>
    internal static List<FlagDate> TakeDueReminders(IEnumerable<FlagDate> flags, DateTime now, Func<string, bool> hasAccount)
    {
        var due = flags.Where(f => f.Reminder is { } at && at <= now && hasAccount(f.AccountId)).ToList();
        foreach (var f in due) f.Reminder = null;
        return due;
    }

    /// <summary>
    /// Dalla notifica del promemoria, senza aprire la finestra: Posponi promemoria (fra un'ora) o Segna come completato,
    /// sul messaggio ritrovato dall'account, dall'id con la cartella e dalla chiave salvati, mai dal testo della notifica né
    /// dal solo UID (si ripete da una cartella all'altra).
    /// </summary>
    /// <param name="messageId">L'id e la cartella della voce quando la notifica è partita (null nelle notifiche di prima delle correzioni del giro P4).</param>
    public async Task FlagReminderActionAsync(string accountId, string key, string? messageId, string? folderId, string action)
    {
        await accountsReady.Task;
        var f = S.Flags.FirstOrDefault(x => x.AccountId == accountId && messageId != null && x.MessageId == messageId && x.FolderId == folderId)
                ?? (key.Length > 0 ? S.Flags.FirstOrDefault(x => x.AccountId == accountId && x.Key == key) : null);
        var account = accounts.FirstOrDefault(a => a.Provider.Account.Id == accountId);
        if (f == null || account == null) return;
        var mail = account.Provider.Account;
        if (action == "flagsnooze")
        {
            f.Reminder = DateTime.Now.AddHours(1);
            Store.Save();
            Log.Write($"promemoria del contrassegno posposto alle {f.Reminder:HH:mm}");
            return;
        }
        // La riga dell'elenco aperto di cui è la voce (stesso id nella stessa cartella, o spostato con la stessa chiave),
        // altrimenti il messaggio dove la voce dice che sta.
        bool graph = mail.Kind == AccountKind.Microsoft;
        var message = rows.Rows.Where(r => r.Account == account).Select(r => r.Summary).FirstOrDefault(s => Flags.Find(mail, s) == f)
                      ?? new MessageSummary { Id = f.MessageId, FolderId = f.FolderId, StableId = graph ? f.MessageId : f.Key, InternetMessageId = graph ? f.Key : "", IsFlagged = true };
        S.Flags.Remove(f);
        Store.Save();
        foreach (var row in rows.Rows.Where(r => r.Summary == message)) { row.IsFlagged = false; row.RefreshFlag(); }
        await RunAsync("contrassegno", () => account.Provider.CompleteFlagAsync([message]));
    }
}
