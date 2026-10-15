namespace Missiva;

/// <summary>
/// La chiave pubblica con cui Missiva controlla la firma di un aggiornamento (Updates; 15 ottobre 2026, richiesta
/// dell'autore). È la metà pubblica della chiave di rilascio dell'autore (ECDSA P-256), in base64, nel formato
/// SubjectPublicKeyInfo: la scrive qui privato\chiave-rilascio.ps1, una volta sola, quando crea la chiave. La metà privata
/// non sta in nessun repository: è sul PC dell'autore, cifrata con una sua password. Finché questa è vuota gli
/// aggiornamenti sono spenti anche nei rilasci, e Impostazioni, Generali, Informazioni lo dice.
/// </summary>
static class UpdateKey
{
    internal const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEa5/9Pe88f+UwAIKpratqKCamDVjp5HteMnnfyFtOWuyCYl8UdLPOBjjaiyUaOgLZ17E+8MY9IYQ25Cxku0dX1Q==";
}
