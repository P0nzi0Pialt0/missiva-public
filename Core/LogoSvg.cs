using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Missiva;

/// <summary>
/// L'SVG di un logo prima di SharpVectors (audit, S-01): non una lista nera sul testo ma una lista bianca sull'albero.
/// I byte si leggono come UTF-8 (niente gzip, UTF-16, byte nulli), con un lettore XML senza DTD; restano solo gli
/// elementi e gli attributi del disegno statico, i riferimenti solo interni (#id), gli stili riscritti dichiarazione per
/// dichiarazione; il risultato si riscrive da capo e a SharpVectors arriva quel testo, non i byte del sito: quello che
/// si controlla è quello che si disegna. Null se l'SVG non va (allora niente logo da quel file).
/// Misurato il 4 ottobre 2026 con SharpVectors 1.8.6 (docs\audit\ESITO.md, blocco 2): scarica immagini, fogli di stile e
/// use esterni anche da 127.0.0.1 e apre percorsi di rete; finisce lo stack con 3.000 gruppi annidati o una maschera
/// che usa sé stessa; non finisce mai (e chiede 1 GB al secondo) con fill:url(http...) in uno stile, con una parentesi
/// non chiusa in un colore, con @media (condizione), @keyframes, @supports; moltiplica use, motivi e maschere annidati.
/// Da qui i tetti: profondità, elementi, lavoro (gli elementi contati quante volte si disegnano).
/// </summary>
internal static class LogoSvg
{
    const string SvgNs = "http://www.w3.org/2000/svg", XlinkNs = "http://www.w3.org/1999/xlink", XmlNs = "http://www.w3.org/XML/1998/namespace";
    internal const int MaxDepth = 64, MaxElements = 10_000, MaxWork = 20_000;
    const long Saturated = 1_000_000_000_000;

    static HashSet<string> Set(string names) => new(names.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>Gli elementi che restano. Gli altri si tolgono con quello che contengono (metadati, filtri, marcatori, animazioni, caratteri).</summary>
    static readonly HashSet<string> Kept = Set("svg g defs symbol use switch a path rect circle ellipse line polyline polygon text tspan textPath " +
                                               "linearGradient radialGradient stop clipPath mask pattern style");

    /// <summary>Con questi il logo non sarebbe quello: l'SVG intero si rifiuta, come faceva il filtro di prima.</summary>
    static readonly HashSet<string> Refused = Set("script image foreignObject iframe embed object audio video canvas");

    /// <summary>Le proprietà di presentazione: valgono come attributi e come dichiarazioni di stile.</summary>
    static readonly HashSet<string> Presentation = Set(
        "alignment-baseline baseline-shift clip clip-path clip-rule color color-interpolation color-rendering direction display dominant-baseline " +
        "enable-background fill fill-opacity fill-rule font font-family font-size font-size-adjust font-stretch font-style font-variant font-weight " +
        "glyph-orientation-horizontal glyph-orientation-vertical image-rendering isolation kerning letter-spacing mask mix-blend-mode opacity overflow " +
        "paint-order pointer-events shape-rendering solid-color solid-opacity stop-color stop-opacity stroke stroke-dasharray stroke-dashoffset " +
        "stroke-linecap stroke-linejoin stroke-miterlimit stroke-opacity stroke-width text-anchor text-decoration text-rendering transform " +
        "transform-origin unicode-bidi vector-effect visibility word-spacing writing-mode");

    static readonly HashSet<string> Geometry = Set(
        "id class style viewBox preserveAspectRatio x y width height version baseProfile d points cx cy r rx ry x1 y1 x2 y2 pathLength " +
        "gradientUnits gradientTransform spreadMethod fx fy fr offset patternUnits patternContentUnits patternTransform clipPathUnits maskUnits " +
        "maskContentUnits dx dy rotate textLength lengthAdjust startOffset method spacing systemLanguage requiredFeatures requiredExtensions type media");

    /// <summary>Si ereditano: un motivo messo su un gruppo si ridisegna per ogni elemento che il gruppo contiene.</summary>
    static readonly HashSet<string> Inherited = Set("fill stroke");

    static readonly Regex AnyUrl = new(@"url\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex InnerUrl = new(@"url\s*\(\s*(?:'\s*#([^\s'""()]+)\s*'|""\s*#([^\s'""()]+)\s*""|#([^\s'""()]+))\s*\)",
                                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    const string Compound = @"(?:\*|[A-Za-z][A-Za-z0-9]*|[.#]-?[A-Za-z_][A-Za-z0-9_-]*)(?:[.#]-?[A-Za-z_][A-Za-z0-9_-]*)*";
    static readonly Regex Selector = new($@"^{Compound}(?:(?:\s*>\s*|\s+){Compound})*$", RegexOptions.NonBacktracking);
    static readonly Regex PropertyName = new("^-?[a-z][a-z0-9-]*$", RegexOptions.NonBacktracking);

