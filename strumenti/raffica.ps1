param([Parameter(Mandatory)][int]$Message, [int]$Count = 25, [int]$Interval = 40, [string]$Out = "C:\Progetti\Missiva\prova-dati\raffica")
# Raffica di foto del riquadro di lettura dopo la selezione di un messaggio: seleziona il messaggio -Message
# dell'elenco con UI Automation e fotografa la finestra (PrintWindow) ogni -Interval ms, -Count volte. Salva un
# mosaico (Out_mosaico.png) con i fotogrammi del riquadro di lettura e il tempo di ognuno, per vedere se il riquadro
# cambia una volta sola o passa per stati intermedi (segnaposto, oggetto nuovo sul corpo vecchio, avvisi che spostano).
# Stampa anche le righe "tempi:" del registro scritte nel frattempo.
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class B {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
[B]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$log = "C:\Progetti\Missiva\prova-dati\missiva.log"
$proc = Get-Process Missiva | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | Sort-Object { $_.MainWindowHandle -eq 0 } | Select-Object -First 1
if (-not $proc) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$main = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.AndCondition (
    (New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $proc.Id),
    (New-Object System.Windows.Automation.PropertyCondition $A::NameProperty, "Missiva"))))
$h = [IntPtr]$main.Current.NativeWindowHandle
if ([B]::IsIconic($h)) { [B]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 800 } # ridotta a icona: PrintWindow darebbe fogli bianchi
$list = $main.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::AutomationIdProperty, "MessageList"))
$items = $list.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::ListItem)))
$reader = $main.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::AutomationIdProperty, "Reader"))
$wr = New-Object B+RECT; [B]::GetWindowRect($h, [ref]$wr) | Out-Null
$rr = $reader.Current.BoundingRectangle
$crop = New-Object System.Drawing.Rectangle ([int]($rr.X - $wr.L)), ([int]($rr.Y - $wr.T)), ([int]$rr.Width), ([int]$rr.Height)
$w = $wr.R - $wr.L; $hh = $wr.B - $wr.T

$start = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
$frames = New-Object System.Collections.ArrayList
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$items[$Message].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
for ($i = 0; $i -lt $Count; $i++) {
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    [B]::PrintWindow($h, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose()
    [void]$frames.Add([pscustomobject]@{ Ms = $clock.ElapsedMilliseconds; Bmp = $bmp })
    $wait = ($i + 1) * $Interval - $clock.ElapsedMilliseconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
}

# Mosaico: i fotogrammi del riquadro rimpiccioliti, cinque per riga, con il tempo in alto a sinistra.
$scale = 0.35; $cw = [int]($crop.Width * $scale); $ch = [int]($crop.Height * $scale); $cols = 5
$rows = [math]::Ceiling($frames.Count / $cols)
$mosaic = New-Object System.Drawing.Bitmap ($cw * $cols), ($ch * $rows)
$g = [System.Drawing.Graphics]::FromImage($mosaic)
$font = New-Object System.Drawing.Font "Segoe UI", 14, ([System.Drawing.FontStyle]::Bold)
for ($i = 0; $i -lt $frames.Count; $i++) {
    $dest = New-Object System.Drawing.Rectangle (($i % $cols) * $cw), ([math]::Floor($i / $cols) * $ch), $cw, $ch
    $g.DrawImage($frames[$i].Bmp, $dest, $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.FillRectangle([System.Drawing.Brushes]::Black, $dest.X, $dest.Y, 70, 24)
    $g.DrawString("$($frames[$i].Ms) ms", $font, [System.Drawing.Brushes]::Yellow, $dest.X + 2, $dest.Y)
    $g.DrawRectangle([System.Drawing.Pens]::Gray, $dest)
    $frames[$i].Bmp.Dispose()
}
$g.Dispose(); $mosaic.Save("$($Out)_mosaico.png"); $mosaic.Dispose()
"salvato $($Out)_mosaico.png ($($frames.Count) fotogrammi in $($clock.ElapsedMilliseconds) ms)"
Start-Sleep -Milliseconds 500
if (Test-Path $log) { Get-Content $log | Select-Object -Skip $start | Where-Object { $_ -match "tempi:|ERRORE" } }
