using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Missiva;

/// <summary>
/// I PDF con il motore di Windows (Windows.Data.Pdf): solo il disegno delle pagine, come immagini PNG. Non esegue
/// JavaScript, azioni di apertura, link o moduli: non li conosce. Si usa solo nel processo dei PDF (PdfWorker).
/// </summary>
public static class PdfRender
{
    // Limiti sulle risposte del processo dei PDF, da una parte e dall'altra (audit, blocco 10, S-12): il processo li rispetta
    // quando disegna, Missiva li controlla su quello che riceve, perché il processo legge file che vengono da chiunque.
    /// <summary>Le pagine di un PDF in anteprima: oltre, un segnaposto per pagina peserebbe sull'interfaccia.</summary>
    public const int MaxPages = 5000;
    /// <summary>La larghezza massima di una pagina o di un'immagine in uscita (la stessa del tetto delle richieste).</summary>
    public const int MaxPixelWidth = 4000;
    /// <summary>L'altezza massima, e i pixel totali (160 MB di bitmap): una pagina molto alta si disegna più stretta, con le sue proporzioni.</summary>
    public const int MaxPixelHeight = 16384;
    public const long MaxPixels = 40_000_000;
    /// <summary>I pixel di un'immagine come arriva: oltre non si decodifica nemmeno rimpicciolita.</summary>
    public const long MaxSourcePixels = 150_000_000;
    /// <summary>I byte di un PNG in risposta (prima 200 MB) e i caratteri di un messaggio d'errore.</summary>
    public const int MaxPngBytes = 100_000_000, MaxMessage = 1024;

    public static async Task<(PdfDocument Document, InMemoryRandomAccessStream Stream)> LoadAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var document = await PdfDocument.LoadFromStreamAsync(stream);
            if (document.PageCount == 0) throw new InvalidDataException("nessuna pagina");
            if (document.PageCount > MaxPages) throw new InvalidDataException($"troppe pagine per l'anteprima ({document.PageCount}, al massimo {MaxPages})");
            return (document, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Quanti pixel fa un DIP per il motore: DestinationWidth è in DIP, e il motore la moltiplica per la scala dello
    /// schermo. Prima gli si passavano i pixel, e a 200% una pagina chiesta larga 1470 pixel arrivava larga 2940: quattro
    /// volte la memoria della bitmap in Missiva. Dalla scala di Windows, corretta con la prima pagina disegnata.
    /// </summary>
    static double scale;

    /// <summary>Una pagina disegnata larga tanti pixel, come PNG.</summary>
    public static async Task<byte[]> PageAsync(PdfDocument document, int index, uint pixels)
    {
        pixels = Math.Clamp(pixels, 16u, (uint)MaxPixelWidth);
        // Una pagina molto alta e stretta (uno scontrino lungo, una pagina web intera) si disegna più stretta, con le sue
        // proporzioni: al massimo MaxPixelHeight di altezza e MaxPixels in tutto.
        using (var page = document.GetPage((uint)index))
        {
            var size = page.Size;
            if (size.Width > 0 && size.Height > 0)
            {
                double ratio = size.Height / size.Width, limit = Math.Floor(Math.Min(MaxPixelHeight / ratio, Math.Sqrt(MaxPixels / ratio)));
                if (limit < 16) throw new InvalidDataException("pagina fuori misura");
                pixels = (uint)Math.Min(pixels, limit);
            }
        }
        if (scale <= 0) scale = Math.Max(1, GetDpiForSystem()) / 96.0;
        uint dips = (uint)Math.Max(1, Math.Round(pixels / scale));
        var png = await RenderAsync(document, index, dips);
        int width = PngWidth(png);
        if (width > 0 && Math.Abs(width - (int)pixels) > 2)
        {
            scale = width / (double)dips; // la scala vera del motore: si ridisegna una volta
            png = await RenderAsync(document, index, (uint)Math.Max(1, Math.Round(pixels / scale)));
        }
        return png;
    }

    static async Task<byte[]> RenderAsync(PdfDocument document, int index, uint dips)
    {
        using var page = document.GetPage((uint)index);
        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = dips });
        output.Seek(0);
        using var png = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(png);
        return png.ToArray();
    }

    /// <summary>La larghezza di un PNG, dall'intestazione IHDR; 0 se non si legge.</summary>
    internal static int PngWidth(byte[] png) => png.Length > 24 ? png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19] : 0;

    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Se i byte sono un PNG di una misura che Missiva decodifica: la firma, la prima parte (IHDR) al suo posto, larghezza e
    /// altezza dentro i limiti (con qualche pixel di tolleranza per gli arrotondamenti del motore) e pixel totali dentro il
    /// tetto. Si guarda prima di darlo a PngBitmapDecoder: un'intestazione enorme costerebbe gigabyte (audit, S-12).
    /// </summary>
    internal static bool IsAcceptablePng(byte[] png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(PngSignature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4)) != 13) return false;
        width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return width is > 0 and <= MaxPixelWidth + 16 && height is > 0 and <= MaxPixelHeight + 16 && (long)width * height <= MaxPixels + MaxPixels / 10;
    }

    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    /// <summary>Perché il PDF non si mostra.</summary>
    public static string Explain(Exception ex) =>
        ex is Sandbox.SandboxException ? "L'anteprima protetta non si è potuta avviare su questo PC, e senza protezione Missiva non apre il PDF: " +
                                         "aprilo con il programma di Windows (Apri) o scaricalo."
        : ex is TimeoutException ? "Il PDF non si è disegnato in tempo: forse è rovinato. Puoi scaricarlo e aprirlo con un altro programma."
        : ex.Message.Contains("troppo grande") ? "Il PDF è troppo grande per l'anteprima (oltre 100 MB): scaricalo o aprilo con il programma di Windows."
        : ex.Message.Contains("troppe pagine") ? $"Il PDF ha troppe pagine per l'anteprima (oltre {MaxPages}): scaricalo o aprilo con il programma di Windows."
        : (uint)ex.HResult == 0x8007052B || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "Il PDF è protetto da una password: aprilo con il programma di Windows (Apri)."
            : "Il PDF è rovinato o incompleto, e non si può mostrare. Puoi scaricarlo e provare con un altro programma.";
}

