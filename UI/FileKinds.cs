namespace Missiva.UI;

/// <summary>
/// Il tipo di file di un allegato, dal nome (e dal tipo MIME, quando c'è), per le icone come nel nuovo Outlook: la scheda
/// del riquadro di lettura (ReadingPage.FileIcon) e la pastiglia nell'elenco (AttachmentChip) usano gli stessi colori.
/// </summary>
public static class FileKinds
{
    public enum Kind { Pdf, Word, Excel, PowerPoint, Archive, Image, Audio, Video, Calendar, Mail, Other }

    public static string Extension(string name) => Path.GetExtension(name.Trim().TrimEnd('.', ' ')).TrimStart('.').ToLowerInvariant();

    public static Kind Of(string name, string contentType = "") => Extension(name) switch
    {
        "pdf" => Kind.Pdf,
        "doc" or "docx" or "docm" or "dot" or "dotx" or "odt" or "rtf" => Kind.Word,
        "xls" or "xlsx" or "xlsm" or "xlsb" or "csv" or "ods" => Kind.Excel,
        "ppt" or "pptx" or "pptm" or "pps" or "ppsx" or "odp" => Kind.PowerPoint,
        "zip" or "rar" or "7z" or "gz" or "tgz" or "tar" or "bz2" or "xz" => Kind.Archive,
        "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "heic" or "heif" or "tif" or "tiff" or "svg" => Kind.Image,
        "mp3" or "m4a" or "wav" or "ogg" or "opus" or "flac" or "aac" or "wma" or "amr" => Kind.Audio,
        "mp4" or "mov" or "avi" or "mkv" or "webm" or "wmv" or "m4v" or "3gp" => Kind.Video,
        "ics" or "vcs" => Kind.Calendar,
        "eml" or "msg" => Kind.Mail,
        _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => Kind.Image,
        _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => Kind.Audio,
        _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => Kind.Video,
        _ when contentType.Equals("text/calendar", StringComparison.OrdinalIgnoreCase) => Kind.Calendar,
        _ when contentType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase) => Kind.Mail,
        _ => Kind.Other,
    };

    /// <summary>Colore della fascia della scheda nel riquadro di lettura, che porta sopra un testo bianco.</summary>
    public static string Band(Kind kind, bool light) => kind switch
    {
        Kind.Pdf => "#D13438",
        Kind.Word => "#185ABD",
        Kind.Excel => "#107C41",
        Kind.PowerPoint => "#C43E1C",
        Kind.Archive => "#9A6700",
        Kind.Image => "#038387",
        Kind.Audio or Kind.Video => "#8764B8",
        Kind.Calendar or Kind.Mail => "#0F6CBD",
        _ => light ? "#6B6B6B" : "#7A7A7A",
    };

    /// <summary>Colore del glifo nell'elenco: le stesse tinte in toni medi, leggibili sul fondo chiaro e su quello scuro.</summary>
    public static string GlyphColor(Kind kind) => kind switch
    {
        Kind.Pdf => "#E0393E",
        Kind.Word => "#2B7CD3",
        Kind.Excel => "#1E9E57",
        Kind.PowerPoint => "#D35230",
        Kind.Archive => "#B7811B",
        Kind.Image => "#0E9AA7",
        Kind.Audio or Kind.Video => "#8E6CC7",
        Kind.Calendar or Kind.Mail => "#0078D4",
        _ => "#8A8A8A",
    };

    /// <summary>Glifo di Segoe Fluent Icons per l'elenco: foglio, immagine, pacco, nota, video, calendario, busta.</summary>
    public static string Glyph(Kind kind) => kind switch
    {
        Kind.Image => "",
        Kind.Archive => "",
        Kind.Audio => "",
        Kind.Video => "",
        Kind.Calendar => "",
        Kind.Mail => "",
        _ => "",
    };
}
