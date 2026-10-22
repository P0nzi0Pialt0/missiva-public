using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace Missiva;

/// <summary>
/// La lingua di Missiva: italiano o inglese (21 ottobre 2026, richiesta dell'autore: "Ho bisogno di avere missiva anche in
/// inglese"). Si sceglie una volta all'avvio (Start, dal costruttore di App, prima di ogni finestra): l'impostazione di
/// Impostazioni, Generali, Aspetto (Settings.Language: "" come Windows, "it", "en") e, se è "come Windows", la lingua di
/// visualizzazione di Windows (italiano se è italiano, inglese altrimenti). Cambiarla vale dal prossimo avvio.
/// </summary>
/// <remarks>
/// Regole d'uso (valgono per tutto il progetto, che ha <c>global using static Missiva.Lingua</c>):
/// <list type="bullet">
/// <item>L'italiano è la lingua di partenza e la chiave, e non cambia: in italiano <c>T</c> restituisce lo stesso testo,
/// senza costo; in inglese la traduzione, letta dai file <c>Assets/lingue/en/*.json</c> (risorse incorporate).</item>
/// <item><c>T("testo italiano")</c> per un testo che va sullo schermo; con valori <c>T("Elimina {0} messaggi", n)</c>, cioè
/// string.Format con <see cref="Culture"/> (anche in italiano: it-IT, non i formati di Windows). La chiave è sempre un
/// testo normale fra virgolette: mai <c>$"..."</c>, mai <c>@"..."</c>, mai concatenato, mai dentro un'espressione
/// (<c>cond ? T("Sì") : T("No")</c>, non <c>T(cond ? "Sì" : "No")</c>). Una frase intera per chiave.</item>
/// <item>Le tabelle di testi (campi statici, elenchi) si segnano con <c>N("...")</c>, che non traduce, e si traducono dove si
/// mostrano con <c>T(valore)</c>. Mai <c>T(...)</c> in un inizializzatore statico: la lingua si sceglie dopo.</item>
/// <item>Nel XAML <c>{local:T 'Posta in arrivo'}</c> (UI.TExtension; l'apostrofo dentro si scrive <c>\'</c>).</item>
/// <item>Le traduzioni: un oggetto JSON piatto per area, <c>"testo italiano": "English text"</c>, con la chiave esattamente
/// come la stringa C# dopo gli escape e gli stessi segnaposto. Il gruppo <c>lingua</c> della suite controlla sorgenti e
/// file; <c>strumenti\lingua-mancanti.ps1</c> elenca i testi che sembrano italiani e non passano da T.</item>
/// <item>Dentro un metodo o un tipo generico con un parametro di tipo <c>T</c> si scrive <c>Lingua.T(...)</c>: lì <c>T</c> è il
/// tipo (errore CS0119 del compilatore, non un'ombra silenziosa).</item>
/// </list>
/// </remarks>
public static class Lingua
{
    static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>
    /// La lingua di visualizzazione e la cultura dei formati di Windows, prese prima che Start cambi CurrentUICulture: la
    /// classe si inizializza al più tardi alla prima chiamata, e Start è la prima.
    /// </summary>
    static readonly CultureInfo WindowsUi = CultureInfo.CurrentUICulture, WindowsFormat = CultureInfo.CurrentCulture;

    /// <summary>"it" o "en": la lingua di questa sessione. Di base "it" (anche nella suite, qualunque sia la lingua di Windows).</summary>
    public static string Code { get; private set; } = "it";

    /// <summary>Missiva è in inglese.</summary>
    public static bool English => Code == "en";

    /// <summary>
    /// La cultura per mostrare date e numeri con parole (mesi, giorni, "d MMMM yyyy"): it-IT in italiano; in inglese la
    /// cultura del formato internazionale di Windows se è inglese (en-US, en-GB...), altrimenti en-GB. Sola lettura.
    /// </summary>
    public static CultureInfo Culture { get; private set; } = Italian;

    /// <summary>"it" o "en", per l'attributo lang delle pagine HTML.</summary>
    public static string Tag => Code;

    /// <summary>
    /// La lingua degli editor di WPF (correttore ortografico) e di quello che la vuole come XmlLanguage: it-IT in italiano,
    /// come prima scritto a mano, la cultura inglese in inglese.
    /// </summary>
    public static XmlLanguage Xml => XmlLanguage.GetLanguage(Culture.Name);

    /// <summary>Le traduzioni della lingua di questa sessione: null in italiano (T non cerca nulla).</summary>
    static FrozenDictionary<string, string>? translations;

    /// <summary>La riga del registro che dice come è stata scelta la lingua (App la scrive con "Missiva ... avviata").</summary>
    public static string Described { get; private set; } = "lingua: it (Start non chiamata)";

    /// <summary>Il testo italiano nella lingua di Missiva: in italiano lo stesso testo; in inglese la traduzione, o l'italiano se manca.</summary>
    public static string T(string italiano)
    {
        var map = translations;
        if (map == null || italiano == null) return italiano!;
        if (map.TryGetValue(italiano, out var english)) return english;
        Missing(italiano);
        return italiano;
    }