/// <summary>
/// Le immagini dell'anteprima con i decodificatori di Windows (Windows.Graphics.Imaging), nel processo dell'anteprima come
/// i PDF: il decodificatore del tipo letto dai byte (PNG, JPEG, GIF, BMP; mai la scelta automatica fra tutti i codec
/// installati), le foto grandi rimpicciolite, e in uscita un PNG. Prima le decodificava WIC dentro Missiva.
/// </summary>
public static class ImageRender
{
    /// <summary>Il decodificatore di Windows per la firma dei byte, o null se non è un'immagine che l'anteprima mostra.</summary>
    static Guid? DecoderOf(byte[] bytes) => bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => BitmapDecoder.PngDecoderId,
        [0xFF, 0xD8, 0xFF, ..] => BitmapDecoder.JpegDecoderId,
        [0x47, 0x49, 0x46, 0x38, ..] => BitmapDecoder.GifDecoderId,
        [0x42, 0x4D, ..] => BitmapDecoder.BmpDecoderId,
        _ => null,
    };

    /// <summary>L'immagine (la prima immagine di una GIF) come PNG, larga al massimo maxWidth pixel.</summary>
    public static async Task<byte[]> PngAsync(byte[] bytes, int maxWidth)
    {
        var codec = DecoderOf(bytes) ?? throw new InvalidDataException("non è un'immagine PNG, JPEG, GIF o BMP");
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(codec, input);
        var transform = new BitmapTransform();
        // Un tetto anche sull'altezza e sui pixel totali, non solo sulla larghezza (audit, blocco 10, S-12): un'immagine
        // 1440 x 65535 arrivava intera (377 MB); oltre MaxSourcePixels non si decodifica nemmeno rimpicciolita.
        double width = decoder.PixelWidth, height = decoder.PixelHeight;
        if (width * height > PdfRender.MaxSourcePixels) throw new InvalidDataException("immagine troppo grande");
        double scale = Math.Min(Math.Min(1.0, maxWidth / width), Math.Min(PdfRender.MaxPixelHeight / height, Math.Sqrt(PdfRender.MaxPixels / (width * height))));
        if (scale < 1)
        {
            transform.ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                                                                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        output.Seek(0);
        using var png = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(png);
        return png.ToArray();
    }
}

