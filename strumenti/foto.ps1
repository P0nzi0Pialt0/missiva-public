param([string]$Out = "C:\Progetti\Missiva\prova-dati\foto.png", [string]$Title = "", [switch]$Popup)
# Fotografa una finestra della versione di prova (PrintWindow: funziona anche se è coperta). Senza -Title la prima.
# Elenca anche le finestre aperte della prova, utile per trovare dialoghi e menu. -Popup fotografa il menu aperto
# (le finestre senza titolo: menu del tasto destro, menu "..." e simili).
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
}
"@
[W]::SetProcessDPIAware() | Out-Null
$pids = @(Get-Process Missiva -ErrorAction Stop | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | ForEach-Object Id)
if ($pids.Count -eq 0) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$wins = New-Object System.Collections.ArrayList
[W]::EnumWindows({ param($h, $l)
  $p = 0; [W]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  if ($pids -contains $p -and [W]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 256; [W]::GetWindowText($h, $sb, 256) | Out-Null
    [void]$wins.Add([pscustomobject]@{ H = $h; T = $sb.ToString() })
  }
  $true }, [IntPtr]::Zero) | Out-Null
$wins | ForEach-Object { "finestra: '$($_.T)'" }
$w = if ($Popup) { $wins | Where-Object T -eq "" | Select-Object -First 1 } elseif ($Title) { $wins | Where-Object T -like "*$Title*" | Select-Object -First 1 } else { $wins | Where-Object T -eq "Missiva" | Select-Object -First 1 }
if (-not $w) { $w = $wins | Select-Object -First 1 }
$r = New-Object W+RECT; [W]::GetWindowRect($w.H, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[W]::PrintWindow($w.H, $dc, 2) | Out-Null
$g.ReleaseHdc($dc); $g.Dispose(); $bmp.Save($Out); $bmp.Dispose()
"salvato $Out ($($r.R - $r.L)x$($r.B - $r.T))"
