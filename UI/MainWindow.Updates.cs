using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Gli aggiornamenti da GitHub nella finestra (Updates; 15 ottobre 2026, richiesta dell'autore), solo nei rilasci: il
/// controllo due minuti dopo l'avvio e poi una volta al giorno (con la casella delle Impostazioni accesa), o con "Controlla
/// ora". Nessuna finestra da sola: una versione nuova accende la barra in fondo all'elenco ("È disponibile Missiva ... del
/// ...") con Novità, Aggiorna e la X, che la chiude fino al giorno dopo per quella versione. Aggiorna scarica e controlla,
/// prova il nuovo eseguibile, chiede conferma e poi esce come Esci (App.UpdateOnExit), con le domande delle scritture
/// aperte. Un errore del controllo automatico va solo nel registro.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? updateTimer;
    /// <summary>La versione nuova offerta nella barra (manifesto firmato e zip), o null.</summary>
    Updates.Offer? updateOffer;
    /// <summary>Lo scaricamento in corso, da fermare con Annulla; null fuori dallo scaricamento.</summary>
    CancellationTokenSource? updateDownload;
    /// <summary>Aggiorna è in corso, dallo scaricamento alla domanda: un controllo intanto non cambia la barra.</summary>
    bool updating;
    bool updateChecking;

    /// <summary>Com'è andato l'ultimo controllo, con l'ora: lo dice Impostazioni, Generali, Informazioni.</summary>
    internal static string? LastUpdateLine { get; private set; }

    /// <summary>Il controllo automatico: due minuti dopo l'avvio, poi ogni giorno, finché Missiva resta aperta. Solo nei rilasci.</summary>
    internal void StartUpdateChecks()
    {
        if (Updates.Current != Updates.Mode.On) return;
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromDays(1);
            await CheckUpdatesAsync(automatic: true);
        };
        updateTimer.Start();
    }

    /// <summary>
    /// Un controllo: automatico (con la casella spenta nessuna richiesta, e una barra chiusa oggi non torna) o chiesto con
    /// "Controlla ora" (la barra torna anche se era stata chiusa). Il testo dell'esito, per le Impostazioni.
    /// </summary>
    internal async Task<string> CheckUpdatesAsync(bool automatic)
    {
        if (updateChecking) return "Un controllo è già in corso.";
        if (updating) return "Un aggiornamento è già in corso.";
        updateChecking = true;
        try
        {
            var outcome = automatic
                ? await Updates.AutomaticAsync(S.UpdatesAuto, Updates.Repository, UpdateKey.PublicKey, Updates.CurrentNumber, Updates.Http)
                : await Updates.CheckAsync(Updates.Repository, UpdateKey.PublicKey, Updates.CurrentNumber, Updates.Http);
            if (outcome.Kind != Updates.CheckKind.Off || !automatic)
                LastUpdateLine = $"{outcome.Line} (alle {DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)} del {DateTime.Now.ToString("dd'/'MM", CultureInfo.InvariantCulture)})";
            if (outcome.Offer is { } offer && !updating)
            {
                updateOffer = offer;
                if (!automatic || Updates.Shows(offer.Manifest, S.UpdateDismissed, S.UpdateDismissedOn, DateTime.Now)) ShowUpdateOffer();
            }
            return LastUpdateLine ?? outcome.Line;
        }
        catch (Exception ex)
        {
            Log.Error("controllo degli aggiornamenti", ex);
            return "Il controllo non è riuscito: " + ex.Message;
        }
        finally
        {
            updateChecking = false;
        }
    }

    /// <summary>La barra con la versione offerta: Novità (se ci sono), Aggiorna e la X.</summary>
    void ShowUpdateOffer()
    {
        if (updateOffer is not { } offer) return;
        UpdateText.Text = $"È disponibile {offer.Manifest.Title}";
        UpdateNewsButton.Visibility = offer.Manifest.News.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNowButton.Visibility = Visibility.Visible;
        UpdateCancelButton.Visibility = Visibility.Collapsed;
        UpdateCloseButton.Visibility = Visibility.Visible;
        UpdateBar.Visibility = Visibility.Visible;
    }

    /// <summary>All'avvio dopo un aggiornamento non riuscito (Updates.AfterStart): la barra lo dice, con la sola X.</summary>
    internal void ShowUpdateFailure(string notice)
    {
        if (updating) return;
        updateOffer = null;
        UpdateText.Text = notice;
        UpdateNewsButton.Visibility = UpdateNowButton.Visibility = UpdateCancelButton.Visibility = Visibility.Collapsed;
        UpdateCloseButton.Visibility = Visibility.Visible;
        UpdateBar.Visibility = Visibility.Visible;
    }

    /// <summary>La X: la barra si chiude, e per quella versione non torna fino al giorno dopo.</summary>
    void OnUpdateClose(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        UpdateBar.Visibility = Visibility.Collapsed;
        if (updateOffer is not { } offer) return;
        S.UpdateDismissed = offer.Manifest.Number.ToString();
        S.UpdateDismissedOn = DateTime.Today;
        Store.Save();
        Log.Write($"aggiornamenti: barra di {offer.Manifest.Number} chiusa, torna domani");
    }

    void OnUpdateNews(object sender, RoutedEventArgs e)
    {
        if (updateOffer is not { } offer) return;
        Dialog.Show(this, $"Novità di Missiva {offer.Manifest.Number}", UpdateNews(offer.Manifest));
    }

    /// <summary>Le novità di un manifesto, una riga per punto.</summary>
    internal static string UpdateNews(Updates.Manifest manifest) =>
        manifest.News.Count == 0 ? "Il rilascio non dice che cosa cambia." : string.Join("\n", manifest.News.Select(line => "• " + line));

    /// <summary>"Scarico Missiva 0.3.530: 12,4 di 98,0 MB", nella barra durante lo scaricamento.</summary>
    internal static string UpdateProgress(Version number, long done, long total) =>
        $"Scarico Missiva {number}: {done / 1048576.0:0.0} di {total / 1048576.0:0.0} MB";

    /// <summary>
    /// Aggiorna. Nella versione di prova scrive solo che cosa farebbe. Altrimenti scarica e controlla (Updates.DownloadAsync),
    /// prova il nuovo eseguibile, chiede conferma ed esce come Esci; a ogni intoppo la barra lo dice e Missiva resta com'è.
    /// </summary>
    void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (updateOffer is not { } offer || updating) return;