    sealed class Node(string name, int parent)
    {
        public readonly string Name = name;
        public readonly int Parent = parent;
        public List<int>? Children;
        public List<string>? Links;                        // href="#id": il contenuto dell'altro si disegna qui
        public List<(string Id, bool Inherited)>? Paints;  // url(#id) negli attributi e nello stile dell'elemento
        public byte State;                                 // 0 da misurare, 1 in corso, 2 misurato
        public long Count, Work;                           // elementi disegnati qui dentro, e con i motivi e le maschere
        public int Height;
        public bool Heavy;                                 // usa (anche più in basso) un motivo o una maschera
    }

    /// <summary>L'SVG ripulito e riscritto, da dare a SharpVectors; null se non è un logo che Missiva disegna.</summary>
    public static string? Clean(byte[] data)
    {
        // gzip (svgz), UTF-16 e UTF-32, byte nulli: il testo che si controlla deve essere quello che si disegna.
        if (data.Length < 8 || (data[0] == 0x1F && data[1] == 0x8B) || (data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)
            || Array.IndexOf(data, (byte)0) >= 0)
            return null;
        int start = data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        try
        {
            return Clean(Encoding.UTF8.GetString(data, start, data.Length - start));
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? Clean(string text)
    {
        var output = new StringBuilder(text.Length);
        var nodes = new List<Node>();
        var ids = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var sheetPaints = new List<string>();
        var open = new Stack<int>();
        StringBuilder? css = null;
        int skipped = 0, textDepth = 0;
        bool bare = false;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, MaxCharactersInDocument = 4 * 1024 * 1024 };
        using (var reader = XmlReader.Create(new StringReader(text), settings))
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction:
                        if (reader.Name.Equals("xml-stylesheet", StringComparison.OrdinalIgnoreCase)) return null;
                        break;
                    case XmlNodeType.Element:
                    {
                        if (skipped > 0)
                        {
                            if (!reader.IsEmptyElement) skipped++;
                            break;
                        }
                        string name = reader.LocalName;
                        // Lo spazio dei nomi di SVG, o nessuno se manca anche alla radice (SharpVectors li disegna, come prima).
                        if (nodes.Count == 0) bare = reader.NamespaceURI.Length == 0;
                        bool svg = reader.NamespaceURI == (bare ? "" : SvgNs), empty = reader.IsEmptyElement;
                        if (open.Count == 0 && !(svg && name == "svg" && nodes.Count == 0)) return null;
                        if (svg && Refused.Contains(name)) return null;
                        // Dentro <style> c'è solo testo; un foglio di un altro tipo non si legge.
                        if (!svg || !Kept.Contains(name) || css != null
                            || (name == "style" && reader.GetAttribute("type") is { } type && !type.Trim().Equals("text/css", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!empty) skipped = 1;
                            break;
                        }
                        if (open.Count >= MaxDepth || nodes.Count >= MaxElements) return null;
                        var node = new Node(name, open.Count > 0 ? open.Peek() : -1);
                        int index = nodes.Count;
                        nodes.Add(node);
                        if (node.Parent >= 0) (nodes[node.Parent].Children ??= new()).Add(index);
                        writer.WriteStartElement(name, SvgNs);
                        while (reader.MoveToNextAttribute())
                        {
                            string attribute = reader.LocalName, space = reader.NamespaceURI, value = reader.Value;
                            if (attribute == "href" && (space.Length == 0 || space == XlinkNs))
                            {
                                value = value.Trim();
                                if (value.Length < 2 || value[0] != '#' || value.Contains('%') || !Sound(value, false)) return null;
                                (node.Links ??= new()).Add(value[1..]);
                                if (space.Length == 0) writer.WriteAttributeString("href", value);
                                else writer.WriteAttributeString("xlink", "href", XlinkNs, value);
                            }
                            else if (space == XmlNs && attribute is "space" or "lang")
                                writer.WriteAttributeString("xml", attribute, XmlNs, value);
                            else if (space.Length == 0 && attribute == "style")
                            {
                                var list = new List<(string, string)>();
                                if (Declarations(value, list) is not { } style) return null;
                                foreach (var (property, id) in list) (node.Paints ??= new()).Add((id, Inherited.Contains(property)));
                                if (style.Length > 0) writer.WriteAttributeString("style", style);
                            }
                            else if (space.Length == 0 && (Presentation.Contains(attribute) || Geometry.Contains(attribute)))
                            {
                                bool presentation = Presentation.Contains(attribute);
                                var list = new List<string>();
                                if (!Sound(value, presentation) || !Paints(value, list)) return null;
                                foreach (var id in list) (node.Paints ??= new()).Add((id, Inherited.Contains(attribute)));
                                if (attribute == "id")
                                {
                                    if (!ids.TryGetValue(value, out var same)) ids[value] = same = new();
                                    same.Add(index);
                                }
                                writer.WriteAttributeString(attribute, value);
                            }
                        }
                        reader.MoveToElement();
                        if (name == "style" && !empty) css = new StringBuilder();
                        if (empty) writer.WriteEndElement();
                        else
                        {
                            open.Push(index);
                            if (name == "text") textDepth++;
                        }
                        break;
                    }
                    case XmlNodeType.EndElement:
                        if (skipped > 0)
                        {
                            skipped--;
                            break;
                        }
                        if (nodes[open.Pop()].Name == "text") textDepth--;
                        if (css != null)
                        {
                            if (Sheet(css.ToString(), sheetPaints) is not { } sheet) return null;
                            writer.WriteString(sheet);
                            css = null;
                        }
                        writer.WriteEndElement();
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                        if (skipped > 0) break;
                        if (css != null) css.Append(reader.Value);
                        else if (textDepth > 0) writer.WriteString(reader.Value);
                        break;
                }
            }
        }
        if (nodes.Count == 0) return null;

