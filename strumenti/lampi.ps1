param([string]$Keys, [string]$Steps, [int]$Ms = 1500, [string]$Out = "C:\Progetti\Missiva\prova-dati\lampi", [switch]$List)
# Fotografa di continuo (PrintWindow) la finestra della prova mentre esegue i passi (righe per MISSIVA_KEYS): tiene solo
# i fotogrammi del riquadro di lettura diversi dal precedente, con il tempo in ms dall'ultimo passo.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Lampi {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
  public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
  public static int W, H;
  // Un HBITMAP della finestra intera, e l'impronta della zona (x, y, w, h) in pixel.
  public static IntPtr Shot(IntPtr h, int x, int y, int w, int hh, out long hash) {
    RECT r; GetWindowRect(h, out r); W = r.R - r.L; H = r.B - r.T;
    var screen = GetDC(IntPtr.Zero); var mem = CreateCompatibleDC(screen); var bmp = CreateCompatibleBitmap(screen, W, H);
    var old = SelectObject(mem, bmp); PrintWindow(h, mem, 2); SelectObject(mem, old);
    var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
    var bits = new byte[W * H * 4]; GetDIBits(mem, bmp, 0, (uint)H, bits, ref bi, 0);
    DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    long s = 17;
    for (int yy = y; yy < Math.Min(H, y + hh); yy += 3) for (int xx = x; xx < Math.Min(W, x + w); xx += 3) s = s * 31 + BitConverter.ToInt32(bits, (yy * W + xx) * 4);
    hash = s; return bmp;
  }
}
"@
[Lampi]::SetProcessDPIAware() | Out-Null
$p = Get-Process Missiva | Where-Object { ($_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*") -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$h = $p.MainWindowHandle
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Out | Out-Null
$hash = 0L
$bmp = [Lampi]::Shot($h, 0, 0, 1, 1, [ref]$hash); [Lampi]::DeleteObject($bmp) | Out-Null
$w = [Lampi]::W; $ht = [Lampi]::H
# Il riquadro di lettura: la parte destra della finestra, sotto la barra.
$ax = [int]($w * 0.56); $ay = [int]($ht * 0.16); $aw = [int]($w * 0.42); $ah = [int]($ht * 0.8)
# -List: l'elenco dei messaggi invece del riquadro (per esempio l'ingresso di una riga nuova).
if ($List) { $ax = [int]($w * 0.23); $aw = [int]($w * 0.33); $ah = [int]($ht * 0.5) }
$area = New-Object System.Drawing.Rectangle $ax, $ay, $aw, $ah
$n = 0
foreach ($step in $Steps.Split(";")) {
  Add-Content $Keys $step
  $clock = [Diagnostics.Stopwatch]::StartNew(); $last = 0L; $times = @()
  while ($clock.ElapsedMilliseconds -lt $Ms) {
    $bmp = [Lampi]::Shot($h, $ax, $ay, $aw, $ah, [ref]$hash); $t = $clock.ElapsedMilliseconds
    $times += $t
    if ($hash -ne $last) {
      $img = [System.Drawing.Image]::FromHbitmap($bmp); $c = $img.Clone($area, $img.PixelFormat)
      $c.Save((Join-Path $Out ("{0:D2}-{1}-{2:D4}ms.png" -f $n, ($step -replace '[^a-z0-9]', ''), $t))); $c.Dispose(); $img.Dispose(); $last = $hash; $n++
    }
    [Lampi]::DeleteObject($bmp) | Out-Null
  }
  $gaps = for ($i = 1; $i -lt $times.Count; $i++) { $times[$i] - $times[$i - 1] }
  "{0}: {1} fotogrammi, ogni {2:N0} ms" -f $step, $times.Count, ($gaps | Measure-Object -Average).Average
}
Get-ChildItem $Out | Select-Object -ExpandProperty Name