/// <summary>
/// Il motore dei PDF (e i decodificatori delle immagini dell'anteprima) in un processo a parte (Missiva.exe
/// --pdf-worker), come fanno i browser. Il PDF viene da chiunque: si legge fuori da Missiva, in un AppContainer senza
/// capability (Sandbox: niente rete, niente file dell'utente, niente token), in un processo che non può avviarne altri
/// (job di Windows con un solo processo attivo) e che muore con Missiva (il job si chiude con lei). Se l'AppContainer non
/// si crea, nessun ripiego senza protezione: l'anteprima lo dice e resta Apri. E la memoria: nel processo di Missiva la
/// prima anteprima costava 130-160 MB privati in più (Direct2D e Direct3D del motore di Windows), che restavano fino alla
/// chiusura; il processo dei PDF invece si chiude da solo dopo un minuto senza richieste, e se serve di nuovo riparte e
/// ricarica il documento. Un documento alla volta (quello dell'anteprima), richieste una alla volta, ognuna al massimo 15 s.
/// </summary>
public sealed class PdfWorker : IDisposable
{
    public static PdfWorker Shared { get; } = new();

    /// <summary>Oltre non si apre in anteprima.</summary>
    public const int MaxSize = 100 * 1024 * 1024;
    /// <summary>Quanto può durare una richiesta, con l'avvio del processo (per le prove si accorcia).</summary>
    internal TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Senza richieste per tanto il processo si chiude (per le prove si accorcia).</summary>
    internal TimeSpan IdleTime { get; set; } = TimeSpan.FromMinutes(1);

    readonly SemaphoreSlim gate = new(1, 1);
    Process? process;
    /// <summary>L'handle del processo avuto da CreateProcess, tenuto fino a Stop (il numero del processo non passa a un altro).</summary>
    IntPtr processHandle;
    BinaryReader? reader;
    BinaryWriter? writer;
    IntPtr job;
    Timer? idle;
    /// <summary>Il documento dell'anteprima, per ricaricarlo se il processo si è chiuso intanto.</summary>
    byte[]? document;
    bool documentLoaded;

    /// <summary>Un errore del processo dei PDF, con il codice di Windows (per Explain: PDF protetto da password).</summary>
    sealed class WorkerException : Exception
    {
        public WorkerException(string message, int code) : base(message) => HResult = code;
    }

    /// <summary>Il processo ha risposto fuori dal protocollo o dai limiti di Missiva (audit, S-12): si chiude, ne parte un altro alla richiesta dopo.</summary>
    internal sealed class WorkerProtocolException(string message) : Exception(message);

    /// <summary>Il PDF nel processo: pagine e misura della prima (in punti, 1/72 di pollice).</summary>
    public Task<(int Pages, double Width, double Height)> LoadAsync(byte[] pdf)
    {
        if (pdf.Length == 0) throw new InvalidDataException("PDF vuoto");
        if (pdf.Length > MaxSize) throw new InvalidDataException("PDF troppo grande");
        return RunAsync(() =>
        {
            document = pdf;
            documentLoaded = false;
            return Load(pdf);
        });
    }

    /// <summary>
    /// Una pagina del PDF caricato, larga tanti pixel, come PNG. Con cancel superato (la pagina è uscita dalla vista, lo zoom
    /// è cambiato) la richiesta esce dalla coda senza disegnare nulla.
    /// </summary>
    public Task<byte[]> RenderAsync(int page, int pixels, CancellationToken cancel = default) => RunAsync(() =>
    {
        if (!documentLoaded)
        {
            if (document == null) throw new InvalidOperationException("nessun PDF caricato");
            Load(document); // il processo si era chiuso: si ricarica lo stesso documento
        }
        writer!.Write((byte)'R');
        writer.Write(page);
        writer.Write(pixels);
        writer.Flush();
        Expect('P');
        return ReadPng(reader!);
    }, cancel);