        // I riferimenti: dentro i tetti, senza cicli, e i motivi e le maschere mai uno dentro l'altro.
        IEnumerable<int> Targets(string id) => ids.TryGetValue(id, out var found) ? found : [];
        static bool Gradient(Node n) => n.Name is "linearGradient" or "radialGradient" or "stop";
        // Un foglio di stile non dice a quali elementi tocca: da lì solo sfumature e ritagli, che non si moltiplicano.
        if (sheetPaints.SelectMany(Targets).Any(t => !Gradient(nodes[t]) && nodes[t].Name != "clipPath")) return null;

        bool Measure(int index, int depth)
        {
            var node = nodes[index];
            if (node.State == 2) return depth + node.Height - 1 <= MaxDepth;
            if (node.State == 1 || depth > MaxDepth) return false; // un ciclo, o troppo in profondità seguendo i riferimenti
            node.State = 1;
            long count = 1, work = 1;
            int height = 1;
            bool heavy = false;
            foreach (int next in (node.Children ?? Enumerable.Empty<int>()).Concat((node.Links ?? Enumerable.Empty<string>()).SelectMany(Targets)))
            {
                if (!Measure(next, depth + 1)) return false;
                var other = nodes[next];
                count = Math.Min(count + other.Count, Saturated);
                work = Math.Min(work + other.Work, Saturated);
                height = Math.Max(height, other.Height + 1);
                heavy |= other.Heavy;
            }
            foreach (var (id, inherited) in node.Paints ?? Enumerable.Empty<(string, bool)>())
                foreach (int target in Targets(id))
                {
                    var other = nodes[target];
                    if (Gradient(other)) continue;
                    if (!Measure(target, depth + 1)) return false;
                    if (other.Name == "clipPath")
                    {
                        work = Math.Min(work + other.Count, Saturated);
                        continue;
                    }
                    // Un motivo o una maschera: il suo contenuto non ne usa altri, e non ne eredita da chi lo contiene.
                    if (other.Heavy) return false;
                    for (int up = other.Parent; up >= 0; up = nodes[up].Parent)
                        if (nodes[up].Paints is { } above && above.Any(p => p.Inherited && Targets(p.Id).Any(t => !Gradient(nodes[t]) && nodes[t].Name != "clipPath")))
                            return false;
                    heavy = true;
                    work = Math.Min(work + (inherited ? count : 1) * other.Count, Saturated);
                }
            (node.Count, node.Work, node.Height, node.Heavy, node.State) = (count, work, height, heavy, 2);
            return count <= MaxElements && work <= MaxWork;
        }

