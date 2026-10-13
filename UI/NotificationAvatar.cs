using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva.UI;

/// <summary>
/// L'immagine di un mittente nelle notifiche quando il suo logo non c'è (SenderLogos): il cerchio con le iniziali, dello
/// stesso colore e con le stesse lettere dell'elenco (Avatars). Windows vuole un file: si disegna una volta per colore e
/// iniziali e si tiene in cartella "notifiche" dei dati. Il disegno gira su un thread suo (STA), quindi vale anche dove non
/// c'è un thread dell'interfaccia.
/// </summary>
static class NotificationAvatar
{
    const int Size = 128;

    /// <summary>Il file PNG del cerchio con le iniziali di questo mittente; null se non si riesce a scriverlo.</summary>
    public static string? PathOf(Address person)
    {
        try
        {
            string initials = Avatars.Initials(person);
            var color = Avatars.ColorOf(person);
            string folder = System.IO.Path.Combine(Store.Dir, "notifiche");
            string path = System.IO.Path.Combine(folder, $"{color.R:X2}{color.G:X2}{color.B:X2}-{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(initials))}.png");
            if (System.IO.File.Exists(path)) return path;
            System.IO.Directory.CreateDirectory(folder);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Draw(path, initials, color); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("immagine della notifica", ex);
            return null;
        }
    }

    static void Draw(string path, string initials, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(brush, null, new Point(Size / 2.0, Size / 2.0), Size / 2.0, Size / 2.0);
            var text = new FormattedText(initials, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                initials.Length > 1 ? 50 : 60, Brushes.White, 1.0);
            context.DrawText(text, new Point((Size - text.Width) / 2, (Size - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
}
