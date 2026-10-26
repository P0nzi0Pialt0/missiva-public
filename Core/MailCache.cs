using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>Il JSON della copia locale scritto dal compilatore, come quello delle impostazioni (SettingsJson): all'avvio si legge subito.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<MailFolder>))]
[JsonSerializable(typeof(List<MessageSummary>))]
sealed partial class CacheJson : JsonSerializerContext;

/// <summary>
/// Copia locale delle cartelle e della prima pagina di ogni cartella, come fanno Outlook e Thunderbird: all'avvio
/// Missiva mostra subito quella e intanto chiede al server lo stato aggiornato. Sta in %LOCALAPPDATA%\Missiva\cache,
/// una cartella per account, e si cancella quando l'account viene rimosso.
/// </summary>
public static class MailCache
{

    static string AccountDir(MailAccount account) => Store.CachePath(Store.CacheName(account.Id));

    static string FolderFile(MailAccount account, string folderId, bool? focused)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folderId)))[..16].ToLowerInvariant();
        var suffix = focused switch { true => "-evidenziata", false => "-altra", _ => "" };
        return Path.Combine(AccountDir(account), $"{hash}{suffix}.json");
    }

    /// <summary>
    /// Le cartelle della copia locale. I nomi delle cartelle speciali sono quelli di Missiva nella lingua di quando la
    /// copia è stata scritta: si rimettono in quella di adesso (MailFolder.SpecialName), così dopo un cambio di lingua
    /// non restano nell'altra fino alla lettura dal server (o, senza rete, per tutta la sessione).
    /// </summary>
    public static List<MailFolder>? LoadFolders(MailAccount account) =>
        Read<List<MailFolder>>(Path.Combine(AccountDir(account), "cartelle.json"))?
            .Select(f => MailFolder.SpecialShown(account.Kind, f.Kind) is { Length: > 0 } shown && f.Name != shown ? f.Renamed(shown) : f)
            .ToList();

    public static void SaveFolders(MailAccount account, List<MailFolder> folders) =>
        Write(Path.Combine(AccountDir(account), "cartelle.json"), folders);

    public static List<MessageSummary>? LoadMessages(MailAccount account, string folderId, bool? focused)
    {
        var messages = Read<List<MessageSummary>>(FolderFile(account, folderId, focused));
        // Le copie salvate prima che le anteprime fossero solo testo ("[image: ...]", indirizzi web).
        foreach (var m in messages ?? new()) m.Preview = MailHtml.PlainPreview(m.Preview);
        return messages;
    }

    /// <summary>I messaggi salvati delle cartelle dell'account, tranne la Posta indesiderata (per la prima rubrica dei suggerimenti).</summary>
    public static IEnumerable<MessageSummary> AllMessages(MailAccount account)
    {
        foreach (var folder in (LoadFolders(account) ?? new()).Where(f => f.Kind != FolderKind.Junk))
            foreach (var focused in new bool?[] { null, true, false })
            {
                var path = FolderFile(account, folder.Id, focused);
                if (!File.Exists(path)) continue;
                foreach (var m in Read<List<MessageSummary>>(path) ?? new()) yield return m;
            }
    }

    public static void SaveMessages(MailAccount account, string folderId, bool? focused, IEnumerable<MessageSummary> messages) =>
        Write(FolderFile(account, folderId, focused), messages.ToList());

    /// <summary>
    /// Quando è stata scritta la copia della prima pagina della cartella (ora di Windows), null se non c'è: la riga sopra
    /// l'elenco lo dice quando il server non risponde e a schermo resta la copia (audit di affidabilità del 25 ottobre 2026,
    /// elenco, E-01).
    /// </summary>
    public static DateTime? SavedAt(MailAccount account, string folderId, bool? focused)
    {
        try
        {
            var path = FolderFile(account, folderId, focused);
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch (Exception ex)
        {
            Log.Error("data della copia locale", ex);
            return null;
        }
    }

    /// <summary>
    /// I messaggi appena arrivati in cima alla prima pagina salvata di una cartella, senza doppioni, dal più recente. Solo se
    /// la copia c'è: una cartella mai aperta non ne ha, e la prima apertura la chiede al server. Così la Posta in arrivo
    /// aperta dopo l'arrivo (un altro account, la vista di tutti gli account, un riavvio) mostra subito i nuovi: prima la
    /// copia restava quella dell'ultima apertura, e il messaggio della notifica non c'era finché non rispondeva il server.
    /// </summary>
    /// <returns>Quanti ne sono entrati.</returns>
    public static int Prepend(MailAccount account, string folderId, bool? focused, IReadOnlyList<MessageSummary> incoming)
    {
        if (incoming.Count == 0 || LoadMessages(account, folderId, focused) is not { } page) return 0;
        var known = page.Select(m => m.Id).ToHashSet();
        var added = incoming.Where(m => known.Add(m.Id)).OrderByDescending(m => m.Instant).ToList();
        if (added.Count > 0) SaveMessages(account, folderId, focused, added.Concat(page));
        return added.Count;
    }

    /// <summary>
    /// La cartella è stata eliminata: la copia della sua prima pagina (anche Evidenziata e Altra) si toglie. Restava sul
    /// disco finché l'account non usciva da Missiva, e una cartella nuova con lo stesso nome si apriva per un momento con i
    /// messaggi di quella di prima (scelte del 12 ottobre, trovato con C-47).
    /// </summary>
    public static void ForgetFolder(MailAccount account, string folderId)
    {
        foreach (var focused in new bool?[] { null, true, false })
        {
            try { File.Delete(FolderFile(account, folderId, focused)); }
            catch (DirectoryNotFoundException) { } // l'account non ha ancora nessuna copia
            catch (Exception ex) { Log.Error("copia locale della cartella eliminata", ex); }
        }
    }

    /// <summary>
    /// Le copie scritte in chiaro dalle versioni di prima (anche quelle delle cartelle che non si riaprono) si cifrano una
    /// volta, in sottofondo, qualche secondo dopo l'avvio: poi nessun oggetto resta leggibile nella cartella della copia.
    /// </summary>
    public static void EncryptOld() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        int done = 0;
        try
        {
            var root = Store.CachePath("");
            if (!Directory.Exists(root)) return;
            EncryptOldIn(root, ref done);
        }
        catch (Exception ex)
        {
            Log.Error("cifratura della copia locale", ex);
        }
        if (done > 0) Log.Write($"copia locale: {done} file scritti in chiaro dalle versioni di prima ora cifrati");
    });

    /// <summary>
    /// Il giro di EncryptOld su una cartella della copia: ogni .json, anche nelle sottocartelle, letto per intero per
    /// guardarne il primo byte, e cifrato se comincia come un JSON in chiaro. A parte per poterlo misurare e provare
    /// (audit dell'11 ottobre, C-06): gira a ogni avvio, anche quando non c'è più nulla da cifrare.
    /// </summary>
    /// <param name="done">Quanti file ha cifrato fin qui, anche se poi si ferma per un errore.</param>
    internal static void EncryptOldIn(string root, ref int done)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes[0] is not ((byte)'[' or (byte)'{')) continue;
            SafeFile.WriteCopy(path, SafeFile.Protect(bytes));
            done++;
        }
    }

    public static void Delete(MailAccount account)
    {
        BodyCache.Delete(account); // prima i corpi, perché una loro scrittura in corso non ricrei la cartella dopo
        // Si cancella una cartella intera: solo dentro la copia locale. Il nome viene già da Store.CacheName, che non lascia
        // uscire nessun identificativo; qui lo si controlla lo stesso, sul percorso risolto (audit dell'11 ottobre, T-14).
        var dir = AccountDir(account);
        if (!Store.IsInside(dir, Store.CachePath("")))
        {
            Log.Write("copia locale di un account rimosso non cancellata: il suo percorso non sta nella cartella della copia");
            return;
        }
        // Se non si riesce (un file tenuto aperto da un altro programma) la copia resta sul disco: prima nessuno lo sapeva
        // (audit dell'11 ottobre, C-32).
        try { Directory.Delete(dir, true); }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) { Log.Error("copia locale dell'account rimosso non cancellata", ex); }
    }

    /// <summary>
    /// La copia locale è cifrata con DPAPI come i token (giro P3): oggetti, mittenti e anteprime non restano in chiaro sul
    /// disco. Quelle scritte prima, in chiaro, si leggono ancora (cominciano con "[" o "{") e alla prossima scrittura si cifrano.
    /// </summary>
    static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 0 && bytes[0] is not ((byte)'[' or (byte)'{'))
                bytes = SafeFile.Unprotect(bytes);
            return JsonSerializer.Deserialize(bytes, typeof(T), CacheJson.Default) as T;
        }
        catch (Exception ex)
        {
            Log.Error($"lettura della copia locale {Path.GetFileName(path)}", ex);
            return null;
        }
    }

    static void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFile.WriteCopy(path, SafeFile.Protect(JsonSerializer.SerializeToUtf8Bytes(value, typeof(T), CacheJson.Default)));
        }
        catch (Exception ex)
        {
            Log.Error($"scrittura della copia locale {Path.GetFileName(path)}", ex);
        }
    }
}
