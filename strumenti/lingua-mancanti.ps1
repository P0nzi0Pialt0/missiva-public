param([string[]]$Files = @())
# Un aiuto per portare Missiva in inglese (Core/Lingua.cs): elenca, con file e riga, i testi che sembrano italiani e non
# passano da T("..."), N("...") o {local:T '...'}. Nei .cs i testi fra virgolette (anche $"..." e @"..."), nei .xaml gli
# attributi di testo (Content, Text, Header, ToolTip, Title, AutomationProperties.Name, PlaceholderText e simili, gli
# StringFormat dei Binding) e il testo dentro gli elementi. "Sembra italiano": lettere accentate o una parola italiana
# comune (articoli, preposizioni, e le parole dell'interfaccia: Rispondi, Elimina, cartella, messaggi...). Salta i
# commenti, le righe con "Log.", i testi dentro una chiamata a Log.* o a una Regex, e i file delle prove (Core\ProvaBozze*,
# Core\DevTools*, Core\*Samples*, Core\TestingProvider.cs, Core\CursorProbe.cs, Core\InputProbe.cs, Core\WheelRecorder.cs)
# e Core\Lingua.cs. Non è una regola: alcuni testi trovati non vanno tradotti (i nomi delle cartelle del server usati per
# riconoscerle, le parole confrontate con quello che arriva da fuori); il controllo vero, che ogni T ha la traduzione, è il
# gruppo "lingua" della suite. Uso:
#   pwsh -File strumenti\lingua-mancanti.ps1                         tutto il progetto
#   pwsh -File strumenti\lingua-mancanti.ps1 -Files UI\SettingsWindow.cs,UI\MainWindow.xaml,"UI\MainWindow.*.cs"
# Un nome di file da solo (SettingsWindow.cs) si cerca nel progetto; un modello con * vale per i file che non sono delle prove.
# Alla fine il conteggio per file.
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
# Con pwsh -File un elenco con le virgole arriva come un testo solo: le virgole si dividono qui.
$Files = @($Files | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = Split-Path $PSScriptRoot

if (-not ("LinguaMancanti" -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public static class LinguaMancanti
{
    static readonly Regex Accents = new Regex("[àèéìòùÀÈÉÌÒÙ]");
    static readonly Regex Words = new Regex(@"\b(" + string.Join("|", new[] {
        // le parole di sempre (le stesse di LinLooksItalian nel gruppo "lingua", che le usa per le traduzioni rimaste in italiano)
        "di", "del", "della", "delle", "dei", "degli", "dal", "dalla", "dalle", "il", "lo", "la", "le", "gli", "un", "una", "uno", "non", "per", "con", "che",
        "nel", "nella", "nelle", "alla", "alle", "al", "sono", "questo", "questa", "messaggio", "messaggi", "cartella", "cartelle", "posta", "nuovo", "nuova",
        "tutti", "tutte", "apri", "chiudi", "salva", "elimina", "invia", "annulla", "oggi", "ieri", "ancora", "anche", "senza", "oppure",
        // le parole dell'interfaccia, che stanno anche da sole su un pulsante
        "rispondi", "inoltra", "archivia", "sposta", "copia", "allega", "allegato", "allegati", "cerca", "impostazioni", "aggiungi", "rimuovi", "modifica",
        "conferma", "riprova", "scarta", "bozza", "bozze", "cestino", "contrassegna", "contrassegno", "segna", "letto", "letti", "posponi", "posposti", "fissa",
        "categoria", "categorie", "categorizza", "filtri", "filtro", "oggetto", "mittente", "destinatari", "destinatario", "giorno", "giorni", "settimana", "mese",
        "anno", "domani", "evento", "eventi", "riunione", "calendario", "persone", "contatti", "contatto", "gruppo", "gruppi", "firma", "firme", "modelli",
        "modello", "regole", "regola", "aspetto", "chiaro", "scuro", "generali", "notifiche", "notifica", "avvio", "chiusura", "informazioni", "aggiornamento",
        "aggiorna", "scarica", "scaricamenti", "mostra", "nascondi", "immagini", "immagine", "stampa", "errore", "avviso", "attenzione", "accedi", "esci",
        "utente", "indirizzo", "nome", "cognome", "telefono", "sito", "inviato", "inviata", "ricevuto", "testo", "carattere", "dimensione", "colore",
        "grassetto", "corsivo", "sottolineato", "elenco", "sinistra", "destra", "sopra", "sotto", "prima", "dopo", "sempre", "mai", "tutto", "nessuno",
        "nessuna", "altro", "altra", "altri", "pagina", "finestra", "scheda", "pulsante", "collegamento", "lingua", "ricerca", "risposta", "risposte", "ore",
        "minuti", "secondi", "ultimo", "ultimi", "prossimo", "prossima", "mattina", "pomeriggio", "sera", "domenica", "lunedì", "gennaio", "febbraio",
        "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre", "esporta", "importa", "sicurezza",
        "ripristina", "accetta", "rifiuta", "provvisorio", "partecipa", "luogo", "inizio", "ripeti", "promemoria", "organizzatore", "partecipanti",
        "facoltativo", "privato", "occupato", "libero", "ufficio", "completato", "scadenza", "importante", "dettagli", "origine", "intestazioni",
        "visualizza", "indietro", "avanti", "giù", "riavvia", "consenti", "accesso", "chiedi", "scegli", "nuovi", "vuota", "vuoto", "caricamento",
    }) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool LooksItalian(string text)
    {
        return Accents.IsMatch(text) || Words.IsMatch(text);
    }

    public sealed class Found
    {
        public int Line;
        public string Text;
    }

    // ---------------------------------------------------------------- C#

    // kind: 0 codice, 1 commento, 2 testo; literals: inizio e fine di ogni testo fra virgolette (non dei caratteri fra apici)
    static void Code(string s, byte[] kind, List<int[]> literals, ref int i, bool hole)
    {
        int braces = 0, parens = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') kind[i++] = 1; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                while (i < end) kind[i++] = 1;
                continue;
            }
            if (c == '"' || c == '\'' || ((c == '$' || c == '@') && Prefixed(s, i))) { Literal(s, kind, literals, ref i); continue; }
            if (hole)
            {
                if (c == '(' || c == '[') parens++;
                else if (c == ')' || c == ']') parens--;
                else if (c == '{') braces++;
                else if (c == '}') { if (braces == 0) return; braces--; }
                else if (c == ':' && braces == 0 && parens <= 0 && !(i + 1 < s.Length && s[i + 1] == ':') && !(i > 0 && s[i - 1] == ':'))
                {
                    while (i < s.Length && s[i] != '}') kind[i++] = 2;
                    return;
                }
            }
            i++;
        }
    }

    static bool Prefixed(string s, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) return false;
        int ats = 0;
        while (i < s.Length && (s[i] == '$' || s[i] == '@')) { if (s[i] == '@' && ++ats > 1) return false; i++; }
        return i < s.Length && s[i] == '"';
    }

    static void Literal(string s, byte[] kind, List<int[]> literals, ref int i)
    {
        int start = i, dollars = 0;
        bool verbatim = false;
        while (i < s.Length && (s[i] == '$' || s[i] == '@')) { if (s[i] == '$') dollars++; else verbatim = true; kind[i++] = 2; }
        if (s[i] == '\'')
        {
            kind[i++] = 2;
            while (i < s.Length && s[i] != '\'' && s[i] != '\n') { if (s[i] == '\\' && i + 1 < s.Length) kind[i++] = 2; kind[i++] = 2; }
            if (i < s.Length && s[i] == '\'') kind[i++] = 2;
            return;
        }
        int quotes = 0;
        while (i + quotes < s.Length && s[i + quotes] == '"') quotes++;
        if (!verbatim && quotes >= 3)
        {
            for (int k = 0; k < quotes; k++) kind[i++] = 2;
            while (i < s.Length)
            {
                if (s[i] == '"')
                {
                    int run = 0;
                    while (i + run < s.Length && s[i + run] == '"') run++;
                    for (int k = 0; k < run; k++) kind[i++] = 2;
                    if (run >= quotes) break;
                    continue;
                }
                if (dollars > 0 && s[i] == '{')
                {
                    int run = 0;
                    while (i + run < s.Length && s[i + run] == '{') run++;
                    for (int k = 0; k < run; k++) kind[i++] = 2;
                    if (run >= dollars)
                    {
                        Code(s, kind, literals, ref i, true);
                        while (i < s.Length && s[i] == '}') kind[i++] = 2;
                    }
                    continue;
                }
                kind[i++] = 2;
            }
            literals.Add(new[] { start, i });
            return;
        }
        kind[i++] = 2;
        while (i < s.Length)
        {
            char c = s[i];
            if (verbatim && c == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"') { kind[i++] = 2; kind[i++] = 2; continue; }
                kind[i++] = 2;
                break;
            }
            if (!verbatim)
            {
                if (c == '\\' && i + 1 < s.Length) { kind[i++] = 2; kind[i++] = 2; continue; }
                if (c == '"') { kind[i++] = 2; break; }
                if (c == '\n') break;
            }
            if (dollars > 0 && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { kind[i++] = 2; kind[i++] = 2; continue; }
                kind[i++] = 2;
                Code(s, kind, literals, ref i, true);
                if (i < s.Length) kind[i++] = 2;
                continue;
            }
            kind[i++] = 2;
        }
        literals.Add(new[] { start, i });
    }

    static int Back(string s, byte[] kind, int i)
    {
        while (i >= 0 && (kind[i] == 1 || (kind[i] == 0 && char.IsWhiteSpace(s[i])))) i--;
        return i;
    }

    static bool WordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

    static string WordBefore(string s, int i)
    {
        int from = i;
        while (from >= 0 && WordChar(s[from])) from--;
        return s.Substring(from + 1, i - from);
    }

    // Il testo che comincia in start è il primo argomento di T(, N(, Lingua.T( o Lingua.N(.
    static bool Wrapped(string s, byte[] kind, int start)
    {
        int open = Back(s, kind, start - 1);
        if (open < 0 || s[open] != '(') return false;
        int name = Back(s, kind, open - 1);
        if (name < 0) return false;
        string word = WordBefore(s, name);
        if (word != "T" && word != "N") return false;
        int before = Back(s, kind, name - word.Length);
        if (before >= 0 && s[before] == '.') return WordBefore(s, Back(s, kind, before - 1)) == "Lingua";
        // Una parola prima di T è una parola chiave ("return T(", "await T(", "yield return T("), non un pezzo del nome:
        // WordBefore ha già preso tutte le lettere attaccate. Solo "new T(" è un tipo.
        return before < 0 || !WordChar(s[before]) || WordBefore(s, before) != "new";
    }

    // Il testo sta (anche più fuori) fra le parentesi di una chiamata a Log.* o a una Regex: non va sullo schermo.
    static bool InsideLogOrRegex(string s, byte[] kind, int start)
    {
        int depth = 0, levels = 0;
        for (int k = start - 1; k >= 0 && start - k < 6000 && levels < 6; k--)
        {
            if (kind[k] != 0) continue;
            char c = s[k];
            if (c == ')' || c == ']') depth++;
            else if (c == '(' || c == '[')
            {
                if (depth > 0) { depth--; continue; }
                levels++;
                if (c != '(') continue;
                int end = Back(s, kind, k - 1);
                int from = end;
                while (from >= 0 && (WordChar(s[from]) || s[from] == '.')) from--;
                string name = end >= 0 ? s.Substring(from + 1, end - from) : "";
                if (name.StartsWith("Log.", StringComparison.Ordinal) || name.Contains("Regex")) return true;
            }
            else if (depth == 0 && (c == ';' || c == '{' || c == '}')) return false;
        }
        return false;
    }

    public static List<Found> ScanCs(string s)
    {
        var found = new List<Found>();
        var kind = new byte[s.Length];
        var literals = new List<int[]>();
        int i = 0;
        Code(s, kind, literals, ref i, false);
        var lineStarts = new List<int> { 0 };
        for (int k = 0; k < s.Length; k++) if (s[k] == '\n') lineStarts.Add(k + 1);
        foreach (var span in literals)
        {
            int start = span[0], end = Math.Min(span[1], s.Length);
            string raw = s.Substring(start, end - start);
            if (!LooksItalian(Regex.Replace(raw, @"\\[nrt]", " "))) continue;
            // Le opzioni della riga di comando ("--senza-rete") e una parola sola in minuscolo ("apri", "sera"): comandi e chiavi, quasi mai schermo.
            if (Regex.IsMatch(raw, "^\"(--|[a-zàèéìòù]+\"$)")) continue;
            int index = lineStarts.BinarySearch(start);
            int line = index >= 0 ? index : ~index - 1;
            int lineEnd = s.IndexOf('\n', lineStarts[line]);
            string lineText = s.Substring(lineStarts[line], (lineEnd < 0 ? s.Length : lineEnd) - lineStarts[line]);
            if (lineText.Contains("Log.") || InsideLogOrRegex(s, kind, start)) continue;
            bool wrapped = Wrapped(s, kind, start);
            if (wrapped && raw[0] == '"' && !raw.StartsWith("\"\"\"", StringComparison.Ordinal)) continue;
            string shown = raw.Replace("\r", "").Replace("\n", " ");
            if (shown.Length > 140) shown = shown.Substring(0, 137) + "...";
            if (wrapped) shown += "   <- dentro T o N ma con $, @ o \"\"\": va riscritto con {0}";
            found.Add(new Found { Line = line + 1, Text = shown });
        }
        return found;
    }

    // ---------------------------------------------------------------- XAML

    static readonly HashSet<string> TextAttributes = new HashSet<string>(StringComparer.Ordinal) {
        "Content", "Text", "Header", "ToolTip", "Title", "AutomationProperties.Name", "AutomationProperties.HelpText", "AutomationProperties.ItemStatus",
        "PlaceholderText", "Description", "Label", "ToolTipService.ToolTip", "Watermark", "ContentStringFormat", "HeaderStringFormat", "StringFormat",
    };

    public static List<Found> ScanXaml(string s)
    {
        var found = new List<Found>();
        string text = Regex.Replace(s, "<!--.*?-->", m => Regex.Replace(m.Value, "[^\n]", " "), RegexOptions.Singleline);
        foreach (Match a in Regex.Matches(text, "(?<name>[\\w.:]+)\\s*=\\s*\"(?<value>[^\"]*)\""))
        {
            string name = a.Groups["name"].Value, value = WebUtility.HtmlDecode(a.Groups["value"].Value);
            int colon = name.IndexOf(':');
            string bare = colon >= 0 ? name.Substring(colon + 1) : name;
            bool shown;
            if (value.StartsWith("{}", StringComparison.Ordinal)) shown = TextAttributes.Contains(bare) && LooksItalian(value); // {} davanti: un testo, non un'estensione
            else if (value.StartsWith("{", StringComparison.Ordinal)) shown = value.Contains("StringFormat") && LooksItalian(value); // {local:T ...} no, il formato di un Binding sì
            else shown = TextAttributes.Contains(bare) && LooksItalian(value);
            if (shown) found.Add(new Found { Line = 1 + Count(text, a.Index), Text = name + "=\"" + value + "\"" });
        }
        foreach (Match t in Regex.Matches(text, ">(?<text>[^<>]+)<"))
        {
            string raw = t.Groups["text"].Value, value = WebUtility.HtmlDecode(raw).Trim();
            if (value.Length == 0 || value.StartsWith("{", StringComparison.Ordinal) || !LooksItalian(value)) continue;
            int at = t.Groups["text"].Index + raw.Length - raw.TrimStart().Length;
            found.Add(new Found { Line = 1 + Count(text, at), Text = "testo: " + value.Replace("\r", "").Replace("\n", " ") });
        }
        found.Sort((x, y) => x.Line.CompareTo(y.Line));
        return found;
    }

    static int Count(string text, int upTo)
    {
        int n = 0;
        for (int k = 0; k < upTo && k < text.Length; k++) if (text[k] == '\n') n++;
        return n;
    }
}
'@
}