        if (!Measure(0, 1)) return null;
        long sheetWork = sheetPaints.SelectMany(Targets).Where(t => nodes[t].Name == "clipPath").Sum(t => nodes[t].Count);
        if (nodes[0].Work + nodes[0].Count * sheetWork > MaxWork) return null;
        return output.ToString();
    }

    /// <summary>
    /// Un valore che SharpVectors legge senza perdersi: niente "\" (le sequenze di CSS), parentesi chiuse e mai una
    /// dentro l'altra; nelle proprietà di presentazione anche le virgolette chiuse, senza parentesi dentro.
    /// </summary>
    static bool Sound(string value, bool presentation)
    {
        int depth = 0;
        char quote = '\0';
        foreach (char c in value)
        {
            if (c == '\\') return false;
            if (quote != '\0')
            {
                if (c is '(' or ')') return false;
                if (c == quote) quote = '\0';
            }
            else if (presentation && c is '\'' or '"') quote = c;
            else if (c == '(' && ++depth > 1) return false;
            else if (c == ')' && --depth < 0) return false;
        }
        return depth == 0 && quote == '\0';
    }

    /// <summary>
    /// Ogni url(...) del valore è un riferimento interno, url(#id): gli id in ids. Falso se ce n'è uno che non lo è, o
    /// con un "%" nell'id (scritto in un altro modo potrebbe essere l'id di un altro elemento).
    /// </summary>
    static bool Paints(string value, List<string> ids)
    {
        if (!value.Contains('(')) return true;
        var inner = InnerUrl.Matches(value);
        if (inner.Count != AnyUrl.Count(value)) return false;
        foreach (Match m in inner)
        {
            var id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (id.Contains('%')) return false;
            ids.Add(id);
        }
        return true;
    }

    /// <summary>
    /// "proprietà:valore;..." riscritto: solo le proprietà di presentazione, con valori sani e riferimenti interni (in
    /// paints, con la proprietà). Le altre dichiarazioni si tolgono; null se un valore non va (l'SVG si rifiuta).
    /// </summary>
    static string? Declarations(string text, List<(string Property, string Id)> paints)
    {
        var output = new StringBuilder();
        foreach (var part in text.Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon < 0)
            {
                if (part.AsSpan().Trim().Length == 0) continue;
                return null;
            }
            var property = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();
            if (!PropertyName.IsMatch(property)) return null;
            if (!Presentation.Contains(property) || value.Length == 0) continue;
            var ids = new List<string>();
            if (value.AsSpan().IndexOfAny(":{}@<>") >= 0 || !Sound(value, true) || !Paints(value, ids)) return null;
            foreach (var id in ids) paints.Add((property, id));
            output.Append(property).Append(':').Append(value).Append(';');
        }
        return output.ToString();
    }

    /// <summary>
    /// Il foglio di stile riscritto: senza commenti, senza regole @ (@import rifiuta l'SVG; @media, @keyframes e le altre
    /// si tolgono: il logo è quello del tema chiaro, fermo), solo selettori semplici (elemento, .classe, #id, discendente,
    /// figlio), dichiarazioni come sopra. Null se è scritto male.
    /// </summary>
    static string? Sheet(string css, List<string> paints)
    {
        var plain = new StringBuilder(css.Length);
        for (int at = 0; at < css.Length;)
        {
            int comment = css.IndexOf("/*", at, StringComparison.Ordinal);
            if (comment < 0)
            {
                plain.Append(css, at, css.Length - at);
                break;
            }
            int end = css.IndexOf("*/", comment + 2, StringComparison.Ordinal);
            if (end < 0) return null;
            plain.Append(css, at, comment - at).Append(' ');
            at = end + 2;
        }
        css = plain.ToString();
        if (css.Contains('\\')) return null;
        var output = new StringBuilder(css.Length);
        int i = 0;
        while (true)
        {
            while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
            if (i >= css.Length) break;
            int brace = css.IndexOf('{', i);
            if (css[i] == '@')
            {
                if (css.AsSpan(i).StartsWith("@import", StringComparison.OrdinalIgnoreCase)) return null;
                int semicolon = css.IndexOf(';', i);
                if (semicolon >= 0 && (brace < 0 || semicolon < brace))
                {
                    i = semicolon + 1;
                    continue;
                }
                if (brace < 0) return null;
                int depth = 0, k = brace;
                for (; k < css.Length; k++)
                    if (css[k] == '{') depth++;
                    else if (css[k] == '}' && --depth == 0) break;
                if (k >= css.Length) return null;
                i = k + 1;
                continue;
            }
            int close = brace < 0 ? -1 : css.IndexOf('}', brace);
            if (close < 0) return null;
            var selectors = css[i..brace].Split(',').Select(s => s.Trim()).ToList();
            var body = css[(brace + 1)..close];
            i = close + 1;
            if (body.Contains('{')) return null;
            if (selectors.Any(s => s.Length > 200 || !Selector.IsMatch(s))) continue; // :hover, [attributo], :root: la regola si toglie
            var found = new List<(string Property, string Id)>();
            if (Declarations(body, found) is not { } declarations) return null;
            paints.AddRange(found.Select(f => f.Id));
            output.Append(string.Join(',', selectors)).Append('{').Append(declarations).Append('}');
        }
        return output.ToString();
    }
}