#if DEBUG || PROVA
        Log.Write($"prova: aggiornerei Missiva da {AppVersion.Number} a {offer.Manifest.Number} (scaricherei {offer.Manifest.FileName}, {offer.Manifest.Size} byte, da {offer.ZipUrl.Host})");
#else
        _ = Log.GuardAsync("aggiornamento di Missiva", () => UpdateNowAsync(offer));
#endif
    }

    async Task UpdateNowAsync(Updates.Offer offer)
    {
        var number = offer.Manifest.Number;
        updating = true;
        try
        {
            using var cancel = updateDownload = new CancellationTokenSource();
            UpdateNewsButton.Visibility = UpdateNowButton.Visibility = UpdateCloseButton.Visibility = Visibility.Collapsed;
            UpdateCancelButton.Visibility = Visibility.Visible;
            UpdateText.Text = UpdateProgress(number, 0, offer.Manifest.Size);
            var progress = new Progress<long>(done => { if (updateDownload != null) UpdateText.Text = UpdateProgress(number, done, offer.Manifest.Size); });
            Updates.Prepared prepared;
            try
            {
                prepared = await Updates.DownloadAsync(offer, Updates.CurrentNumber, Path.Combine(Store.Dir, Updates.FolderName), Updates.Http, progress, cancel.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                ShowUpdateOffer();
                return;
            }
            catch (Exception ex)
            {
                ShowUpdateOffer();
                UpdateText.Text = ex is Updates.Refused
                    ? $"Missiva {number} non è stata accettata: {ex.Message}"
                    : $"Lo scaricamento di Missiva {number} non è riuscito: {ex.Message}";
                return;
            }
            finally
            {
                updateDownload = null;
            }
            UpdateCancelButton.Visibility = Visibility.Collapsed;
            UpdateText.Text = $"Provo Missiva {number}…";
            if (await Updates.TryRunAsync(prepared) is { } problem)
            {
                Log.Write($"aggiornamenti: il nuovo eseguibile {number} non parte ({problem}): resta {AppVersion.Number}");
                Updates.Remove(prepared.Folder);
                ShowUpdateOffer();
                UpdateText.Text = $"Missiva {number} è stata scaricata ma su questo PC non parte ({problem}): resta questa.";
                return;
            }
            if (Dialog.Ask(this, $"Aggiornare a Missiva {number}?",
                           "Missiva si chiude e si riapre aggiornata. Se stai scrivendo un messaggio, prima ti chiede se salvare la bozza.", "Aggiorna") != Choice.Primary)
            {
                Updates.Remove(prepared.Folder);
                ShowUpdateOffer();
                return;
            }
            UpdateText.Text = $"Aggiorno a Missiva {number}…";
            App.Current.UpdateOnExit(prepared);
        }
        finally
        {
            updating = false;
        }
    }

    void OnUpdateCancel(object sender, RoutedEventArgs e) => updateDownload?.Cancel();

    /// <summary>L'uscita per l'aggiornamento è stata annullata (Annulla in una domanda di una scrittura): Missiva resta com'è.</summary>
    internal void UpdateCancelled(Updates.Prepared prepared)
    {
        Updates.Remove(prepared.Folder);
        ShowUpdateOffer();
        UpdateText.Text = $"Aggiornamento a Missiva {prepared.Manifest.Number} annullato: resta questa.";
    }
}
