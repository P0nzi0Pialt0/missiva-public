#if DEBUG || PROVA
namespace Missiva;

/// <summary>
/// Solo nella versione di prova (Debug e PROVA con MISSIVA_NO_READ): la casella è quella vera, quindi il provider vero sta
/// dentro questo, che gli passa tutte le letture e manda al registro ("prova: ...") tutte le scritture, restituendo quello
/// che restituirebbero fatte. Così Contrassegna, categorie, Posponi, azioni sulle cartelle, invio, e ogni azione nuova
/// di IMailProvider, non toccano la casella anche se chi le chiama non controlla MainWindow.Testing.
/// </summary>
public sealed class TestingProvider(IMailProvider real) : IMailProvider, IDisposable
{
    public MailAccount Account => real.Account;
    public bool IsClosed => real.IsClosed;
    public bool SupportsFocused => real.SupportsFocused;
    public bool PrefetchesMany => real.PrefetchesMany;
    public bool SchedulesOnServer => real.SchedulesOnServer;

    // ---------------------------------------------------------------- letture: al provider vero

    public Task<List<MailFolder>> GetFoldersAsync() => real.GetFoldersAsync();
    public Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null) => real.GetMessagesAsync(folder, focused, next, options);
    public bool SortsOnServer(SortBy sort) => real.SortsOnServer(sort);
    public Task<Dictionary<string, string>> GetPreviewsAsync(MailFolder folder, IReadOnlyList<MessageSummary> messages) => real.GetPreviewsAsync(folder, messages);
    public Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default) => real.SearchAsync(folder, query, next, cancel);
    public Task<MessageBody> GetBodyAsync(MessageSummary message, CancellationToken cancel = default) => real.GetBodyAsync(message, cancel);
    public Task PrefetchAsync(IReadOnlyList<MessageSummary> messages, CancellationToken cancel = default) => real.PrefetchAsync(messages, cancel);
    public MessageBody? KeptBody(MessageSummary message) => real.KeptBody(message);
    public MessageBody? SavedBody(MessageSummary message) => real.SavedBody(message);
    public Task<ListHeaders?> GetListHeadersAsync(MessageSummary message, CancellationToken cancel = default) => real.GetListHeadersAsync(message, cancel);
    /// <summary>Lavoro senza rete: la Posta in arrivo degli ultimi giorni, per scaricarne i corpi. Una lettura.</summary>
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, DateTime sinceUtc, int max) => real.GetRecentAsync(inbox, sinceUtc, max);
    public Task<List<MessageSummary>> GetRecentKeysAsync(MailFolder inbox, DateTime sinceUtc, int max) => real.GetRecentKeysAsync(inbox, sinceUtc, max);
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, IReadOnlyList<MessageSummary> keys) => real.GetRecentAsync(inbox, keys);
    public Task<List<MessageSummary>> GetConversationAsync(MessageSummary message, CancellationToken cancel = default) => real.GetConversationAsync(message, cancel);
    public Task<byte[]> GetAttachmentAsync(MessageSummary message, Attachment attachment, IProgress<long>? progress = null, CancellationToken cancel = default) =>
        real.GetAttachmentAsync(message, attachment, progress, cancel);
    public Task<List<Category>> GetCategoriesAsync() => real.GetCategoriesAsync();
    public Task<byte[]> GetRawAsync(MessageSummary message, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null) =>
        real.GetRawAsync(message, limit, cancel, progress);
    public Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc) => real.GetNewAsync(sinceUtc);
    /// <summary>I nuovi messaggi per UID: SEARCH e FETCH in sola lettura, al provider vero.</summary>
    public Task<Arrivals> GetNewAsync(string? mark, DateTime sinceUtc) => real.GetNewAsync(mark, sinceUtc);
    /// <summary>Per le regole di Missiva: UID SEARCH e FETCH in sola lettura, al provider vero; le azioni delle regole restano scritture.</summary>
    public Task<Arrivals?> GetArrivedAsync(string? mark, DateTime sinceUtc) => real.GetArrivedAsync(mark, sinceUtc);
    public Task<int> GetInboxUnreadAsync() => real.GetInboxUnreadAsync();
    /// <summary>IDLE ed EXAMINE: letture, al provider vero (la connessione dell'IDLE apre la Posta in arrivo in sola lettura).</summary>
    public Task WatchInboxAsync(Action<InboxNotice> changed, CancellationToken cancel) => real.WatchInboxAsync(changed, cancel);
    public bool HasInboxDelta => real.HasInboxDelta;
    public Task<InboxChanges?> GetInboxChangesAsync(CancellationToken cancel = default) => real.GetInboxChangesAsync(cancel);
    public Task<List<SentRecipients>> GetSentRecipientsAsync(int max) => real.GetSentRecipientsAsync(max);
    /// <summary>Non tocca la casella: chiude soltanto le connessioni di Missiva.</summary>
    public Task ResetConnectionsAsync() => real.ResetConnectionsAsync();
    /// <summary>Lo stesso, dopo la ripresa del PC o un cambio di rete: non manda nulla al server.</summary>
    public void DropConnections() => real.DropConnections();
    public bool HasCalendar => real.HasCalendar;
    public Task<CalendarData?> GetCalendarAsync(DateTime from, DateTime to, CancellationToken cancel = default) => real.GetCalendarAsync(from, to, cancel);
    public bool HasAutoReplies => real.HasAutoReplies;
    public Task<AutoReplies?> GetAutoRepliesAsync() => real.GetAutoRepliesAsync();
    public bool HasContacts => real.HasContacts;
    public bool ContactsReadOnly => real.ContactsReadOnly;
    public Task<List<Contact>?> GetContactsAsync(CancellationToken cancel = default) => real.GetContactsAsync(cancel);
    public Task<List<Availability>?> GetScheduleAsync(IReadOnlyList<string> emails, DateTime from, DateTime to) => real.GetScheduleAsync(emails, from, to);
    public bool HasPeople => real.HasPeople;
    public Task<List<Contact>?> SearchPeopleAsync(string text, CancellationToken cancel = default) => real.SearchPeopleAsync(text, cancel);
    public bool HasServerRules => real.HasServerRules;
    public Task<List<MailRule>?> GetRulesAsync() => real.GetRulesAsync();
    /// <summary>Coda delle azioni senza rete: quali messaggi ci sono ancora, con EXAMINE. Una lettura; le azioni poi vanno solo nel registro.</summary>
    public Task<List<MessageSummary>?> StillThereAsync(IReadOnlyList<MessageSummary> messages) => real.StillThereAsync(messages);

    // ---------------------------------------------------------------- scritture: solo nel registro

    static Task Pretend(string what)
    {
        Log.Write("prova: " + what);
        return Task.CompletedTask;
    }

    public Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read) =>
        Pretend($"segnerei {(read ? "letti" : "da leggere")} {messages.Count} messaggi");

    public Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged) =>
        Pretend(flagged ? $"contrassegnerei {messages.Count} messaggi" : $"toglierei il contrassegno a {messages.Count} messaggi");

    public Task SetFlagDueAsync(IReadOnlyList<MessageSummary> messages, DateTime? start, DateTime? due) =>
        Pretend($"contrassegnerei {messages.Count} messaggi di {Account.Email} " + (due is { } d ? $"con scadenza {d:dd/MM/yyyy}" : "senza data") +
                (start is { } s && due != null && s.Date != due.Value.Date ? $" (inizio {s:dd/MM/yyyy})" : ""));

    public Task CompleteFlagAsync(IReadOnlyList<MessageSummary> messages) => Pretend($"segnerei come completati {messages.Count} messaggi di {Account.Email}");

    public async Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination)
    {
        await Pretend($"sposterei {messages.Count} messaggi in {destination.Name}");
        return messages.Where(m => m.FolderId != destination.Id).Select(m => new MovedMessage(m, m.Id)).ToList();
    }

    public async Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination)
    {
        await Pretend($"annullerei, {moved.Count} messaggi tornerebbero da {destination.Name}");
        return moved.Select(x => x with { NewId = x.Original.Id }).ToList();
    }

    public async Task<Category> CreateCategoryAsync(string name, int preset)
    {
        await Pretend($"creerei la categoria \"{name.Trim()}\"");
        return new Category(name.Trim(), preset);
    }

    /// <summary>Come il provider vero, aggiorna le categorie dei messaggi: l'elenco cambia come se fosse fatto.</summary>
    public async Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on)
    {
        await Pretend($"{(on ? "metterei" : "toglierei")} la categoria \"{category}\" a {messages.Count} messaggi");
        foreach (var message in messages)
        {
            message.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (on) message.Categories.Add(category);
        }
    }

    public async Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent)
    {
        await Pretend($"creerei la cartella \"{name.Trim()}\"{(parent != null ? " in " + parent.Name : "")}");
        return new MailFolder { Id = name.Trim(), Name = name.Trim(), Kind = FolderKind.Other, ParentId = parent?.Id };
    }

    public async Task<string> RenameFolderAsync(MailFolder folder, string name)
    {
        await Pretend($"rinominerei la cartella {folder.Name} in \"{name.Trim()}\"");
        return folder.Id;
    }

    public async Task<string> MoveFolderAsync(MailFolder folder, MailFolder? parent)
    {
        await Pretend($"sposterei la cartella {folder.Name} {(parent != null ? "in " + parent.Name : "al primo livello")}");
        return folder.Id;
    }

    public Task EmptyFolderAsync(MailFolder folder, MailFolder? trash) =>
        Pretend($"svuoterei {folder.Name}{(trash != null ? ", nel Cestino" : ", per sempre")}");

    public Task MarkAllReadAsync(MailFolder folder) => Pretend($"segnerei letti tutti i messaggi di {folder.Name}");

    public Task DeleteFolderAsync(MailFolder folder) => Pretend($"eliminerei la cartella {folder.Name}");

    /// <summary>Il Cestino se c'è già (lettura); se manca e il provider vero lo creerebbe (IMAP), uno finto, senza crearlo.</summary>
    public async Task<MailFolder?> EnsureTrashAsync()
    {
        var existing = (await real.GetFoldersAsync()).FirstOrDefault(f => f.Kind == FolderKind.Deleted);
        if (existing != null || Account.Kind != AccountKind.Imap) return existing;
        await Pretend($"creerei il Cestino (Trash) di {Account.Email}");
        return new MailFolder { Id = "Trash", Name = "Cestino", Kind = FolderKind.Deleted };
    }

    /// <summary>La cartella se c'è già (lettura); se manca, una finta, senza crearla.</summary>
    public async Task<MailFolder> EnsureFolderAsync(string name)
    {
        var existing = (await real.GetFoldersAsync()).FirstOrDefault(f => f.ParentId == null && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        await Pretend($"creerei la cartella \"{name}\"");
        return new MailFolder { Id = name, Name = name, Kind = FolderKind.Other };
    }

    public async Task<string> SnoozeAsync(MessageSummary message, MailFolder snoozeFolder)
    {
        await Pretend($"posporrei il messaggio {message.Id} in {snoozeFolder.Name}");
        return message.StableId.Length > 0 ? message.StableId : message.Id;
    }

    public async Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox)
    {
        await Pretend($"riporterei il messaggio {key} da {snoozeFolder.Name} in {inbox.Name}");
        return key;
    }

    public Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) =>
        Pretend($"copierei {messages.Count} messaggi in {destination.Name}");

    public Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages) => Pretend($"eliminerei per sempre {messages.Count} messaggi");

    public Task SendAsync(OutgoingMessage message) =>
        Pretend($"invierei \"{message.Subject}\" da {(message.From is { } identity ? $"{identity.Email} (altro indirizzo di {Account.Email})" : Account.Email)} " +
                $"a {message.To.Count + message.Cc.Count + message.Bcc.Count} destinatari " +
                $"(A: {string.Join(", ", message.To.Select(a => a.Email))}), " +
                (message.Mode is ComposeMode.Reply or ComposeMode.ReplyAll && message.Original != null ? $"risposta al messaggio {message.Original.Id} con la citazione, " : "") +
                $"{message.AttachmentPaths.Count + message.KeptAttachments.Count} allegati, {message.Images.Count} immagini incorporate" +
                (message.Draft != null ? ", continuando una bozza" : "") +
                (message.Importance != Importance.Normal ? $", importanza {(message.Importance == Importance.High ? "alta" : "bassa")}" : "") +
                (message.ReadReceipt ? ", conferma di lettura" : "") + (message.DeliveryReceipt ? ", conferma di recapito" : "") +
                (message.SendAt is { } at ? $", pianificato per {at:dd/MM/yyyy HH:mm} (lo terrebbe Exchange)" : ""));

    public async Task<bool> SendDraftAsync(string draftKey)
    {
        await Pretend($"invierei la bozza pianificata {draftKey} di {Account.Email}");
        return true;
    }

    /// <summary>La finestra di scrittura lo controlla già da sé; qui una bozza finta, se mai ci si arrivasse.</summary>
    public async Task<SavedDraft> SaveDraftAsync(OutgoingMessage message)
    {
        await Pretend($"salverei la bozza \"{message.Subject}\" di {Account.Email}, {message.Images.Count} immagini incorporate");
        return new SavedDraft { Message = new MessageSummary { Id = "prova", FolderId = "prova", Subject = message.Subject, IsDraft = true } };
    }

    public Task DiscardDraftAsync(SavedDraft draft) => Pretend($"eliminerei dalle Bozze la bozza {draft.Message.Id} di {Account.Email}");

    public Task RespondAsync(MessageSummary message, Invitation invite, InviteResponse response) =>
        Pretend($"risponderei {response.Answer switch { InviteAnswer.Accept => "accetta", InviteAnswer.Tentative => "provvisorio", InviteAnswer.Decline => "rifiuta", _ => "proponi" }} " +
                $"all'invito \"{invite.Summary}\" di {invite.Organizer?.Email ?? "?"} (UID {invite.Uid}, {(invite.GraphMessageId != null ? "evento di Outlook" : "iMIP")}, " +
                $"{(response.Send ? "con la risposta all'organizzatore" : "senza risposta")}{(response.Comment.Length > 0 ? ", con una nota" : "")}" +
                $"{(response.NewStart is { } s ? $", nuovo orario {s:dd/MM HH:mm}" : "")})");

    /// <summary>Il consenso non tocca la casella, ma nella prova nessuno può darlo: solo nel registro, senza finestre.</summary>
    public Task AllowCalendarAsync() => Pretend($"chiederei il consenso al calendario per {Account.Email}");

    public Task AllowMailboxSettingsAsync() => Pretend($"chiederei il consenso alle impostazioni della casella per {Account.Email}");

    public Task AllowContactsAsync() => Pretend($"chiederei il consenso ai contatti per {Account.Email}");

    public Task AllowContactsWriteAsync() => Pretend($"chiederei il consenso alla modifica dei contatti per {Account.Email}");

    public Task AllowPeopleAsync() => Pretend($"chiederei il consenso alle persone (colleghi) per {Account.Email}");

    public async Task<string> SaveContactAsync(Contact? before, Contact after)
    {
        if (real is GmailProvider)
        {
            // Google: i campi che l'unione scriverebbe (GoogleContacts.Merge), mai una richiesta alla People API.
            var fields = GoogleContacts.Merge(null, before, after).Fields;
            await Pretend(before == null ? $"creerei il contatto \"{after.Name}\" in {Account.Email} (Google: {string.Join(", ", fields)})"
                : fields.Count == 0 ? $"nessuna modifica al contatto \"{after.Name}\", nessuna richiesta"
                : $"aggiornerei il contatto \"{after.Name}\" di {Account.Email} (Google: {string.Join(", ", fields)})");
            return before?.ServerId ?? "people/c0000000000000000001";
        }
        var changes = ContactFiles.ToGraph(before, after);
        await Pretend(before == null ? $"creerei il contatto \"{after.Name}\" in {Account.Email} ({changes.Count} campi)"
            : changes.Count == 0 ? $"nessuna modifica al contatto \"{after.Name}\", nessuna richiesta"
            : $"modificherei il contatto \"{after.Name}\" di {Account.Email}: {string.Join(", ", changes.Select(c => c.Key))}");
        return before?.ServerId ?? "prova";
    }

    public Task DeleteContactAsync(Contact contact) => Pretend($"eliminerei il contatto \"{contact.Name}\" di {Account.Email}");

    public Task AllowSharedAsync() =>
        Pretend($"chiederei il consenso alle caselle condivise per {Account.Email}" +
                (Store.Settings.Accounts.FirstOrDefault(a => a.Id == Account.SharedOf) is { } owner ? $" (con l'account {owner.Email})" : ""));

    public async Task<string> CreateEventAsync(NewEvent e)
    {
        var people = e.Required.Count + e.Optional.Count;
        await Pretend($"creerei l'evento \"{e.Subject}\" in {Account.Email} ({(e.CalendarId.Length > 0 ? "calendario " + e.CalendarId : "calendario predefinito")}), " +
                      (e.AllDay ? $"il {e.Start:dd/MM/yyyy}, giornata intera" : $"dal {e.Start:dd/MM/yyyy HH:mm} al {e.End:dd/MM/yyyy HH:mm}") +
                      (people > 0 ? $", riunione con {people} partecipanti ({e.Optional.Count} facoltativi)" : "") +
                      (e.OnlineMeeting ? ", riunione di Teams" : "") +
                      (e.Repeat != EventRepeat.None ? $", si ripete {e.Repeat switch { EventRepeat.Daily => "ogni giorno", EventRepeat.Weekly => "ogni settimana", _ => "ogni mese" }}" : "") +
                      (e.ReminderMinutes is { } m ? $", promemoria {m} minuti prima" : ", senza promemoria"));
        return "prova";
    }

    public Task RespondToEventAsync(CalendarEvent e, InviteAnswer answer, string comment, bool send) =>
        Pretend($"risponderei {answer switch { InviteAnswer.Accept => "accetta", InviteAnswer.Decline => "rifiuta", _ => "provvisorio" }} all'evento \"{e.Subject}\" " +
                $"di {e.Organizer?.Email ?? "?"} ({(send ? "con la risposta all'organizzatore" : "senza risposta")}{(comment.Trim().Length > 0 ? ", con una nota" : "")})");

    public Task UpdateEventAsync(CalendarEvent original, NewEvent changed, bool series)
    {
        var patch = Calendars.Changes(original, changed, TimeZoneInfo.Local.Id);
        return Pretend(patch.Count == 0 ? $"nessuna modifica all'evento \"{original.Subject}\", nessuna richiesta"
            : $"modificherei l'evento \"{original.Subject}\" di {Account.Email} ({(series && original.SeriesMasterId.Length > 0 ? "tutta la serie" : original.IsRecurring ? "solo questo evento" : "evento singolo")}): " +
              $"{Calendars.ChangedText(patch)}" + (patch.ContainsKey("start") ? $", dal {changed.Start:dd/MM/yyyy HH:mm} al {changed.End:dd/MM/yyyy HH:mm}" : "") +
              (changed.IsMeeting ? $", aggiornamento a {changed.Required.Count + changed.Optional.Count} partecipanti" : ""));
    }

    public Task DeleteEventAsync(CalendarEvent e, bool series) =>
        Pretend($"eliminerei l'evento \"{e.Subject}\" di {Account.Email} ({(series && e.SeriesMasterId.Length > 0 ? "tutta la serie" : "solo questo evento")})");

    public Task CancelEventAsync(CalendarEvent e, string comment, bool series) =>
        Pretend($"annullerei la riunione \"{e.Subject}\" di {Account.Email} ({(series && e.SeriesMasterId.Length > 0 ? "tutta la serie" : "solo questo evento")}, " +
                $"{e.Attendees.Count} partecipanti{(comment.Trim().Length > 0 ? ", con una nota" : ", senza nota")})");

    public Task SetAutoRepliesAsync(AutoReplies replies) =>
        Pretend(replies.State switch
        {
            AutoReplyState.Scheduled => $"imposterei le risposte automatiche di {Account.Email} dal {replies.Start:dd/MM/yyyy HH:mm} al {replies.End:dd/MM/yyyy HH:mm}",
            AutoReplyState.On => $"imposterei le risposte automatiche di {Account.Email}, attive da adesso",
            _ => $"spegnerei le risposte automatiche di {Account.Email}",
        } + $" (esterni: {replies.External switch { AutoReplyAudience.None => "nessuno", AutoReplyAudience.Contacts => "solo i contatti", _ => "tutti" }}, " +
            $"messaggio interno {replies.InternalHtml.Length} caratteri, esterno {replies.ExternalHtml.Length})");

    public async Task<string> SaveRuleAsync(MailRule? before, MailRule after)
    {
        var changes = before == null ? null : Rules.Changes(before, after);
        await Pretend(before == null ? $"creerei la regola \"{after.Name}\" in {Account.Email}: {Rules.Describe(after)}"
            : changes!.Count == 0 ? $"nessuna modifica alla regola \"{after.Name}\", nessuna richiesta"
            : $"modificherei la regola \"{after.Name}\" di {Account.Email}: {Rules.ChangedText(changes)}");
        return before?.ServerId ?? "prova";
    }

    public Task DeleteRuleAsync(MailRule rule) => Pretend($"eliminerei la regola \"{rule.Name}\" di {Account.Email}");

    public void Dispose() => (real as IDisposable)?.Dispose();
}
#endif
