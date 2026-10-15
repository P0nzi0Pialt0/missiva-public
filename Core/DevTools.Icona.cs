#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva;

static partial class DevTools
{
    /// <summary>
    /// L'icona di Missiva dai suoi disegni in Assets\icona (16 ottobre 2026: icona Fluent, la busta viola senza il quadrato
    /// dietro, scelta dall'autore fra tre proposte): missiva.svg per 64 pixel e oltre, missiva-32.svg per 32-48 e
    /// missiva-16.svg per 16-24, perché a quelle misure i dettagli del disegno grande si impastano. Disegnati con
    /// SharpVectors e WPF come i loghi dei mittenti. Escono missiva.ico con le nove misure dell'icona di prima (fino a 64
    /// pixel in BMP a 32 bit, 128 e 256 in PNG), missiva-128.png per la barra del titolo e una PNG per misura da guardare.
    /// Solo Debug: Missiva.exe --icona cartella-dei-disegni cartella-di-uscita (con MISSIVA_DATA, come gli altri strumenti).
    /// </summary>
    static void DrawIcon(string sources, string output)
    {
        Directory.CreateDirectory(output);
        var cache = new Dictionary<string, (DrawingGroup Drawing, double Side)>();
        (DrawingGroup Drawing, double Side) Load(string name)
        {
            if (cache.TryGetValue(name, out var known)) return known;
            var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
            {
                IncludeRuntime = false, TextAsGeometry = true, CanUseBitmap = false,
                ExternalResourcesAccessMode = SharpVectors.Dom.ExternalResourcesAccessModes.Ignore,
            };
            var drawing = new SharpVectors.Converters.FileSvgReader(settings).Read(Path.Combine(sources, name))
                          ?? throw new InvalidOperationException($"{name}: SharpVectors non l'ha letto");
            // Il lato del disegno è quello del viewBox, quadrato: ogni disegno ha un rettangolo trasparente grande quanto la tela,
            // così i limiti del disegno sono la tela e l'icona non si sposta né si allarga.
            var side = Math.Round(Math.Max(drawing.Bounds.Right, drawing.Bounds.Bottom));
            Console.WriteLine($"{name}: limiti {drawing.Bounds}, lato {side}");
            return cache[name] = (drawing, side);
        }

        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = new List<(int Size, byte[] Data)>();
        foreach (var size in sizes)
        {
            var (drawing, side) = Load(size <= 24 ? "missiva-16.svg" : size <= 48 ? "missiva-32.svg" : "missiva.svg");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / side, size / side));
                dc.DrawDrawing(drawing);
                dc.Pop();
            }
            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
            var png = Png(straight);
            File.WriteAllBytes(Path.Combine(output, $"missiva-{size}.png"), png);
            frames.Add((size, size <= 64 ? Dib(straight, size) : png));
        }
        File.WriteAllBytes(Path.Combine(output, "missiva.ico"), Ico(frames));
        Console.WriteLine($"scritti missiva.ico ({frames.Count} misure) e le PNG in {output}");
    }

    static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Un'immagine dell'icona come la vuole il formato ICO per le misure piccole: intestazione BMP, pixel BGRA dal basso, maschera.</summary>
    static byte[] Dib(BitmapSource bitmap, int size)
    {
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        int maskStride = (size + 31) / 32 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((short)1); writer.Write((short)32);
        writer.Write(0); writer.Write(size * size * 4 + maskStride * size); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int y = size - 1; y >= 0; y--) writer.Write(pixels, y * size * 4, size * 4);
        for (int y = size - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (int x = 0; x < size; x++)
                if (pixels[(y * size + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(row);
        }
        return stream.ToArray();
    }

    static byte[] Ico(List<(int Size, byte[] Data)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0); writer.Write((short)1); writer.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
            writer.Write(data.Length); writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) writer.Write(data);
        return stream.ToArray();
    }
}
#endif