    /// <summary>Come T(italiano), con i valori al posto di {0}, {1}... (string.Format con <see cref="Culture"/>).</summary>
    public static string T(string italiano, params object?[] valori) => string.Format(Culture, T(italiano), valori);

    /// <summary>Segna un testo da tradurre dove si mostra (con T(valore)) e lo restituisce com'è: per tabelle ed elenchi statici.</summary>
    public static string N(string italiano) => italiano;

#if DEBUG
    /// <summary>I testi senza traduzione già scritti nel registro: una riga per testo, la prima volta.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> missing = new(StringComparer.Ordinal);
#endif

    static void Missing(string italiano)
    {
#if DEBUG
        if (missing.TryAdd(italiano, 0)) Log.Write($"lingua: manca la traduzione in {Code} di \"{italiano}\"");
#endif
    }

    // ---------------------------------------------------------------- la scelta (funzioni pure)

    /// <summary>
    /// La lingua dall'impostazione e dalla lingua di visualizzazione di Windows: "it" ed "en" valgono come sono; "" (come
    /// Windows), null o un valore sconosciuto danno "it" se Windows è in italiano (it-IT, it-CH...), "en" altrimenti.
    /// </summary>
    internal static string Choose(string? setting, CultureInfo windows) =>
        setting is "it" or "en" ? setting : windows.TwoLetterISOLanguageName == "it" ? "it" : "en";

    /// <summary>
    /// La cultura di date e numeri per la lingua: it-IT per l'italiano; per l'inglese quella del formato internazionale di
    /// Windows se è inglese e specifica (en-US, en-GB, en-AU...), altrimenti en-GB. Sempre una cultura di sola lettura,
    /// senza le personalizzazioni di Windows (come GetCultureInfo).
    /// </summary>
    internal static CultureInfo CultureFor(string code, CultureInfo windowsFormat)
    {
        if (code != "en") return Italian;
        try
        {
            if (!windowsFormat.IsNeutralCulture && windowsFormat.TwoLetterISOLanguageName == "en" && windowsFormat.Name.Length > 0)
                return CultureInfo.GetCultureInfo(windowsFormat.Name);
        }
        catch (CultureNotFoundException)
        {
        }
        return CultureInfo.GetCultureInfo("en-GB");
    }

    /// <summary>
    /// Con questa impostazione, al prossimo avvio Missiva sarebbe in un'altra lingua (Impostazioni, Generali, Aspetto: allora
    /// dice "La lingua cambia al prossimo avvio" con Riavvia ora). Falso se l'impostazione è quella con cui è partita: con
    /// "--lingua" (Debug e PROVA) la lingua di adesso non viene dall'impostazione, e le foto non devono mostrare l'avviso.
    /// </summary>
    internal static bool ChangesAtRestart(string? setting) =>
        (setting ?? "") != (settingAtStart ?? "") && Choose(setting, WindowsUi) != Code;

    /// <summary>L'impostazione con cui Start ha scelto la lingua (null prima di Start).</summary>
    static string? settingAtStart;

    /// <summary>"--lingua it|en" fra gli argomenti: il valore, o null se manca o non è "it" né "en". Conta solo in Debug e PROVA (Start).</summary>
    internal static string? FromCommandLine(IReadOnlyList<string> args)
    {
        for (int i = 0; i + 1 < args.Count; i++)
            if (args[i] == "--lingua") return args[i + 1] is "it" or "en" ? args[i + 1] : null;
        return null;
    }

    // ---------------------------------------------------------------- avvio e prove

    /// <summary>
    /// Una volta all'avvio, dopo la lettura delle impostazioni e prima di ogni finestra (costruttore di App): sceglie la
    /// lingua, carica le traduzioni se è l'inglese e mette la cultura dei testi dell'interfaccia (CurrentUICulture, anche
    /// dei thread nuovi) su quella di Missiva, così i testi di WPF (il menu di una casella di testo) seguono la lingua.
    /// CurrentCulture (i formati di Windows) e FrameworkElement.Language restano come sono: cambierebbero i formati
    /// dell'italiano. Solo in Debug e nella versione di prova "--lingua it|en" vale più dell'impostazione (le foto in inglese).
    /// </summary>
    /// <param name="setting">Settings.Language.</param>
    /// <param name="args">Gli argomenti della riga di comando (senza il nome dell'eseguibile).</param>
    internal static void Start(string? setting, IReadOnlyList<string> args)
    {
        string? forced = null;
#if DEBUG || PROVA
        forced = FromCommandLine(args);
#endif
        var loaded = Apply(forced ?? Choose(setting, WindowsUi), WindowsFormat);
        settingAtStart = setting ?? "";
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        var why = setting is "it" or "en" ? $"impostazione {setting}" : $"impostazione \"{setting}\", come Windows";
        Described = $"lingua: {Code} ({why}; Windows {WindowsUi.Name}, formati di Windows {WindowsFormat.Name}" + (forced != null ? $"; --lingua {forced} dalla riga di comando" : "") +
                    $"), date e numeri {Culture.Name}" + (loaded is { } t ? $", {t.Texts.Count} traduzioni da {t.Files.Count} file" + Problems(t) : "");
    }

