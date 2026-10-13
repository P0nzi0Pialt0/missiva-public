param([switch]$Installed, [switch]$Detail)
# Memoria usata da Missiva: il processo e i processi WebView2 del riquadro di lettura (quelli con la sua cartella
# WebView2). Di default la versione di prova (la Debug, o la finale di prova.ps1 -Release); con -Installed quella
# installata. Per confronto, il nuovo Outlook (olk.exe e i suoi msedgewebview2) di solito supera i 500 MB. -Detail elenca
# i processi WebView2 per tipo.
$exes = if ($Installed) { @(Join-Path $env:LOCALAPPDATA "Programs\Missiva\Missiva.exe") } else {
    @("C:\Progetti\Missiva\Missiva-sorgente\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe", "C:\Progetti\Missiva\prova-release\Missiva.exe") }
$data = if ($Installed) { Join-Path $env:LOCALAPPDATA "Missiva" } else { "C:\Progetti\Missiva\prova-dati" }
$main = Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { $exes -contains $_.Path } | Sort-Object { $_.MainWindowHandle -eq 0 } | Select-Object -First 1
if (-not $main) { throw "Missiva non è aperta ($($exes -join ', '))" }
if (-not $Installed) { "versione: $(if ($main.Path -like '*prova-release*') { 'finale (prova.ps1 -Release)' } else { 'Debug' })" }
$web = Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*$data\WebView2*" }
$webProcs = @($web | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
$mb = { param($v) [math]::Round($v / 1MB) }
"Missiva: set di lavoro $(& $mb $main.WorkingSet64) MB, privata $(& $mb $main.PrivateMemorySize64) MB"
"WebView2: $($webProcs.Count) processi, set di lavoro $(& $mb ($webProcs | Measure-Object WorkingSet64 -Sum).Sum) MB, privata $(& $mb ($webProcs | Measure-Object PrivateMemorySize64 -Sum).Sum) MB"
if ($Detail) {
    foreach ($w in $web) {
        $kind = if ($w.CommandLine -match '--type=([\w-]+)') { $Matches[1] } else { "browser" }
        $proc = Get-Process -Id $w.ProcessId -ErrorAction SilentlyContinue
        if ($proc) { "  $kind`: privata $(& $mb $proc.PrivateMemorySize64) MB" }
    }
}
"Totale privata: $(& $mb ($main.PrivateMemorySize64 + ($webProcs | Measure-Object PrivateMemorySize64 -Sum).Sum)) MB"