    /// <summary>
    /// Il PNG della risposta: al massimo MaxPngBytes (prima 200 MB), con la firma e un'intestazione dentro le misure di
    /// Missiva (audit, blocco 10, S-12). Il processo legge file di chiunque: una risposta fuori misura non si decodifica, e il
    /// processo, che non ha rispettato il protocollo, si chiude.
    /// </summary>
    internal static byte[] ReadPng(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is <= 0 or > PdfRender.MaxPngBytes) throw new WorkerProtocolException($"risposta non valida dal processo dei PDF (lunghezza {length})");
        var png = reader.ReadBytes(length);
        if (png.Length != length) throw new EndOfStreamException();
        if (!PdfRender.IsAcceptablePng(png, out var width, out var height)) throw new WorkerProtocolException($"risposta non valida dal processo dei PDF (PNG {width} x {height})");
        return png;
    }

    /// <summary>
    /// Un'immagine dell'anteprima (PNG, JPEG, GIF, BMP) decodificata nel processo, come PNG largo al massimo maxWidth
    /// pixel: in Missiva arriva solo un PNG fatto da Windows. Il documento PDF caricato resta com'è.
    /// </summary>
    public Task<byte[]> ImageAsync(byte[] image, int maxWidth, CancellationToken cancel = default)
    {
        if (image.Length == 0) throw new InvalidDataException("immagine vuota");
        if (image.Length > MaxSize) throw new InvalidDataException("immagine troppo grande");
        return RunAsync(() =>
        {
            writer!.Write((byte)'I');
            writer.Write(image.Length);
            writer.Write(image);
            writer.Write(Math.Clamp(maxWidth, 16, PdfRender.MaxPixelWidth));
            writer.Flush();
            Expect('P');
            return ReadPng(reader!);
        }, cancel);
    }

#if DEBUG
    /// <summary>Prove: che cosa riesce a fare il processo dal suo AppContainer (leggere un file dell'utente, collegarsi al PC stesso).</summary>
    internal Task<string> TestReachAsync(string path, int port, string variable = "") => RunAsync(() =>
    {
        writer!.Write((byte)'T');
        writer.Write(path);
        writer.Write(port);
        writer.Write(variable);
        writer.Flush();
        Expect('T');
        return reader!.ReadString();
    });

    /// <summary>Prove: una richiesta scritta dalla prova, con il tubo verso il processo e quello di ritorno.</summary>
    internal Task<T> TestRequestAsync<T>(Func<BinaryWriter, BinaryReader, T> request) => RunAsync(() => request(writer!, reader!));
