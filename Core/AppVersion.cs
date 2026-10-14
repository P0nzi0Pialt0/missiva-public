using System.Reflection;

namespace Missiva;

/// <summary>
/// La versione di Missiva come la porta l'eseguibile (14 ottobre 2026, richiesta dell'autore). I primi due numeri sono
/// scritti nel progetto (MissivaVersion in Missiva.csproj), il terzo è il numero dei commit del ramo compilato e cresce a
/// ogni modifica; il testo dice anche la data e il commit dell'ultima modifica compresa (target MissivaGitVersion). Fino a
/// quel giorno era 0.3.0 per ogni compilazione, dal 25 settembre 2026: non si capiva quale versione fosse installata.
/// </summary>
public static class AppVersion
{
    static readonly Assembly Self = typeof(AppVersion).Assembly;

    /// <summary>Il numero, come in App installate: "0.3.525".</summary>
    public static string Number => Self.GetName().Version?.ToString(3) ?? "";

    /// <summary>
    /// Il numero con la data e il commit dell'ultima modifica compresa: "0.3.525 del 14/10/2026 (8c444de)", con in più
    /// ", con modifiche non ancora in un commit" se è stata compilata da una cartella che ne aveva. Senza git, il solo numero.
    /// </summary>
    public static string Text => Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } text ? text : Number;
}