# I file delle prove e il meccanismo stesso: i loro testi non vanno sullo schermo.
function Test-Escluso([string]$Relativo) {
    $r = $Relativo -replace '/', '\'
    return $r -like 'Core\ProvaBozze*' -or $r -like 'Core\DevTools*' -or $r -like 'Core\*Samples*' -or
        $r -in @('Core\TestingProvider.cs', 'Core\CursorProbe.cs', 'Core\InputProbe.cs', 'Core\WheelRecorder.cs', 'Core\Lingua.cs')
}

function Get-Sorgenti {
    Get-ChildItem -Path $root -Recurse -File -Include *.cs, *.xaml | Where-Object {
        $parts = [IO.Path]::GetRelativePath($root, $_.FullName).Split([IO.Path]::DirectorySeparatorChar)
        -not ($parts | Select-Object -SkipLast 1 | Where-Object { $_ -in @('bin', 'obj') -or $_.StartsWith('.') })
    }
}

$chosen = [System.Collections.Generic.List[string]]::new()
if ($Files.Count -eq 0) {
    foreach ($f in Get-Sorgenti) { if (-not (Test-Escluso ([IO.Path]::GetRelativePath($root, $f.FullName)))) { $chosen.Add($f.FullName) } }
} else {
    $all = $null
    foreach ($item in $Files) {
        $path = if ([IO.Path]::IsPathRooted($item)) { $item } else { Join-Path $root $item }
        if ($item -notmatch '[*?]' -and (Test-Path -LiteralPath $path -PathType Leaf)) { $chosen.Add((Resolve-Path -LiteralPath $path).Path); continue }
        $hits = @()
        if ($item -match '[*?]') { $hits = @(Get-ChildItem -Path $path -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.cs', '.xaml' }) }
        if ($hits.Count -eq 0 -and $item -notmatch '[\\/]') {
            if (-not $all) { $all = @(Get-Sorgenti) }
            $hits = @($all | Where-Object { $_.Name -like $item })
        }
        if ($hits.Count -eq 0) { Write-Warning "nessun file per '$item'"; continue }
        foreach ($m in $hits) {
            if ($item -match '[*?]' -and (Test-Escluso ([IO.Path]::GetRelativePath($root, $m.FullName)))) { continue }
            $chosen.Add($m.FullName)
        }
    }
}

$counts = [ordered]@{}
$total = 0
foreach ($file in ($chosen | Select-Object -Unique)) {
    $relative = [IO.Path]::GetRelativePath($root, $file)
    $text = [IO.File]::ReadAllText($file)
    $found = if ($file.EndsWith('.xaml', [StringComparison]::OrdinalIgnoreCase)) { [LinguaMancanti]::ScanXaml($text) } else { [LinguaMancanti]::ScanCs($text) }
    foreach ($f in $found) { "{0}:{1}: {2}" -f $relative, $f.Line, $f.Text }
    if ($found.Count -gt 0) { $counts[$relative] = $found.Count; $total += $found.Count }
}
""
"--- per file"
foreach ($k in $counts.Keys) { "{0}: {1}" -f $k, $counts[$k] }
"in tutto: $total testi da guardare in $($counts.Count) file (su $(@($chosen | Select-Object -Unique).Count) letti)"
