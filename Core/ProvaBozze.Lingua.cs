#if DEBUG
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Markup;

namespace Missiva;

/// <summary>
/// Gruppo "lingua" (21 ottobre 2026, Missiva anche in inglese): il meccanismo di Core/Lingua.cs con funzioni pure (la scelta
/// della lingua e della cultura, T in italiano e in inglese, i file delle traduzioni, l'estensione del XAML con
/// l'apostrofo, l'impostazione), e i sorgenti letti come testo: ogni T("..."), N("...") e {local:T '...'} ha la sua
/// traduzione con gli stessi segnaposto; nessuna forma che questo controllo non saprebbe leggere ($"...", @"...", un testo
/// concatenato o dentro un'espressione) né un T in un inizializzatore statico; nessuna chiave dei file JSON senza
/// sorgente, nessun conflitto fra file, nessuna traduzione rimasta in italiano. Nessuna finestra, nessuna rete. Da solo con
/// "Missiva.exe --prova-audit risultato.txt lingua". Alla fine la lingua torna all'italiano, come prima di ogni gruppo
/// (RestHooks): una prova che passa all'inglese con Lingua.Force("en") rimette l'italiano in un finally.
/// </summary>
static partial class ProvaBozze
{
    static void LanguageTests()
    {
        Report.Add("--- Lingua: il meccanismo dell'inglese (Core/Lingua.cs), i sorgenti e i file delle traduzioni");
        try
        {
            LinChoice();
            LinTexts();
            LinFiles();
            LinOnSta(LinXaml);
            LinSettings();
            LinScannerSelfTest();
            LinSources();
            LinShadows();
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>Gli elenchi di quello che non va, tagliati: i primi dodici.</summary>
    static string LinWrong(List<string> wrong) =>
        wrong.Count == 0 ? "" : $" [NO: {string.Join("; ", wrong.Take(12))}{(wrong.Count > 12 ? $"; e altri {wrong.Count - 12}" : "")}]";

    /// <summary>Sul thread STA, dove si creano gli elementi di WPF: quello della suite, o uno nuovo se questo non lo è.</summary>
    static void LinOnSta(Action work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            work();
            return;
        }
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new InvalidOperationException("sul thread STA", error);
    }

    // ---------------------------------------------------------------- la scelta (funzioni pure)

    /// <summary>
    /// La lingua dall'impostazione e da Windows, la cultura di date e numeri, "--lingua" dalla riga di comando, e quando le
    /// Impostazioni dicono che la lingua cambia al prossimo avvio. Senza Start (la suite non la chiama) Missiva è in italiano.
    /// </summary>
    static void LinChoice()
    {
        static CultureInfo C(string name) => name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
        Check(Lingua.Code == "it" && !Lingua.English && Lingua.Culture.Name == "it-IT",
              $"lingua, la suite parte in italiano qualunque sia la lingua di Windows (qui {CultureInfo.InstalledUICulture.Name}): Lingua.Code \"{Lingua.Code}\", cultura {Lingua.Culture.Name}");

        (string? Setting, string Windows, string Expected)[] languages =
        [
            ("", "it-IT", "it"), (null, "it-IT", "it"), ("", "it-CH", "it"), ("", "en-US", "en"), ("", "en-GB", "en"), ("", "de-DE", "en"), ("", "fr-FR", "en"),
            ("", "", "en"), ("it", "en-US", "it"), ("it", "de-DE", "it"), ("en", "it-IT", "en"), ("en", "en-US", "en"), ("fr", "it-IT", "it"), ("xx", "en-US", "en"),
            ("EN", "it-IT", "it"),
        ];
        var wrong = languages.Where(c => Lingua.Choose(c.Setting, C(c.Windows)) != c.Expected).Select(c => $"{c.Setting ?? "null"} con {c.Windows}").ToList();
        Check(wrong.Count == 0,
              $"lingua, la scelta: l'impostazione \"\" (come Windows) dà l'italiano con Windows in italiano (it-IT, it-CH) e l'inglese con ogni altra lingua; \"it\" ed \"en\" valgono " +
              $"come sono; null o un valore sconosciuto valgono come \"\" ({languages.Length} casi)" + LinWrong(wrong));

        (string Code, string Format, string Expected)[] cultures =
        [
            ("it", "it-IT", "it-IT"), ("it", "en-US", "it-IT"), ("en", "en-US", "en-US"), ("en", "en-GB", "en-GB"), ("en", "en-AU", "en-AU"), ("en", "en-IE", "en-IE"),
            ("en", "it-IT", "en-GB"), ("en", "de-DE", "en-GB"), ("en", "en", "en-GB"), ("en", "", "en-GB"),
        ];
        var bad = cultures.Where(c => Lingua.CultureFor(c.Code, C(c.Format)) is var got && (got.Name != c.Expected || !got.IsReadOnly))
                          .Select(c => $"{c.Code} con {(c.Format.Length == 0 ? "invariante" : c.Format)}: {Lingua.CultureFor(c.Code, C(c.Format)).Name}").ToList();
        Check(bad.Count == 0,
              $"lingua, la cultura di date e numeri: it-IT in italiano qualunque siano i formati di Windows; in inglese quella dei formati di Windows se è inglese e specifica " +
              $"(en-US, en-GB, en-AU, en-IE), altrimenti en-GB (formati italiani, tedeschi, \"en\" senza paese, invariante); sempre di sola lettura ({cultures.Length} casi)" + LinWrong(bad));

        (string[] Args, string? Expected)[] lines =
        [
            (["--lingua", "en"], "en"), (["--foto", "--lingua", "it", "--senza-rete"], "it"), (["--lingua"], null), (["--lingua", "fr"], null), (["--lingua", "EN"], null),
            ([], null), (["--foto"], null), (["en"], null),
        ];
        var odd = lines.Where(l => Lingua.FromCommandLine(l.Args) != l.Expected).Select(l => string.Join(" ", l.Args)).ToList();
        bool onlyTests = Ch1Code("Core", "Lingua.cs") is not { } lingua
                         || Ch3InOrder(Ch3Part(lingua, "internal static void Start(", "internal static void Force("), "#if DEBUG || PROVA", "forced = FromCommandLine(args);", "#endif",
                                       "Apply(forced ?? Choose(setting, WindowsUi), WindowsFormat);");
        Check(odd.Count == 0 && onlyTests,
              $"lingua, \"--lingua it|en\" dalla riga di comando vale più dell'impostazione, solo in Debug e nella versione di prova (Start lo legge dentro #if DEBUG || PROVA: {onlyTests}); " +
              $"senza valore, o con un valore che non è \"it\" né \"en\", non conta ({lines.Length} righe di comando)" + LinWrong(odd));

        // Impostazioni, Generali, Aspetto: l'avviso "La lingua cambia al prossimo avvio" solo se con l'impostazione nuova Missiva
        // partirebbe in un'altra lingua. Nella suite Start non c'è: l'impostazione di partenza vale "".
        bool restart;
        try
        {
            Lingua.Force("it");
            restart = !Lingua.ChangesAtRestart("") && !Lingua.ChangesAtRestart("it") && Lingua.ChangesAtRestart("en");
            Lingua.Force("en");
            restart &= Lingua.ChangesAtRestart("it") && !Lingua.ChangesAtRestart("en") && !Lingua.ChangesAtRestart("");
        }
        finally
        {
            Lingua.Force("it");
        }
        Check(restart,
              "lingua, Impostazioni: \"La lingua cambia al prossimo avvio\" compare quando l'impostazione nuova non è quella di partenza e darebbe un'altra lingua (in italiano \"en\" sì, " +
              "\"it\" no); con la lingua venuta da --lingua e l'impostazione di partenza non compare (le foto in inglese non lo mostrano)");
    }

    // ---------------------------------------------------------------- T e N

    /// <summary>
    /// T in italiano (lo stesso testo, lo stesso oggetto: nessun costo), N, i valori formattati con la cultura giusta;
    /// in inglese la traduzione, un testo che manca (l'italiano, e una riga sola nel registro), la cultura inglese, Force.
    /// </summary>
    static void LinTexts()
    {
        const string restart = "Riavvia ora";
        string format = "{0:N1} MB, {1:dddd d MMMM}", missing = "testo di prova senza traduzione " + Guid.NewGuid().ToString("N")[..8];
        var day = new DateTime(2026, 10, 21);
        try
        {
            Lingua.Force("it");
            string italian = Lingua.T(format, 1234.5, day);
            bool same = ReferenceEquals(T(restart), restart) && ReferenceEquals(Lingua.T(missing), missing) && ReferenceEquals(N(restart), restart)
                        && Lingua.Tag == "it" && Lingua.Culture.IsReadOnly && ReferenceEquals(Lingua.Xml, XmlLanguage.GetLanguage("it-IT"));
            Check(same && italian == "1.234,5 MB, mercoledì 21 ottobre",
                  $"lingua, in italiano T restituisce lo stesso testo (lo stesso oggetto, senza cercarlo), anche uno senza traduzione; N lo restituisce com'è; T con valori formatta con it-IT " +
                  $"(\"{italian}\"); Tag \"it\", Xml come il Language=\"it-IT\" di prima ({same})");

            Lingua.Force("en");
            var log = Path.Combine(Log.TestDir ?? Store.Dir, "missiva.log");
            string translated = T("Riavvia ora"), fallback = Lingua.T(missing), again = Lingua.T(missing);
            int told = File.Exists(log) ? File.ReadAllLines(log).Count(l => l.Contains(missing)) : -1;
            bool english = Lingua.Code == "en" && Lingua.English && Lingua.Tag == "en" && Lingua.Culture.TwoLetterISOLanguageName == "en" && string.Equals(Lingua.Xml.IetfLanguageTag, Lingua.Culture.Name, StringComparison.OrdinalIgnoreCase); // XmlLanguage lo scrive in minuscolo
            Check(english && translated == "Restart now" && fallback == missing && again == missing && told == 1,
                  $"lingua, in inglese T dà la traduzione (\"{translated}\"); un testo senza traduzione resta in italiano e in Debug lo dice il registro, una riga sola per testo ({told}); " +
                  $"Code \"en\", Tag \"en\", cultura {Lingua.Culture.Name}, Xml {Lingua.Xml.IetfLanguageTag}");

            string here = Lingua.T(format, 1234.5, day), hereName = Lingua.Culture.Name;
            Lingua.Force("en", "en-US");
            string us = Lingua.T(format, 1234.5, day), usName = Lingua.Culture.Name;
            Lingua.Force("en", "it-IT");
            string gbName = Lingua.Culture.Name;
            Check(here == "1,234.5 MB, Wednesday 21 October" && us == here && usName == "en-US" && gbName == "en-GB" && (hereName.StartsWith("en-") || hereName == "en"),
                  $"lingua, in inglese T con valori formatta con la cultura inglese (\"{here}\" con {hereName}, la cultura che Start sceglie su questo PC); Force(\"en\", \"en-US\") dà en-US, " +
                  $"Force(\"en\", \"it-IT\") en-GB come con i formati italiani di Windows");

            Lingua.Force("it");
            bool back = Lingua.Code == "it" && ReferenceEquals(T(restart), restart) && Lingua.Culture.Name == "it-IT";
            bool refused;
            try
            {
                Lingua.Force("fr");
                refused = false;
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            Check(back && refused && Lingua.Code == "it",
                  $"lingua, Force(\"it\") rimette l'italiano (T di nuovo senza traduzioni, cultura it-IT: {back}); Force con una lingua che non c'è è un errore e non cambia nulla ({refused})");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    // ---------------------------------------------------------------- i file delle traduzioni

    /// <summary>I file delle aree e quello della lingua: incorporati nell'eseguibile con il nome lingue/en/&lt;file&gt;.json.</summary>
    static readonly string[] LinFileNames = ["agenda.json", "finestra.json", "impostazioni.json", "lettura.json", "lingua.json", "servizio.json"];

    /// <summary>I file incorporati, letti come all'avvio in inglese, e l'unione di file costruiti apposta: conflitti, chiavi ripetute, file sbagliati.</summary>
    static void LinFiles()
    {
        var names = Lingua.Resources("en").Select(f => f.File).ToList();
        var read = Lingua.Read("en");
        Check(LinFileNames.All(names.Contains) && read.Errors.Count == 0 && read.Conflicts.Count == 0 && read.Texts.Count > 0 && read.Files.SequenceEqual(names)
              && Lingua.Resources("it").Count == 0,
              $"lingua, i file delle traduzioni sono risorse dell'eseguibile (lingue/en/...: {string.Join(", ", names)}), letti in ordine di nome e uniti: {read.Texts.Count} traduzioni, " +
              $"nessun file illeggibile né conflitto; per l'italiano nessun file" + LinWrong([.. read.Errors, .. read.Conflicts]));

        static (string, byte[]) F(string name, string json, bool bom = false) =>
            (name, bom ? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)] : Encoding.UTF8.GetBytes(json));
        var merged = Lingua.Merge(
        [
            F("a.json", "{\"Uno\": \"One\", \"Due\": \"Two\", \"Uno\": \"One\"}"), F("b.json", "{\"Due\": \"Deux\", \"Tre\": \"Three\", \"Uno\": \"One\"}"),
            F("c.json", "[\"Uno\"]"), F("d.json", "{\"Cinque\": 5}"), F("e.json", "{\"Quattro\": \"Four\"}", bom: true), F("f.json", "{}"), F("g.json", "{} {}"),
            F("h.json", "{\"Sei\": {\"x\": \"y\"}}"), F("i.json", "{\"Sette\": \"Seven\""),
        ]);
        bool texts = merged.Texts.Count == 4 && merged.Texts["Uno"] == "One" && merged.Texts["Due"] == "Two" && merged.Texts["Tre"] == "Three" && merged.Texts["Quattro"] == "Four";
        bool conflicts = merged.Conflicts.Count == 2 && merged.Conflicts.Any(c => c.Contains("\"Uno\" ripetuta in a.json"))
                         && merged.Conflicts.Any(c => c.Contains("\"Due\"") && c.Contains("a.json") && c.Contains("b.json"));
        var refused = merged.Errors.Select(e => e[..e.IndexOf(':')]).ToList();
        Check(texts && conflicts && refused.SequenceEqual(["c.json", "d.json", "g.json", "h.json", "i.json"]) && merged.Files.Count == 9,
              $"lingua, l'unione dei file: la stessa chiave con due traduzioni in due file è un conflitto (vale la prima), ripetuta nello stesso file anche, uguale in due file no; " +
              $"un file che non è un oggetto di soli testi (un elenco, un numero, un oggetto dentro, due oggetti, uno troncato) resta fuori con il motivo; il BOM non disturba " +
              $"({merged.Texts.Count} traduzioni, conflitti: {string.Join("; ", merged.Conflicts)}; scartati: {string.Join(", ", refused)})");
    }

    // ---------------------------------------------------------------- l'estensione del XAML

    /// <summary>
    /// {l:T '...'} letto da XamlReader come lo legge una finestra: l'apostrofo scritto \' arriva come apostrofo, le
    /// virgolette &amp;quot; come virgolette, la barra rovesciata \\ come una barra; in inglese la traduzione, o l'italiano se manca.
    /// </summary>
    static void LinXaml()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:l="clr-namespace:Missiva.UI;assembly=Missiva">
              <TextBlock Text="{l:T 'Mostra l\'anteprima, poi chiudi'}" />
              <Button Content="{l:T 'Riavvia ora'}" />
              <TextBlock Text="{l:T 'Il pulsante &quot;Invia&quot; e la barra \\ rovesciata'}" />
            </StackPanel>
            """;
        static List<string?> Texts(string xaml) =>
            ((System.Windows.Controls.StackPanel)XamlReader.Parse(xaml)).Children.Cast<object>()
                .Select(c => c switch { System.Windows.Controls.TextBlock t => t.Text, System.Windows.Controls.ContentControl b => b.Content as string, _ => null }).ToList();
        List<string?> italian, english;
        string? direct;
        try
        {
            Lingua.Force("it");
            italian = Texts(xaml);
            Lingua.Force("en");
            english = Texts(xaml);
            direct = new UI.TExtension("Riavvia ora").ProvideValue(null!) as string;
        }
        finally
        {
            Lingua.Force("it");
        }
        string?[] it = ["Mostra l'anteprima, poi chiudi", "Riavvia ora", "Il pulsante \"Invia\" e la barra \\ rovesciata"];
        string?[] en = ["Mostra l'anteprima, poi chiudi", "Restart now", "Il pulsante \"Invia\" e la barra \\ rovesciata"];
        Check(italian.SequenceEqual(it) && english.SequenceEqual(en) && direct == "Restart now" && typeof(UI.TExtension).IsPublic,
              $"lingua, nel XAML {{local:T '...'}} (UI.TExtension, letta da XamlReader): l'apostrofo scritto \\' arriva come apostrofo (\"{italian.FirstOrDefault()}\"), &quot; come virgolette, " +
              $"\\\\ come una barra; in inglese la traduzione (\"{english.ElementAtOrDefault(1)}\") o, se manca, l'italiano [{string.Join(" | ", english)}]");
    }

    // ---------------------------------------------------------------- l'impostazione, l'avvio, gli editor

    /// <summary>
    /// Settings.Language nel JSON delle impostazioni; dai sorgenti: Start nel costruttore di App dopo la lettura delle
    /// impostazioni e prima del tema e delle finestre, la riga del registro, la tendina di Impostazioni, Generali, Aspetto
    /// con le sue traduzioni, e gli editor e WebView2 che seguono la lingua (in italiano lo stesso valore di prima).
    /// </summary>
    static void LinSettings()
    {
        var fresh = new Settings();
        var json = System.Text.Json.JsonSerializer.Serialize(fresh, SettingsJson.Default.Settings);
        var english = System.Text.Json.JsonSerializer.Deserialize("{\"Language\":\"en\"}", SettingsJson.Default.Settings)!;
        var old = System.Text.Json.JsonSerializer.Deserialize("{\"Theme\":\"Dark\"}", SettingsJson.Default.Settings)!;
        Check(fresh.Language == "" && json.Contains("\"Language\": \"\"") && english.Language == "en" && old.Language == "" && old.Theme == AppTheme.Dark,
              "lingua, Settings.Language: \"\" di base (come Windows), scritta e letta nel JSON delle impostazioni; quelle scritte prima, senza, valgono \"\"");

        string words;
        try
        {
            Lingua.Force("en");
            words = string.Join(" | ", T("Lingua (Language)"), T("Come Windows"), T("La lingua cambia al prossimo avvio di Missiva."), T("Riavvia ora"));
        }
        finally
        {
            Lingua.Force("it");
        }
        if (Ch1Code("UI", "App.cs") is not { } app || Ch1Code("UI", "SettingsWindow.cs") is not { } settings || BehSource("UI", "ComposeWindow.xaml") is not { } compose
            || Ch1Code("UI", "ReadingView.xaml.cs") is not { } reading)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: l'avvio, la tendina e gli editor non sono stati letti)");
            return;
        }
        string ctor = Ch3Part(app, "public App()", "public static new App Current");
        bool start = Ch3InOrder(ctor, "loading.GetAwaiter().GetResult()", "Log.Startup(\"impostazioni lette\");",
                                "Lingua.Start(Store.Settings.Language, Program.Arguments(Environment.GetCommandLineArgs()[1..]));", "MicrosoftAuth.WarmUp(", "ThemeMode = ModeOf(")
                     && app.Split("Lingua.Start(").Length == 2
                     && Ch3InOrder(Ch3Part(app, "protected override void OnStartup(", "bool toastsHeard;"), "Log.Write($\"Missiva {AppVersion.Text} avviata\");", "Log.Write(Lingua.Described);");
        Check(start,
              "lingua, l'avvio: Lingua.Start nel costruttore di App, dopo la lettura delle impostazioni e prima di account, tema e finestre, con i soli argomenti che Program.Arguments " +
              "lascia (da un link mailto: solo il link); la riga \"lingua: ...\" nel registro accanto a \"Missiva ... avviata\"");

        string page = Ch3Part(settings, "StackPanel AppearancePage()", "\n    }"), part = Ch3Part(settings, "IEnumerable<UIElement> LanguagePart()", "\n    }");
        bool dropdown = Ch3InOrder(page, "Heading(", "foreach (var part in LanguagePart()) p.Children.Add(part);")
                        && Ch3InOrder(part, "string[] values = [\"\", \"it\", \"en\"];", "T(\"Lingua (Language)\")", "ItemsSource = new[] { T(\"Come Windows\"), \"Italiano\", \"English\" }",
                                      "AutomationProperties.SetName(box, title);", "T(\"La lingua cambia al prossimo avvio di Missiva.\")", "T(\"Riavvia ora\")", "App.Current.RestartBecause(",
                                      "Lingua.ChangesAtRestart(S.Language)", "S.Language = values[box.SelectedIndex];", "Store.Save();", "yield return Section(title);");
        Check(dropdown && words == "Language (Lingua) | Same as Windows | The new language applies the next time Missiva starts. | Restart now",
              $"lingua, Impostazioni, Generali, Aspetto: in cima la tendina \"Lingua (Language)\" con Come Windows, Italiano, English (questi due sempre così), salvata subito in " +
              $"Settings.Language; sotto, se la lingua cambierà, l'avviso con Riavvia ora (App.RestartBecause) ({dropdown}); in inglese: {words}");

        bool editors = compose.Contains("Language=\"{x:Static missiva:Lingua.Xml}\"") && compose.Contains("xmlns:missiva=\"clr-namespace:Missiva\"") && !compose.Contains("Language=\"it-IT\"")
                       && settings.Split("Language = Lingua.Xml,").Length == 3 && !settings.Contains("XmlLanguage.GetLanguage(\"it-IT\")")
                       && reading.Contains("Language = Lingua.Culture.Name,") && !reading.Contains("Language = \"it-IT\"");
        bool same = ReferenceEquals(Lingua.Xml, XmlLanguage.GetLanguage("it-IT")) && Lingua.Culture.Name == "it-IT";
        Check(editors && same,
              $"lingua, gli editor (scrittura, firme, modelli) e WebView2 del riquadro seguono la lingua di Missiva invece di \"it-IT\" scritto a mano ({editors}); in italiano il valore è " +
              $"quello di prima ({same})");
    }

    // ---------------------------------------------------------------- i sorgenti: lettura

    /// <summary>
    /// Un testo trovato nei sorgenti: file e riga, chi lo usa (T, N, XAML), la chiave (null se non si legge), se T ha anche
    /// dei valori, e che cosa non va ("forma": $"...", @"...", un testo grezzo, nel XAML non fra apici; "espressione": un
    /// testo concatenato o dentro un'espressione; "statico": T in un inizializzatore statico), o null.
    /// </summary>
    sealed record LinUse(string File, int Line, string Call, string? Key, bool Values, string? Problem);

    /// <summary>Per ogni carattere di un sorgente C#: 0 codice, 1 commento, 2 testo fra virgolette o carattere fra apici (con i prefissi $ e @).</summary>
    static byte[] LinKinds(string s)
    {
        var kind = new byte[s.Length];
        int i = 0;
        LinCode(s, kind, ref i, hole: false);
        return kind;
    }

    /// <summary>Codice da i fino alla fine o, nel buco di un testo interpolato, fino alla graffa che lo chiude (i si ferma lì).</summary>
    static void LinCode(string s, byte[] kind, ref int i, bool hole)
    {
        int braces = 0, parens = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') kind[i++] = 1;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                while (i < end) kind[i++] = 1;
                continue;
            }
            if (c is '"' or '\'' || c is '$' or '@' && LinPrefixed(s, i))
            {
                LinLiteral(s, kind, ref i);
                continue;
            }
            if (hole)
            {
                if (c is '(' or '[') parens++;
                else if (c is ')' or ']') parens--;
                else if (c == '{') braces++;
                else if (c == '}')
                {
                    if (braces == 0) return;
                    braces--;
                }
                else if (c == ':' && braces == 0 && parens <= 0 && !(i + 1 < s.Length && s[i + 1] == ':') && !(i > 0 && s[i - 1] == ':'))
                {
                    // il formato del buco ({n:N0}): testo fino alla graffa
                    while (i < s.Length && s[i] != '}') kind[i++] = 2;
                    return;
                }
            }
            i++;
        }
    }

    /// <summary>Da i, solo $ e @ (al più una @) e poi le virgolette: l'inizio di un testo interpolato o verbatim.</summary>
    static bool LinPrefixed(string s, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) return false;
        int ats = 0;
        while (i < s.Length && s[i] is '$' or '@')
        {
            if (s[i] == '@' && ++ats > 1) return false;
            i++;
        }
        return i < s.Length && s[i] == '"';
    }

    /// <summary>Un testo fra virgolette (normale, verbatim, interpolato, grezzo) o un carattere fra apici, da i: lo segna e porta i dopo la fine.</summary>
    static void LinLiteral(string s, byte[] kind, ref int i)
    {
        int dollars = 0;
        bool verbatim = false;
        while (i < s.Length && s[i] is '$' or '@')
        {
            if (s[i] == '$') dollars++;
            else verbatim = true;
            kind[i++] = 2;
        }
        if (s[i] == '\'')
        {
            kind[i++] = 2;
            while (i < s.Length && s[i] != '\'' && s[i] != '\n')
            {
                if (s[i] == '\\' && i + 1 < s.Length) kind[i++] = 2;
                kind[i++] = 2;
            }
            if (i < s.Length && s[i] == '\'') kind[i++] = 2;
            return;
        }
        int quotes = 0;
        while (i + quotes < s.Length && s[i + quotes] == '"') quotes++;
        if (!verbatim && quotes >= 3)
        {
            // grezzo: finisce con tante virgolette quante quelle d'apertura; con n $ i buchi si aprono con n graffe
            for (int k = 0; k < quotes; k++) kind[i++] = 2;
            while (i < s.Length)
            {
                if (s[i] == '"')
                {
                    int run = 0;
                    while (i + run < s.Length && s[i + run] == '"') run++;
                    for (int k = 0; k < run; k++) kind[i++] = 2;
                    if (run >= quotes) return;
                    continue;
                }
                if (dollars > 0 && s[i] == '{')
                {
                    int run = 0;
                    while (i + run < s.Length && s[i + run] == '{') run++;
                    if (run >= dollars)
                    {
                        for (int k = 0; k < run; k++) kind[i++] = 2;
                        LinCode(s, kind, ref i, hole: true);
                        while (i < s.Length && s[i] == '}') kind[i++] = 2;
                        continue;
                    }
                    for (int k = 0; k < run; k++) kind[i++] = 2;
                    continue;
                }
                kind[i++] = 2;
            }
            return;
        }
        kind[i++] = 2; // le virgolette d'apertura
        while (i < s.Length)
        {
            char c = s[i];
            if (verbatim && c == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"')
                {
                    kind[i++] = 2;
                    kind[i++] = 2;
                    continue;
                }
                kind[i++] = 2;
                return;
            }
            if (!verbatim)
            {
                if (c == '\\' && i + 1 < s.Length)
                {
                    kind[i++] = 2;
                    kind[i++] = 2;
                    continue;
                }
                if (c == '"')
                {
                    kind[i++] = 2;
                    return;
                }
                if (c == '\n') return; // non chiuso: si ferma alla riga
            }
            if (dollars > 0 && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{')
                {
                    kind[i++] = 2;
                    kind[i++] = 2;
                    continue;
                }
                kind[i++] = 2;
                LinCode(s, kind, ref i, hole: true);
                if (i < s.Length) kind[i++] = 2; // la graffa che chiude il buco
                continue;
            }
            kind[i++] = 2;
        }
    }

    /// <summary>Le parti di codice da tenere per i blocchi: le righe del preprocessore e i rami #else ed #elif diventano commento.</summary>
    static byte[] LinActive(string s, byte[] kind)
    {
        var active = (byte[])kind.Clone();
        var inactive = new Stack<bool>(); // per ogni #if aperto: se da qui il ramo è spento
        int start = 0;
        while (start < s.Length)
        {
            int end = s.IndexOf('\n', start);
            if (end < 0) end = s.Length;
            int first = start;
            while (first < end && s[first] is ' ' or '\t') first++;
            bool directive = first < end && s[first] == '#' && kind[first] == 0;
            if (directive)
            {
                var word = Regex.Match(s[(first + 1)..end], @"^\s*(\w+)").Groups[1].Value;
                if (word == "if") inactive.Push(false);
                else if (word is "else" or "elif" && inactive.Count > 0)
                {
                    inactive.Pop();
                    inactive.Push(true);
                }
                else if (word == "endif" && inactive.Count > 0) inactive.Pop();
            }
            if (directive || inactive.Contains(true))
                for (int k = start; k < end; k++) active[k] = 1;
            start = end + 1;
        }
        return active;
    }

    /// <summary>La posizione del carattere di codice dopo i (saltando spazi e commenti), o la fine.</summary>
    static int LinSkip(string s, byte[] kind, int i)
    {
        while (i < s.Length && (kind[i] == 1 || kind[i] == 0 && char.IsWhiteSpace(s[i]))) i++;
        return i;
    }

    /// <summary>La posizione del carattere di codice prima di i (saltando spazi e commenti), o -1.</summary>
    static int LinBack(string s, byte[] kind, int i)
    {
        while (i >= 0 && (kind[i] == 1 || kind[i] == 0 && char.IsWhiteSpace(s[i]))) i--;
        return i;
    }

    static bool LinWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>La parola che finisce in i (compreso), o "".</summary>
    static string LinWordBefore(string s, int i)
    {
        int from = i;
        while (from >= 0 && LinWordChar(s[from])) from--;
        return s[(from + 1)..(i + 1)];
    }

    /// <summary>Le parole dopo cui T( è una chiamata e non la dichiarazione di un metodo che si chiama così.</summary>
    static readonly HashSet<string> LinCallAfter = ["return", "await", "throw", "case", "in", "else", "yield", "is", "and", "or", "not", "when", "select", "where", "let", "do", "out", "ref"];

    /// <summary>Il contenuto di un testo C# normale, con gli escape tradotti.</summary>
    static string LinUnescape(string body)
    {
        var text = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c != '\\' || i + 1 >= body.Length)
            {
                text.Append(c);
                continue;
            }
            char e = body[++i];
            switch (e)
            {
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case '0': text.Append('\0'); break;
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'v': text.Append('\v'); break;
                case 'e': text.Append('\u001b'); break;
                case 'u' when i + 4 < body.Length: text.Append((char)Convert.ToInt32(body.Substring(i + 1, 4), 16)); i += 4; break;
                case 'U' when i + 8 < body.Length: text.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 1, 8), 16))); i += 8; break;
                case 'x':
                    int n = 0;
                    while (n < 4 && i + 1 + n < body.Length && Uri.IsHexDigit(body[i + 1 + n])) n++;
                    if (n > 0) text.Append((char)Convert.ToInt32(body.Substring(i + 1, n), 16));
                    i += n;
                    break;
                default: text.Append(e); break; // \\ \" \'
            }
        }
        return text.ToString();
    }

    /// <summary>I testi di un sorgente C#: le chiamate T(...), Lingua.T(...) e N(...) fuori da commenti e testi.</summary>
    static List<LinUse> LinScanCs(string file, string s)
    {
        var kind = LinKinds(s);
        var calls = new List<(int At, string Name, int Arg, int End, bool More)>();
        for (int p = 0; p < s.Length; p++)
        {
            if (kind[p] != 0 || s[p] is not ('T' or 'N')) continue;
            if (p > 0 && (LinWordChar(s[p - 1]) || s[p - 1] == '@') || p + 1 < s.Length && LinWordChar(s[p + 1])) continue;
            int open = LinSkip(s, kind, p + 1);
            if (open >= s.Length || s[open] != '(' || kind[open] != 0) continue;
            int b = LinBack(s, kind, p - 1);
            if (b >= 0 && s[b] == '.')
            {
                // Lingua.T, Missiva.Lingua.T: sì; un altro oggetto.T no
                if (LinWordBefore(s, LinBack(s, kind, b - 1)) != "Lingua") continue;
            }
            else if (b >= 0)
            {
                char before = s[b];
                if (LinWordChar(before))
                {
                    var word = LinWordBefore(s, b);
                    if (!LinCallAfter.Contains(word)) continue; // string T(...), new T(...): dichiarazione o costruttore
                }
                else if (before == '>' && !(b > 0 && s[b - 1] == '=')) continue; // List<string> T(...)
                else if (before == ']') continue; // string[] T(...)
                else if (before == '?' && b > 0 && (LinWordChar(s[b - 1]) || s[b - 1] is '>' or ']')) continue; // string? T(...)
            }
            // il primo argomento: fino alla virgola o alla parentesi chiusa allo stesso livello
            int depth = 0, end = -1;
            bool more = false;
            for (int k = open + 1; k < s.Length; k++)
            {
                if (kind[k] != 0) continue;
                char ch = s[k];
                if (ch is '(' or '[' or '{') depth++;
                else if (ch is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        end = k;
                        break;
                    }
                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    end = k;
                    more = true;
                    break;
                }
            }
            if (end < 0) continue;
            calls.Add((p, s[p].ToString(), open + 1, end, more));
        }
        var statics = LinStatic(s, LinActive(s, kind), calls.Where(c => c.Name == "T").Select(c => c.At).ToList());
        var lineStarts = new List<int> { 0 };
        for (int k = 0; k < s.Length; k++)
            if (s[k] == '\n') lineStarts.Add(k + 1);
        int Line(int at)
        {
            int index = lineStarts.BinarySearch(at);
            return (index >= 0 ? index : ~index - 1) + 1;
        }
        var uses = new List<LinUse>();
        foreach (var (at, name, arg, end, more) in calls)
        {
            int a = LinSkip(s, kind, arg);
            if (a >= end) continue; // T()
            string? key = null, problem = null;
            if (kind[a] == 2)
            {
                if (s[a] is '$' or '@' || s[a] == '"' && a + 2 < s.Length && s[a + 1] == '"' && s[a + 2] == '"') problem = "forma";
                else if (s[a] == '"')
                {
                    int close = a + 1;
                    while (close < s.Length && kind[close] == 2) close++;
                    if (LinSkip(s, kind, close) != end) problem = "espressione";
                    else key = LinUnescape(s[(a + 1)..(close - 1)]);
                }
                else problem = "espressione";
            }
            else
            {
                bool literal = false;
                for (int k = a; k < end && !literal; k++) literal = kind[k] == 2;
                if (!literal) continue; // T(valore): una chiave segnata altrove con N
                problem = "espressione";
            }
            if (problem == null && statics.Contains(at)) problem = "statico";
            uses.Add(new(file, Line(at), name, key, more, problem));
        }
        return uses;
    }

    enum LinBlock { File, Namespace, Type, Member, StaticInit, Init, Other }

    /// <summary>
    /// Il primo "=" di un'assegnazione fuori da parentesi tonde e quadre (non "==", "=>", "!=", "&lt;=", "+="...), o -1: in
    /// un campo è l'inizio dell'inizializzatore; quelli dei valori di base dei parametri e degli attributi stanno fra parentesi.
    /// </summary>
    static int LinAssigns(string h)
    {
        int depth = 0;
        for (int k = 0; k < h.Length; k++)
        {
            char c = h[k];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == '=' && depth == 0 && !(k > 0 && "=!<>+-*/%&|^?".Contains(h[k - 1])) && !(k + 1 < h.Length && h[k + 1] is '=' or '>')) return k;
        }
        return -1;
    }

    /// <summary>
    /// Fra le chiamate a T (posizioni), quelle in un inizializzatore statico: di un campo o di una proprietà statica (anche
    /// dentro le graffe di un inizializzatore), o in un costruttore statico. Non dentro una funzione anonima (=>), che gira
    /// dopo. Si seguono le graffe del codice, con i rami #else del preprocessore spenti (active).
    /// </summary>
    static HashSet<int> LinStatic(string s, byte[] active, List<int> calls)
    {
        var found = new HashSet<int>();
        if (calls.Count == 0) return found;
        var mask = new char[s.Length];
        for (int k = 0; k < s.Length; k++) mask[k] = active[k] == 0 ? s[k] : s[k] == '\n' ? '\n' : ' ';
        var stack = new List<(LinBlock Block, int Start, bool Static)> { (LinBlock.File, 0, false) };
        bool pendingStatic = false; // dopo "static int P { get; }": "= T(...)" è l'inizializzatore di una proprietà statica
        calls.Sort();
        int next = 0;
        for (int i = 0; i < s.Length && next < calls.Count; i++)
        {
            while (next < calls.Count && calls[next] == i)
            {
                if (active[i] == 0 && LinIsStatic(mask, stack[^1], i, pendingStatic)) found.Add(i);
                next++;
            }
            if (active[i] != 0) continue;
            char c = s[i];
            if (c == '{')
            {
                var top = stack[^1];
                string header = new(mask, top.Start, i - top.Start);
                LinBlock block;
                bool isStatic = Regex.IsMatch(header, @"\bstatic\b");
                if (top.Block is LinBlock.File or LinBlock.Namespace or LinBlock.Type)
                {
                    int eq = LinAssigns(header);
                    if (Regex.IsMatch(header, @"(?<![:,]\s*)\b(class|struct|record|interface|enum)\b")) block = LinBlock.Type;
                    else if (Regex.IsMatch(header, @"\bnamespace\b")) block = LinBlock.Namespace;
                    else if (top.Block == LinBlock.Type && eq >= 0 && !header[eq..].Contains("=>"))
                        block = Regex.IsMatch(header[..eq], @"\b(static|const)\b") ? LinBlock.StaticInit : LinBlock.Init;
                    else if (top.Block == LinBlock.Type && Regex.IsMatch(header, @"\bstatic\s+[A-Za-z_]\w*\s*\(\s*\)\s*$")) block = LinBlock.StaticInit;
                    else block = LinBlock.Member;
                }
                else if (top.Block == LinBlock.StaticInit) block = header.Contains("=>") ? LinBlock.Member : LinBlock.StaticInit;
                else block = LinBlock.Other;
                stack.Add((block, i + 1, isStatic));
                pendingStatic = false;
            }
            else if (c == '}')
            {
                if (stack.Count > 1)
                {
                    var closed = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    pendingStatic = closed.Block == LinBlock.Member && closed.Static && stack[^1].Block == LinBlock.Type;
                    stack[^1] = stack[^1] with { Start = i + 1 };
                }
            }
            else if (c == ';')
            {
                stack[^1] = stack[^1] with { Start = i + 1 };
                pendingStatic = false;
            }
        }
        return found;
    }

    static bool LinIsStatic(char[] mask, (LinBlock Block, int Start, bool Static) top, int p, bool pendingStatic)
    {
        var h = new string(mask, top.Start, p - top.Start);
        if (top.Block == LinBlock.StaticInit) return !h.Contains("=>");
        if (top.Block != LinBlock.Type) return false;
        int eq = LinAssigns(h);
        if (eq < 0 || h[eq..].Contains("=>")) return false;
        return pendingStatic && h[..eq].Trim().Length == 0 || Regex.IsMatch(h[..eq], @"\b(static|const)\b");
    }

    /// <summary>
    /// I testi di un XAML: gli attributi con {prefisso:T '...'}, dove il prefisso è quello di clr-namespace:Missiva.UI;
    /// la chiave è il testo dopo le entità (&amp;quot;...) e gli escape del XAML (\'). Fuori dai commenti.
    /// </summary>
    static List<LinUse> LinScanXaml(string file, string s)
    {
        var uses = new List<LinUse>();
        var text = Regex.Replace(s, "<!--.*?-->", m => Regex.Replace(m.Value, "[^\n]", " "), RegexOptions.Singleline);
        var prefixes = Regex.Matches(text, "xmlns:(\\w+)\\s*=\\s*\"clr-namespace:Missiva\\.UI(;assembly=Missiva)?\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        foreach (Match attribute in Regex.Matches(text, "([\\w.:]+)\\s*=\\s*\"([^\"]*)\""))
        {
            var value = WebUtility.HtmlDecode(attribute.Groups[2].Value);
            int line = 1 + text.AsSpan(0, attribute.Index).Count('\n');
            foreach (var prefix in prefixes)
            {
                int at = 0;
                while ((at = value.IndexOf("{" + prefix + ":T", at, StringComparison.Ordinal)) >= 0)
                {
                    int i = at + prefix.Length + 3;
                    at = i;
                    if (i < value.Length && LinWordChar(value[i])) continue; // {local:Testo ...}: un'altra estensione
                    int j = i;
                    while (j < value.Length && char.IsWhiteSpace(value[j])) j++;
                    var key = new StringBuilder();
                    bool closed = false;
                    if (j > i && j < value.Length && value[j] == '\'')
                    {
                        for (j++; j < value.Length; j++)
                        {
                            if (value[j] == '\\' && j + 1 < value.Length) key.Append(value[++j]);
                            else if (value[j] == '\'')
                            {
                                closed = true;
                                j++;
                                break;
                            }
                            else key.Append(value[j]);
                        }
                        while (j < value.Length && char.IsWhiteSpace(value[j])) j++;
                    }
                    bool good = closed && j < value.Length && value[j] == '}';
                    uses.Add(new(file, line, "XAML", good ? key.ToString() : null, false, good ? null : "forma"));
                }
            }
        }
        return uses;
    }

    // ---------------------------------------------------------------- i sorgenti: il lettore alla prova

    /// <summary>Il lettore dei sorgenti su un C# e un XAML scritti apposta: trova quello che deve e solo quello, con le righe giuste.</summary>
    static void LinScannerSelfTest()
    {
        const string source = """
            namespace Prova;
            static class Esempio
            {
                static readonly string Fissa = T("Fissa");
                static readonly string[] Tabella = [N("Uno"), N("Due")];
                static readonly Dictionary<string, string> Mappa = new() { ["a"] = T("Mappa") };
                static readonly Func<string> Dopo = () => T("Dopo");
                static string Proprieta => T("Proprietà");
                public static string Iniziale { get; } = T("Iniziale");
                static Esempio() { Altra = T("Costruttore"); }
                string campo = T("Campo");
                void Metodo(int n, string x, bool b = false)
                {
                    var a = T("Semplice");
                    var v = Lingua.T("Elimina {0} messaggi", n);
                    var w = Missiva.Lingua.T("Con \"virgolette\" e \\ barra\n");
                    var i = T($"Interpolato {n}");
                    var j = T(@"Verbatim");
                    var k = T("Pezzo" + x);
                    var l = T(b ? "Sì" : "No");
                    var m = T(x);
                    var o = $"{T("Nel buco")} {n:N0}";
                    // T("Commento")
                    var p = "T(\"Nel testo\")";
                    var q = Altro.T("Altro");
                    var r = T(
                        "Su due righe");
                    /* T("Commento lungo") */
                    var s = b ? T("Ramo") : T("Altro ramo");
                }
            #if DEBUG
                static readonly string Prova = T("Ramo acceso");
            #else
                static readonly string Prova = T("Ramo spento");
            #endif
                static string T(string s) => s;
                string? N(string s) => s;
                static List<string> T2() => [];
            }
            """;
        var found = LinScanCs("Esempio.cs", source.ReplaceLineEndings("\n")).Select(u => $"{u.Line} {u.Call} {u.Key ?? "-"}{(u.Values ? " +valori" : "")}{(u.Problem is { } p ? " " + p : "")}").ToList();
        string[] expected =
        [
            "4 T Fissa statico", "5 N Uno", "5 N Due", "6 T Mappa statico", "7 T Dopo", "8 T Proprietà", "9 T Iniziale statico", "10 T Costruttore statico", "11 T Campo",
            "14 T Semplice", "15 T Elimina {0} messaggi +valori", "16 T Con \"virgolette\" e \\ barra\n", "17 T - forma", "18 T - forma", "19 T - espressione", "20 T - espressione",
            "22 T Nel buco", "26 T Su due righe", "29 T Ramo", "29 T Altro ramo", "32 T Ramo acceso statico", "34 T Ramo spento",
        ];
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();
        Check(missing.Count == 0 && extra.Count == 0,
              $"lingua, il lettore dei sorgenti C# su un esempio: le chiavi di T, Lingua.T e N (con gli escape, anche nel buco di un testo interpolato e su due righe), T con valori; " +
              $"T($\"...\") e T(@\"...\") sono una forma che non si legge, un testo concatenato o in un'espressione pure; T in un campo statico, nelle graffe del suo inizializzatore, " +
              $"in una proprietà statica e nel costruttore statico sì, in una funzione anonima e in un campo d'istanza no; niente da commenti, testi, altri oggetti e dichiarazioni " +
              $"({found.Count} trovati)" + LinWrong([.. missing.Select(m => "manca " + m), .. extra.Select(e => "in più " + e)]));

        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Missiva.UI">
              <!-- <Button Content="{local:T 'Commento'}" /> -->
              <Button Content="{local:T 'Posta in arrivo'}" ToolTip="{local:T 'Mostra l\'anteprima'}" />
              <TextBlock Text="{local:T 'Il pulsante &quot;Invia&quot; &amp; altro'}" />
              <TextBlock Text="{local:T Senza apici}" />
              <TextBlock Text="{local:T Text='Con il nome'}" />
              <TextBlock Text="{Binding Name}" Tag="{local:Testo 'Altra estensione'}" />
            </Window>
            """;
        var inXaml = LinScanXaml("Esempio.xaml", xaml.ReplaceLineEndings("\n")).Select(u => $"{u.Line} {u.Key ?? "-"}{(u.Problem is { } p ? " " + p : "")}").ToList();
        string[] expectedXaml = ["3 Posta in arrivo", "3 Mostra l'anteprima", "4 Il pulsante \"Invia\" & altro", "5 - forma", "6 - forma"];
        Check(inXaml.SequenceEqual(expectedXaml),
              $"lingua, il lettore dei XAML su un esempio: {{local:T '...'}} con l'apostrofo \\' e le entità (&quot; &amp;), con il prefisso di clr-namespace:Missiva.UI; senza apici o con " +
              $"Text= è una forma che non si legge; niente dai commenti e dalle altre estensioni [{string.Join(" | ", inXaml)}]");
    }

    // ---------------------------------------------------------------- i sorgenti del progetto e i file delle traduzioni

    /// <summary>I .cs o .xaml del progetto, fuori da bin, obj e dalle cartelle nascoste.</summary>
    static IEnumerable<string> LinSourceFiles(string root, string extension) =>
        Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories)
            .Where(p => string.Equals(Path.GetExtension(p), extension, StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar).SkipLast(1).Any(d => d is "bin" or "obj" || d.StartsWith('.')))
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>I segnaposto di string.Format di un testo ({0}, {1:N0}, {2,-8}), senza ripetizioni, in ordine.</summary>
    static List<string> LinHoles(string text) =>
        Regex.Matches(text.Replace("{{", "").Replace("}}", ""), @"\{\d+(,\s*-?\d+)?(:[^{}]*)?\}").Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>Un valore che accetta qualunque formato: string.Format sbaglia solo per le graffe, non per il tipo.</summary>
    sealed class LinAny : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => "x";
        public override string ToString() => "x";
    }

    static bool LinFormats(string text)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, Enumerable.Range(0, 10).Select(_ => (object)new LinAny()).ToArray());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Un testo che sembra italiano: lettere accentate o una parola italiana comune (la stessa regola di
    /// strumenti\lingua-mancanti.ps1). Per le traduzioni rimaste uguali alla chiave.
    /// </summary>
    internal static bool LinLooksItalian(string text) =>
        Regex.IsMatch(text, "[àèéìòùÀÈÉÌÒÙ]") ||
        Regex.IsMatch(text, @"\b(di|del|della|delle|dei|degli|dal|dalla|dalle|il|lo|la|le|gli|un|una|uno|non|per|con|che|nel|nella|nelle|al|alla|alle|sono|questo|questa|messaggio|messaggi|" +
                            @"cartella|cartelle|posta|nuovo|nuova|tutti|tutte|apri|chiudi|salva|elimina|invia|annulla|oggi|ieri|ancora|anche|senza|oppure)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// I sorgenti del progetto e i file delle traduzioni: ogni testo ha la traduzione, con gli stessi segnaposto; nessuna
    /// forma illeggibile né T statico; nessuna chiave senza sorgente, nessun conflitto, nessuna traduzione rimasta italiana;
    /// i file su disco sono quelli incorporati nell'eseguibile.
    /// </summary>
    static void LinSources()
    {
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: i testi T, N e {local:T} dei sorgenti e i file delle traduzioni non sono stati controllati)");
            return;
        }
        var uses = new List<LinUse>();
        int csFiles = 0, xamlFiles = 0;
        foreach (var path in LinSourceFiles(root, ".cs"))
        {
            uses.AddRange(LinScanCs(Path.GetRelativePath(root, path), File.ReadAllText(path)));
            csFiles++;
        }
        foreach (var path in LinSourceFiles(root, ".xaml"))
        {
            uses.AddRange(LinScanXaml(Path.GetRelativePath(root, path), File.ReadAllText(path)));
            xamlFiles++;
        }
        var folder = Path.Combine(root, "Assets", "lingue", "en");
        var disk = Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).Select(p => (File: Path.GetFileName(p), Json: File.ReadAllBytes(p))).ToList();
        var files = Lingua.Merge(disk);
        var texts = files.Texts;
        static string At(LinUse u) => $"{u.File}:{u.Line} \"{u.Key}\"";

        var keyed = uses.Where(u => u.Key != null).ToList();
        var keys = keyed.Select(u => u.Key!).ToHashSet(StringComparer.Ordinal);
        var untranslated = keyed.Where(u => !texts.TryGetValue(u.Key!, out var v) || v.Trim().Length == 0).Select(At).ToList();
        Check(untranslated.Count == 0,
              $"lingua, sorgenti: {keyed.Count} testi in T, N e {{local:T}} ({keys.Count} diversi) in {csFiles} file .cs e {xamlFiles} .xaml, tutti con una traduzione non vuota nei file " +
              $"Assets/lingue/en ({texts.Count} chiavi)" + LinWrong(untranslated));

        var withValues = keyed.Where(u => u.Values).Select(u => u.Key!).ToHashSet(StringComparer.Ordinal);
        var holes = new List<string>();
        foreach (var key in keys)
        {
            if (!texts.TryGetValue(key, out var value)) continue;
            if (!LinHoles(key).SequenceEqual(LinHoles(value))) holes.Add($"\"{key}\" ha {string.Join("", LinHoles(key))}, \"{value}\" {string.Join("", LinHoles(value))}");
            else if ((withValues.Contains(key) || Regex.IsMatch(key, @"\{\d")) && !(LinFormats(key) && LinFormats(value))) holes.Add($"\"{key}\" o \"{value}\" non si formatta");
        }
        Check(holes.Count == 0,
              $"lingua, i segnaposto ({{0}}, {{1:N0}}) della traduzione sono quelli della chiave, e chiave e traduzione dei testi con valori si formattano senza errore ({withValues.Count} testi con valori)" +
              LinWrong(holes));

        var forms = uses.Where(u => u.Problem is "forma" or "espressione")
                        .Select(u => $"{u.File}:{u.Line} {u.Call}" + (u.Problem == "forma"
                            ? (u.Call == "XAML" ? ": scrivi {local:T '...'}" : ": $\"...\" o @\"...\" non si leggono, scrivi \"...\" con {0} e i valori")
                            : ": il testo va da solo fra le parentesi, senza + né espressioni")).ToList();
        Check(forms.Count == 0,
              "lingua, i testi da tradurre sono scritti in modo che si possano leggere: nessun T($\"...\") né T(@\"...\"), nessun testo concatenato o dentro un'espressione in T o N, " +
              "nel XAML sempre {local:T '...'}" + LinWrong(forms));

        var statics = uses.Where(u => u.Problem == "statico").Select(At).ToList();
        Check(statics.Count == 0,
              "lingua, nessun T(...) in un inizializzatore statico (campo, proprietà, costruttore statico): la lingua si sceglie all'avvio, dopo; lì si segna con N e si traduce dove si mostra" +
              LinWrong(statics));

        var orphans = new List<string>();
        foreach (var (file, json) in disk)
            orphans.AddRange(Lingua.Merge([(file, json)]).Texts.Keys.Where(k => !keys.Contains(k)).Select(k => $"{file}: \"{k}\""));
        Check(orphans.Count == 0,
              $"lingua, nessuna chiave dei file delle traduzioni senza un T, N o {{local:T}} nei sorgenti ({texts.Count} chiavi in {disk.Count} file)" + LinWrong(orphans));

        var embedded = Lingua.Resources("en");
        bool sameFiles = embedded.Count == disk.Count && embedded.Zip(disk).All(p => p.First.File == p.Second.File && p.First.Json.AsSpan().SequenceEqual(p.Second.Json));
        var broken = files.Conflicts.Concat(files.Errors).ToList();
        Check(broken.Count == 0 && sameFiles && LinFileNames.All(n => disk.Any(d => d.File == n)),
              $"lingua, i file delle traduzioni ({string.Join(", ", disk.Select(d => d.File))}): nessuna chiave con due traduzioni diverse né ripetuta, nessun file illeggibile; " +
              $"sono quelli incorporati nell'eseguibile ({sameFiles}: dopo una modifica serve dotnet build)" + LinWrong(broken));

        var italian = texts.Where(t => t.Value == t.Key && LinLooksItalian(t.Key)).Select(t => $"\"{t.Key}\"").ToList();
        Check(italian.Count == 0, "lingua, nessuna traduzione uguale alla sua chiave italiana (con lettere accentate o parole come di, il, la, non, per, messaggio)" + LinWrong(italian));
    }

    /// <summary>
    /// Nessun membro del progetto si chiama T o N: un metodo o una funzione locale con quel nome nasconderebbe Lingua.T e
    /// Lingua.N senza un errore del compilatore. Un tipo annidato o un parametro di tipo T danno invece l'errore CS0119.
    /// </summary>
    static void LinShadows()
    {
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var types = typeof(Lingua).Assembly.GetTypes();
        var found = types.Where(t => t != typeof(Lingua)).SelectMany(t => t.GetMembers(all).Where(m => m.MemberType != MemberTypes.NestedType).Select(m => (Type: t, Member: m)))
                         .Where(x => x.Member.Name is "T" or "N" || Regex.IsMatch(x.Member.Name, @"g__[TN]\|")).Select(x => $"{x.Type.FullName}.{x.Member.Name}").ToList();
        var nested = types.Where(t => t.IsNested && t.Name is "T" or "N").Select(t => t.FullName).ToList();
        Check(found.Count == 0,
              $"lingua, nessun metodo, campo, proprietà o funzione locale del progetto si chiama T o N: nasconderebbe Lingua.T o Lingua.N senza errore del compilatore " +
              $"(i tipi annidati con quel nome, qui {(nested.Count == 0 ? "nessuno" : string.Join(", ", nested))}, e i parametri di tipo T danno l'errore CS0119: lì si scrive Lingua.T)" +
              LinWrong(found));
    }
}
#endif