    /// <summary>
    /// Per le prove: cambia lingua e carica le traduzioni, senza toccare la cultura dei thread. La cultura di date e numeri
    /// è quella che Start sceglierebbe su questo PC (su quello dell'autore, con i formati italiani, en-GB), o quella che
    /// sceglierebbe con i formati di Windows dati (Force("en", "en-US") dà en-US). L'italiano si rimette con Force("it").
    /// </summary>
    internal static void Force(string code, string? windowsFormat = null)
    {
        if (code is not ("it" or "en")) throw new ArgumentException($"lingua sconosciuta: {code}", nameof(code));
        Apply(code, windowsFormat != null ? CultureInfo.GetCultureInfo(windowsFormat) : WindowsFormat);
    }

    /// <summary>La lingua, la sua cultura e le sue traduzioni (null in italiano); le traduzioni lette, se è l'inglese.</summary>
    static Translations? Apply(string code, CultureInfo windowsFormat)
    {
        var loaded = code == "it" ? null : Read(code);
        translations = loaded?.Texts.ToFrozenDictionary(StringComparer.Ordinal);
        Culture = CultureFor(code, windowsFormat);
        Code = code;
        return loaded;
    }

    static string Problems(Translations t) =>
        (t.Conflicts.Count > 0 ? $"; {t.Conflicts.Count} chiavi con due traduzioni (vale la prima): {string.Join("; ", t.Conflicts.Take(3))}" : "") +
        (t.Errors.Count > 0 ? $"; file non letti: {string.Join("; ", t.Errors)}" : "");

    // ---------------------------------------------------------------- i file delle traduzioni

    /// <summary>Il nome delle risorse incorporate delle traduzioni: "lingue/en/finestra.json" (LogicalName in Missiva.csproj).</summary>
    internal const string ResourcePrefix = "lingue/";

    /// <param name="Texts">Le traduzioni di tutti i file, unite (per una chiave con due traduzioni vale quella del primo file in ordine di nome).</param>
    /// <param name="Files">I file letti, in ordine di nome.</param>
    /// <param name="Conflicts">Le chiavi con traduzioni diverse in due file (o ripetute nello stesso), una riga ciascuna.</param>
    /// <param name="Errors">I file che non sono un oggetto JSON di soli testi, con il motivo: restano fuori.</param>
    internal sealed record Translations(Dictionary<string, string> Texts, List<string> Files, List<string> Conflicts, List<string> Errors);

    /// <summary>I file delle traduzioni di una lingua fra le risorse incorporate, in ordine di nome: (nome del file, contenuto).</summary>
    internal static List<(string File, byte[] Json)> Resources(string code)
    {
        var self = typeof(Lingua).Assembly;
        var prefix = ResourcePrefix + code + "/";
        var files = new List<(string, byte[])>();
        foreach (var name in self.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = self.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            files.Add((name[prefix.Length..], copy.ToArray()));
        }
        return files;
    }

    /// <summary>Le traduzioni di una lingua, lette dalle risorse incorporate.</summary>
    internal static Translations Read(string code) => Merge(Resources(code));

    /// <summary>
    /// Unisce i file delle traduzioni (funzione pura): ognuno un oggetto JSON piatto di testi, "chiave": "traduzione". Una
    /// chiave con traduzioni diverse in due file è un conflitto (vale la prima); ripetuta nello stesso file anche. Un file
    /// che non si legge resta fuori, con il motivo fra gli errori.
    /// </summary>
    internal static Translations Merge(IEnumerable<(string File, byte[] Json)> files)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new List<string>();
        var conflicts = new List<string>();
        var errors = new List<string>();
        foreach (var (file, json) in files)
        {
            names.Add(file);
            List<(string Key, string Value)> pairs;
            try { pairs = Pairs(json); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                errors.Add($"{file}: {ex.Message}");
                continue;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, value) in pairs)
            {
                if (!seen.Add(key)) conflicts.Add($"\"{key}\" ripetuta in {file}");
                else if (texts.TryGetValue(key, out var before))
                {
                    if (before != value) conflicts.Add($"\"{key}\": \"{before}\" in {from[key]}, \"{value}\" in {file}");
                }
                else
                {
                    texts[key] = value;
                    from[key] = file;
                }
            }
        }
        return new(texts, names, conflicts, errors);
    }

    /// <summary>Le coppie di un file, nell'ordine in cui sono scritte (anche quelle ripetute); un errore se non è un oggetto di soli testi.</summary>
    static List<(string, string)> Pairs(byte[] json)
    {
        ReadOnlySpan<byte> bytes = json;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..]; // UTF-8 con BOM
        var reader = new Utf8JsonReader(bytes);
        var pairs = new List<(string, string)>();
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("non è un oggetto JSON");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString() ?? "";
            if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new JsonException($"il valore di \"{key}\" non è un testo");
            pairs.Add((key, reader.GetString() ?? ""));
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) throw new JsonException("dopo l'oggetto c'è dell'altro");
        return pairs;
    }
}
