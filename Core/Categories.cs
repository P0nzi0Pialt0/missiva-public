namespace Missiva;

/// <summary>Una categoria: in Gmail un'etichetta, negli account Microsoft una categoria di Outlook.</summary>
public sealed record Category(string Name, int Preset);

/// <summary>
/// I 25 colori delle categorie di Outlook (preset0-preset24 in Microsoft Graph). Gmail non comunica i colori delle
/// etichette ai programmi di posta: per Gmail il colore lo sceglie l'utente in Missiva, o ne viene assegnato uno.
/// </summary>
public static class CategoryPalette
{
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("Rosso", "#D13438"), ("Arancione", "#F7630C"), ("Marrone", "#8E562E"), ("Giallo", "#FCE100"), ("Verde", "#13A10E"),
        ("Verde acqua", "#00B7C3"), ("Oliva", "#8B8F3A"), ("Blu", "#0078D4"), ("Viola", "#8764B8"), ("Mirtillo", "#C30052"),
        ("Acciaio", "#69797E"), ("Acciaio scuro", "#394146"), ("Grigio", "#7A7574"), ("Grigio scuro", "#484644"), ("Nero", "#201F1E"),
        ("Rosso scuro", "#A4262C"), ("Arancione scuro", "#CA5010"), ("Marrone scuro", "#5E3A1E"), ("Giallo scuro", "#C19C00"),
        ("Verde scuro", "#0B6A0B"), ("Verde acqua scuro", "#005B70"), ("Oliva scuro", "#5C5A28"), ("Blu scuro", "#004E8C"),
        ("Viola scuro", "#5C2E91"), ("Mirtillo scuro", "#6B0E2C"),
    };

    /// <summary>
    /// Colori delle categorie note, per account (dall'elenco delle categorie o dalle scelte dell'utente). Sotto lucchetto:
    /// lo scrive l'interfaccia (Remember) e lo leggono anche i provider, da qualunque thread li si chiami (audit dell'11
    /// ottobre, C-27). La mappa di un account non cambia più dopo essere entrata qui: Remember ne mette una nuova.
    /// </summary>
    static readonly Dictionary<string, Dictionary<string, int>> Known = new();

    public static void Remember(string accountId, IEnumerable<Category> categories)
    {
        var map = categories.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First().Preset, StringComparer.OrdinalIgnoreCase);
        lock (Known) Known[accountId] = map;
    }

    /// <summary>Colore della categoria: quello noto, quello scelto in Missiva, o uno fisso ricavato dal nome.</summary>
    public static int PresetOf(string accountId, string name)
    {
        Dictionary<string, int>? map;
        lock (Known) Known.TryGetValue(accountId, out map);
        if (map != null && map.TryGetValue(name, out var preset)) return preset;
        if (Store.Settings.CategoryColors.TryGetValue(accountId + "|" + name, out var chosen)) return chosen;
        int hash = 0;
        foreach (var c in name.ToLowerInvariant()) hash = hash * 31 + c;
        return Math.Abs(hash % 10); // i dieci colori vivaci
    }

    public static string Hex(int preset) => Presets[Math.Clamp(preset, 0, Presets.Length - 1)].Hex;

    /// <summary>Testo bianco o nero, quello che si legge meglio sul colore.</summary>
    public static string TextHex(int preset)
    {
        var hex = Hex(preset);
        int r = Convert.ToInt32(hex[1..3], 16), g = Convert.ToInt32(hex[3..5], 16), b = Convert.ToInt32(hex[5..7], 16);
        return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? "#1B1B1B" : "#FFFFFF";
    }

    public static string GraphName(int preset) => "preset" + Math.Clamp(preset, 0, Presets.Length - 1);

    public static int FromGraph(string? color) =>
        color != null && color.StartsWith("preset") && int.TryParse(color[6..], out var n) && n >= 0 && n < Presets.Length ? n : 12;

#if DEBUG
    /// <summary>Prove: gli account di prova escono dai colori noti.</summary>
    internal static void TestForget(Func<string, bool> accountId)
    {
        lock (Known)
            foreach (var id in Known.Keys.Where(accountId).ToList()) Known.Remove(id);
    }
#endif
}