#endif

    /// <summary>L'anteprima è chiusa: il documento non serve più, e il processo si chiude presto.</summary>
    public void Forget()
    {
        document = null;
        idle?.Change(TimeSpan.FromSeconds(20), System.Threading.Timeout.InfiniteTimeSpan);
    }

    (int, double, double) Load(byte[] pdf)
    {
        writer!.Write((byte)'L');
        writer.Write(pdf.Length);
        writer.Write(pdf);
        writer.Flush();
        Expect('O');
        var result = ReadLoaded(reader!);
        documentLoaded = true;
        return result;
    }

    /// <summary>
    /// Le pagine e la misura della prima, come le dice il processo: dentro limiti sensati (audit, S-12), non 2^31-1 pagine
    /// (un segnaposto ciascuna sull'interfaccia) né una misura che non è un numero.
    /// </summary>
    internal static (int, double, double) ReadLoaded(BinaryReader reader)
    {
        var result = (reader.ReadInt32(), reader.ReadDouble(), reader.ReadDouble());
        if (result.Item1 is < 1 or > PdfRender.MaxPages || !double.IsFinite(result.Item2) || !double.IsFinite(result.Item3)
            || result.Item2 is <= 0 or > 200_000 || result.Item3 is <= 0 or > 200_000)
            throw new WorkerProtocolException($"risposta non valida dal processo dei PDF ({result.Item1} pagine)");
        return result;
    }

    void Expect(char answer) => Expect(reader!, answer);

    internal static void Expect(BinaryReader reader, char answer)
    {
        var got = reader.ReadByte();
        if (got == answer) return;
        if (got == 'E')
        {
            var code = reader.ReadInt32();
            throw new WorkerException(ReadMessage(reader), code);
        }
        throw new WorkerProtocolException("risposta non valida dal processo dei PDF");
    }

    /// <summary>
    /// Il messaggio d'errore del processo: al massimo MaxMessage caratteri, senza a capo né caratteri di controllo (audit, S-12:
    /// ReadString non aveva limite). Il messaggio finisce nel registro e a schermo.
    /// </summary>
    internal static string ReadMessage(BinaryReader reader)
    {
        int length = reader.Read7BitEncodedInt();
        if (length is < 0 or > 4 * PdfRender.MaxMessage) throw new WorkerProtocolException("risposta non valida dal processo dei PDF (messaggio troppo lungo)");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Clean(System.Text.Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Un messaggio d'errore come lo mostra Missiva: senza caratteri di controllo e di al più MaxMessage caratteri.</summary>
    internal static string Clean(string message)
    {
        var text = new string(message.Where(c => !char.IsControl(c)).ToArray());
        return text.Length > PdfRender.MaxMessage ? text[..PdfRender.MaxMessage] : text;
    }

    async Task<T> RunAsync<T>(Func<T> request, CancellationToken cancel = default)
    {
        await gate.WaitAsync(cancel);
        try
        {
            cancel.ThrowIfCancellationRequested(); // superata mentre aspettava il suo turno
            // L'avvio del processo insieme alla richiesta, su un thread del pool: prima girava sul thread di chi chiede, quello
            // dell'interfaccia, a ogni prima anteprima (42-56 ms su questo PC; audit, v2 V-29). Anche la chiusura di un processo
            // fermo o caduto, che lo aspetta fino a 2,5 s. Una richiesta alla volta (gate): Start e Stop non si incrociano.
            var work = Task.Run(() =>
            {
                Start();
                return request();
            });
            if (await Task.WhenAny(work, Task.Delay(RequestTimeout)) != work)
            {
                _ = work.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Log.Write($"processo dei PDF fermo da {RequestTimeout.TotalSeconds:0.#} s: chiuso");
                await Task.Run(Stop);
                // La richiesta fermata gira ancora sul suo thread (dentro Start, o in una lettura che la chiusura del processo ha
                // appena sbloccato) e tocca gli stessi campi della prossima: prima di lasciarle il posto la si aspetta, al più
                // cinque secondi. Prima il posto si liberava subito, con lei ancora viva (audit dell'11 ottobre, C-29).
                try { await work.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                throw new TimeoutException();
            }
            try
            {
                return await work;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException or WorkerProtocolException)
            {
                Log.Write((ex is WorkerProtocolException ? "processo dei PDF chiuso, risposta fuori dai limiti: " : "processo dei PDF caduto: ") + ex.Message);
                await Task.Run(Stop); // alla prossima richiesta ne parte un altro
                throw new InvalidDataException("processo dei PDF caduto");
            }
        }
        finally
        {
            idle?.Change(IdleTime, System.Threading.Timeout.InfiniteTimeSpan);
            gate.Release();
        }
    }

    void Start()
    {
        if (process is { HasExited: false }) return;
        Stop();
        var clock = Stopwatch.StartNew();
        Sandbox.Started started;
        try
        {
            started = Sandbox.Start("--pdf-worker");
        }
        catch (Sandbox.SandboxException ex)
        {
            Log.Write(ex.Message);
            throw;
        }
        try
        {
            job = Confine(started.Handle); // prima che esegua qualcosa: parte sospeso
        }
        catch (Sandbox.SandboxException ex)
        {
            // Senza il job (un solo processo, chiuso con Missiva) il processo non parte: si termina, e l'anteprima lo dice
            // (audit, blocco 10, S-13; prima si andava avanti senza i limiti).
            Log.Write(ex.Message);
            Sandbox.Abort(started);
            throw;
        }
        process = started.Process;
        processHandle = started.Handle;
        Sandbox.Resume(started);
        writer = new BinaryWriter(started.Input);
        reader = new BinaryReader(started.Output);
        documentLoaded = false;
        idle ??= new Timer(_ => Idle());
        Log.Time($"processo dei PDF avviato nel suo AppContainer ({process.Id}, {(Thread.CurrentThread.IsThreadPoolThread ? "in sottofondo" : "sul thread di chi chiede")})", clock);
    }

    void Idle()
    {
        if (!gate.Wait(0)) return; // una richiesta in corso: alla fine il conto riparte
        try
        {
            if (process != null) Log.Write("processo dei PDF chiuso: nessuna richiesta");
            Stop();
        }
        finally
        {
            gate.Release();
        }
    }

    void Stop()
    {
        try
        {
            if (process is { HasExited: false })
            {
                writer?.Write((byte)'Q');
                writer?.Flush();
                process.WaitForExit(500);
            }
        }
        catch (Exception)
        {
            // già caduto: ci pensa il job
        }
        if (job != IntPtr.Zero) CloseHandle(job); // KILL_ON_JOB_CLOSE: se è ancora vivo, muore qui
        job = IntPtr.Zero;
        try { process?.WaitForExit(2000); } catch (Exception) { }
        process?.Dispose();
        process = null;
        Sandbox.Close(processHandle);
        processHandle = IntPtr.Zero;
        try { writer?.BaseStream.Dispose(); } catch (Exception) { } // il tubo verso un processo già chiuso
        try { reader?.BaseStream.Dispose(); } catch (Exception) { }
        reader = null;
        writer = null;
        documentLoaded = false;
    }

    public void Dispose()
    {
        gate.Wait();
        try { Stop(); }
        finally { gate.Release(); }
        idle?.Dispose();
    }

#if DEBUG || PROVA
    /// <summary>
    /// Prove (Missiva.exe --prova-anteprima): un PDF (la prima pagina a 800 pixel) o un'immagine nel processo
    /// dell'anteprima, con l'eseguibile com'è; nel risultato l'AppContainer, il job, la larghezza e i tempi.
    /// </summary>
    internal static int TestFile(string file, string output)
    {
        var lines = new List<string>();
        using var worker = new PdfWorker();
        try
        {
            var clock = Stopwatch.StartNew();
            var bytes = File.ReadAllBytes(file);
            byte[] png;
            if (bytes.AsSpan().StartsWith("%PDF"u8))
            {
                var (pages, _, _) = worker.LoadAsync(bytes).GetAwaiter().GetResult();
                png = worker.RenderAsync(0, 800).GetAwaiter().GetResult();
                lines.Add($"PDF di {pages} pagine");
            }
            else png = worker.ImageAsync(bytes, 1440).GetAwaiter().GetResult();
            lines.Add($"PNG largo {PdfRender.PngWidth(png)} pixel in {clock.ElapsedMilliseconds} ms");
            lines.Add($"AppContainer: {(worker.TestInAppContainer() ? "sì" : "no")}; job: {(worker.TestInJob() ? "sì" : "no")}");
            File.WriteAllLines(output, lines);
            return worker.TestInAppContainer() ? 0 : 1;
        }
        catch (Exception ex)
        {
            lines.Add("ERRORE: " + ex);
            File.WriteAllLines(output, lines);
            return 1;
        }
    }
#endif

#if DEBUG
    /// <summary>Prove (audit, blocco 10, S-13): il job non si crea, e il processo avviato sospeso deve essere terminato.</summary>
    internal static bool TestFailJob;

    /// <summary>Prove: i limiti che il job ha davvero (letti da Windows): bandiere, processi attivi, memoria, limiti dell'interfaccia.</summary>
    internal (uint Flags, uint ActiveProcesses, ulong Memory, uint Ui)? TestJobLimits()
    {
        if (job == IntPtr.Zero) return null;
        var extended = Marshal.AllocHGlobal(Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        var ui = Marshal.AllocHGlobal(4);
        try
        {
            if (!QueryInformationJobObject(job, 9, extended, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _) || !QueryInformationJobObject(job, 4, ui, 4, out _)) return null;
            var limits = Marshal.PtrToStructure<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(extended);
            return (limits.BasicLimitInformation.LimitFlags, limits.BasicLimitInformation.ActiveProcessLimit, (ulong)limits.ProcessMemoryLimit, (uint)Marshal.ReadInt32(ui));
        }
        finally
        {
            Marshal.FreeHGlobal(extended);
            Marshal.FreeHGlobal(ui);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, out uint returned);
#endif

    /// <summary>Per le prove: il processo in corso, e se sta nel suo job.</summary>
    internal Process? TestProcess => process;
    internal bool TestInJob() => process is { } p && job != IntPtr.Zero && IsProcessInJob(p.Handle, job, out bool inside) && inside;
    internal bool TestInAppContainer() => process is { } p && Sandbox.InAppContainer(p);

    // ---------------------------------------------------------------- il job: un solo processo, che muore con Missiva

    /// <summary>
    /// Un job di Windows per il processo dei PDF: nessun processo figlio (al massimo un processo attivo, lui) e chiusura
    /// quando si chiude il job, cioè con Stop o con Missiva, anche se Missiva cade.
    /// </summary>
    static IntPtr Confine(IntPtr worker)
    {
#if DEBUG
        if (TestFailJob) throw new Sandbox.SandboxException("anteprima protetta non avviata (job non creato: prova)");
#endif
        // Tutto obbligatorio (audit, blocco 10, S-13): se un passo non riesce il job si chiude e Start termina il processo.
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Sandbox.SandboxException($"anteprima protetta non avviata (job non creato: errore {Marshal.GetLastWin32Error()})");
        try
        {
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION
                                 | JOB_OBJECT_LIMIT_PROCESS_MEMORY,
                    ActiveProcessLimit = 1,
                },
                ProcessMemoryLimit = (UIntPtr)MemoryLimit,
            };
            Set(handle, 9 /* JobObjectExtendedLimitInformation */, limits, "limiti");
            Set(handle, 4 /* JobObjectBasicUIRestrictions */, new JOBOBJECT_BASIC_UI_RESTRICTIONS { UIRestrictionsClass = UIRestrictions }, "limiti dell'interfaccia");
            if (!AssignProcessToJobObject(handle, worker)) throw new Sandbox.SandboxException($"anteprima protetta non avviata (processo fuori dal job: errore {Marshal.GetLastWin32Error()})");
            return handle;
        }
        catch
        {
            CloseHandle(handle);
            throw;
        }

        static void Set<T>(IntPtr job, int infoClass, T value, string what) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                if (!SetInformationJobObject(job, infoClass, buffer, (uint)size))
                    throw new Sandbox.SandboxException($"anteprima protetta non avviata (job senza {what}: errore {Marshal.GetLastWin32Error()})");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8, JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x400, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000,
                      JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100;

    /// <summary>
    /// La memoria che il processo può impegnare: 3 GB, il triplo del più grande lavoro che i tetti di PdfRender permettono
    /// (una bitmap da 160 MB più le copie del motore e del PNG; una pagina 4000 x 5657 ne usa circa 500 MB). Oltre, il processo
    /// non ha più memoria e si ferma: l'anteprima dice che il PDF è rovinato, e Missiva non soffre.
    /// </summary>
    const ulong MemoryLimit = 3UL * 1024 * 1024 * 1024;

    /// <summary>
    /// Il processo non tocca l'interfaccia: JOB_OBJECT_UILIMIT_HANDLES (nessun oggetto USER di altri processi: non la finestra
    /// del desktop, quindi niente schermo), appunti in lettura e scrittura, parametri di sistema, impostazioni dello schermo,
    /// atomi globali, desktop, uscita da Windows.
    /// </summary>
    const uint UIRestrictions = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20 | 0x40 | 0x80;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_UI_RESTRICTIONS { public uint UIRestrictionsClass; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    // ---------------------------------------------------------------- il processo dei PDF

    /// <summary>
    /// Missiva.exe --pdf-worker: legge le richieste dallo standard input e risponde sullo standard output. L (lunghezza e
    /// byte) carica il documento, R (pagina, pixel) disegna una pagina, Q chiude. Risposte: O (pagine, larghezza e altezza
    /// della prima), P (lunghezza e PNG), E (codice e messaggio d'errore). Il documento vive solo qui. Senza Missiva
    /// (standard input chiuso) esce. Le chiamate a Windows.Data.Pdf girano su thread MTA (Task.Run): il thread di Main è STA.
    /// </summary>
    public static int Serve()
    {
        var reader = new BinaryReader(Console.OpenStandardInput());
        var writer = new BinaryWriter(Console.OpenStandardOutput());
        PdfDocument? document = null;
        InMemoryRandomAccessStream? stream = null;
        while (true)
        {
            int command;
            try { command = reader.ReadByte(); }
            catch (Exception) { return 0; } // Missiva chiusa
            try
            {
                switch (command)
                {
                    case 'L':
                        int length = reader.ReadInt32();
                        if (length is <= 0 or > MaxSize) return 1;
                        var bytes = reader.ReadBytes(length);
                        if (bytes.Length != length) return 0;
                        stream?.Dispose();
                        stream = null;
                        document = null;
                        var (loaded, memory) = Task.Run(() => PdfRender.LoadAsync(bytes)).GetAwaiter().GetResult();
                        (document, stream) = (loaded, memory);
                        var size = Task.Run(() => { using var first = loaded.GetPage(0); return first.Size; }).GetAwaiter().GetResult();
                        writer.Write((byte)'O');
                        writer.Write((int)loaded.PageCount);
                        writer.Write((double)size.Width);
                        writer.Write((double)size.Height);
                        break;
                    case 'R':
                        int page = reader.ReadInt32(), pixels = reader.ReadInt32();
                        if (document is not { } current || page < 0 || page >= current.PageCount) throw new InvalidDataException("pagina non valida");
                        var png = Task.Run(() => PdfRender.PageAsync(current, page, (uint)Math.Clamp(pixels, 16, 4000))).GetAwaiter().GetResult();
                        writer.Write((byte)'P');
                        writer.Write(png.Length);
                        writer.Write(png);
                        break;
                    case 'I':
                        int imageLength = reader.ReadInt32();
                        if (imageLength is <= 0 or > MaxSize) return 1;
                        var image = reader.ReadBytes(imageLength);
                        int maxWidth = reader.ReadInt32();
                        if (image.Length != imageLength) return 0;
                        var picture = Task.Run(() => ImageRender.PngAsync(image, Math.Clamp(maxWidth, 16, 4000))).GetAwaiter().GetResult();
                        writer.Write((byte)'P');
                        writer.Write(picture.Length);
                        writer.Write(picture);
                        break;
#if DEBUG
                    case 'T':
                        writer.Write((byte)'T');
                        writer.Write(Reach(reader.ReadString(), reader.ReadInt32(), reader.ReadString()));
                        break;
#endif
                    case 'Q':
                        return 0;
                    default:
                        return 1;
                }
            }
            catch (Exception ex) when (ex is not IOException and not EndOfStreamException)
            {
                writer.Write((byte)'E');
                writer.Write(ex.HResult);
                writer.Write(Clean(ex.Message));
            }
            catch (Exception)
            {
                return 0; // Missiva chiusa a metà richiesta
            }
            try { writer.Flush(); }
            catch (Exception) { return 0; }
        }
    }

#if DEBUG
    /// <summary>
    /// Prove, dentro il processo: il file dell'utente si legge? Il PC stesso (127.0.0.1) risponde? Gli appunti si aprono? Lo
    /// schermo si ottiene (GetDC(NULL))? Una variabile d'ambiente di Missiva si vede? Nell'AppContainer, no, no, e (audit,
    /// blocco 10, S-13) la variabile no.
    /// </summary>
    static string Reach(string path, int port, string variable)
    {
        string file, network;
        try { File.ReadAllBytes(path); file = "file letto"; }
        catch (Exception ex) { file = "file non letto (" + ex.GetType().Name + ")"; }
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            network = tcp.ConnectAsync(System.Net.IPAddress.Loopback, port).Wait(3000) ? "collegato" : "non collegato (tempo scaduto)";
        }
        catch (Exception ex) { network = "non collegato (" + (ex.InnerException ?? ex).GetType().Name + ")"; }
        string clipboard, screen;
        if (OpenClipboard(IntPtr.Zero)) { CloseClipboard(); clipboard = "appunti aperti"; }
        else clipboard = "appunti non aperti (" + Marshal.GetLastWin32Error() + ")";
        var dc = GetDC(IntPtr.Zero);
        if (dc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, dc); screen = "GetDC(NULL) ottenuto"; }
        else screen = "GetDC(NULL) no (" + Marshal.GetLastWin32Error() + ")";
        var value = variable.Length > 0 ? Environment.GetEnvironmentVariable(variable) : null;
        return $"{file}; {network}; {clipboard}; {screen}" + (variable.Length > 0 ? $"; variabile {variable}: {(value == null ? "assente" : "presente")}" : "");
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
#endif
}
