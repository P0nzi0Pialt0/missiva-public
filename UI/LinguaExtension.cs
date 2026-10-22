using System.Windows.Markup;

namespace Missiva.UI;

/// <summary>
/// Un testo tradotto nel XAML (Lingua, 21 ottobre 2026): <c>Content="{local:T 'Posta in arrivo'}"</c>, con
/// <c>xmlns:local="clr-namespace:Missiva.UI"</c>, vale <c>Lingua.T("Posta in arrivo")</c>, calcolato quando la finestra si
/// carica, cioè dopo Lingua.Start.
/// </summary>
/// <remarks>
/// Come si scrive:
/// <list type="bullet">
/// <item>Il testo sempre fra apici semplici, dopo uno spazio: <c>{local:T 'Rispondi a tutti'}</c>. Dentro gli apici virgole,
/// due punti, parentesi e parentesi graffe restano come sono.</item>
/// <item>Un apostrofo dentro il testo si scrive <c>\'</c>: <c>ToolTip="{local:T 'Mostra l\'anteprima'}"</c>. Una barra
/// rovesciata si scrive <c>\\</c>.</item>
/// <item>Come in ogni attributo XAML, le virgolette doppie si scrivono <c>&amp;quot;</c> e la e commerciale <c>&amp;amp;</c>.</item>
/// <item>La chiave nel file JSON delle traduzioni è il testo dopo questi escape: per l'esempio sopra "Mostra l'anteprima".</item>
/// <item>Solo testi fissi: niente valori ({0}). Un testo con valori si mette dal codice, con <c>T("...", valore)</c>.</item>
/// <item>Solo la forma con il testo fra apici (non <c>Text=...</c>): è quella che il gruppo <c>lingua</c> della suite e
/// <c>strumenti\lingua-mancanti.ps1</c> riconoscono.</item>
/// </list>
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string text) => Text = text;

    /// <summary>Il testo italiano, che è anche la chiave della traduzione.</summary>
    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Lingua.T(Text);
}
