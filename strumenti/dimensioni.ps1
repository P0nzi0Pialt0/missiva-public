param([int]$Width = 1320, [int]$Height = 820, [string]$Title = "Missiva", [switch]$Minimize, [switch]$Maximize, [switch]$Restore)
# Ridimensiona una finestra della versione di prova a Width x Height DIP (pixel indipendenti dalla scala dello
# schermo, come le misure di WPF), per provare la disposizione a varie larghezze. Poi foto.ps1 la fotografa.
# Senza parametri riporta la finestra principale alla dimensione predefinita (1320x820). -Minimize la riduce a icona,
# -Maximize la ingrandisce, -Restore la riporta com'era (per le misure della memoria da ridotta a icona).
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class R {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
}
"@
[R]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null # per monitor v2: coordinate in pixel fisici
$pids = @(Get-Process Missiva -ErrorAction Stop | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | ForEach-Object Id)
if ($pids.Count -eq 0) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$script:found = [IntPtr]::Zero
[R]::EnumWindows({ param($h, $l)
  $p = 0; [R]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  if ($pids -contains $p -and [R]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 256; [R]::GetWindowText($h, $sb, 256) | Out-Null
    if ($sb.ToString() -eq $Title -and $script:found -eq [IntPtr]::Zero) { $script:found = $h }
  }
  $true }, [IntPtr]::Zero) | Out-Null
if ($script:found -eq [IntPtr]::Zero) { throw "finestra '$Title' non trovata" }
$h = $script:found
if ($Minimize) { [R]::ShowWindow($h, 6) | Out-Null; "finestra '$Title' ridotta a icona"; return } # SW_MINIMIZE
if ($Maximize) { [R]::ShowWindow($h, 3) | Out-Null; "finestra '$Title' ingrandita"; return }    # SW_MAXIMIZE
if ($Restore) { [R]::ShowWindow($h, 9) | Out-Null; "finestra '$Title' ripristinata"; return }    # SW_RESTORE
if ([R]::IsZoomed($h)) { [R]::ShowWindow($h, 9) | Out-Null } # SW_RESTORE: una finestra ingrandita non si ridimensiona
$scale = [R]::GetDpiForWindow($h) / 96.0
[R]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, [int]($Width * $scale), [int]($Height * $scale), 0x0016) | Out-Null # NOMOVE, NOZORDER, NOACTIVATE
Start-Sleep -Milliseconds 400
$r = New-Object R+RECT; [R]::GetWindowRect($h, [ref]$r) | Out-Null
"finestra '$Title': $([math]::Round(($r.R - $r.L) / $scale))x$([math]::Round(($r.B - $r.T) / $scale)) DIP ($($r.R - $r.L)x$($r.B - $r.T) pixel, scala $($scale * 100)%)"
