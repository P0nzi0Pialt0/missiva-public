using System.Windows;
using System.Windows.Media;

namespace Missiva.UI;

public enum SuggestionKind { Search, Recent, Person, File, Mail }

/// <summary>
/// Una riga del pannello della ricerca, come nel nuovo Outlook: la ricerca da fare, una ricerca recente, una persona, un
/// file allegato o un messaggio. Il titolo è diviso intorno a quello che si è scritto, che va in grassetto.
/// </summary>
public sealed class SearchSuggestion
{
    public SuggestionKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    /// <summary>Quello che si cerca scegliendo la riga.</summary>
    public string Query { get; init; } = "";
    /// <summary>File e messaggi: il messaggio da aprire fra i risultati.</summary>
    public MessageSummary? Message { get; init; }
    /// <summary>File e messaggi: l'account del messaggio (nella ricerca in tutti gli account i risultati sono di più account).</summary>
    public IMailProvider? Provider { get; init; }
    /// <summary>File, nella ricerca in tutti gli account: quello che si è scritto, da cercare con la sintassi di ogni account.</summary>
    public string Typed { get; init; } = "";
    /// <summary>Persone: al posto del glifo l'immagine con le iniziali, come nell'elenco.</summary>
    public Address? Person { get; init; }
    public string Glyph { get; init; } = "";
    public Brush? GlyphBrush { get; init; }

    public string Before { get; private set; } = "";
    public string Match { get; private set; } = "";
    public string After { get; private set; } = "";

    public Visibility SubtitleVisibility => Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => Person == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AvatarVisibility => Person != null ? Visibility.Visible : Visibility.Collapsed;
    public string Initials => Person is { } p ? Avatars.Initials(p) : "";
    public Brush? AvatarBrush => Person is { } p ? Avatars.Brush(p) : null;

    /// <summary>La prima volta che compare quello che si è scritto va in grassetto.</summary>
    public SearchSuggestion Highlight(string typed)
    {
        int i = typed.Length > 0 ? Title.IndexOf(typed, StringComparison.CurrentCultureIgnoreCase) : -1;
        if (i < 0) (Before, Match, After) = (Title, "", "");
        else (Before, Match, After) = (Title[..i], Title.Substring(i, typed.Length), Title[(i + typed.Length)..]);
        return this;
    }

    /// <summary>Per UI Automation e l'Assistente vocale.</summary>
    public override string ToString() => Subtitle.Length > 0 ? $"{Title}, {Subtitle}" : Title;
}
