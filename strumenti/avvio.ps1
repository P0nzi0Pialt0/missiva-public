param([int]$Runs = 6, [string]$Arguments = "", [switch]$SenzaRete, [string]$Exe = "C:\Progetti\Missiva\prova-release\Missiva.exe",
      [string]$Compare = "", [int]$After = 800, [int]$Skip = 0, [switch]$Raw)
# I tempi dell'avvio della versione finale di prova, per fase: avvia $Runs volte l'eseguibile sulla copia dei dati già fatta
# da prova.ps1 (lanciare prima una volta "prova.ps1 -Release -Build"), aspetta le righe "tempi: avvio" nel registro, chiude
# e stampa per ogni riga la mediana dell'istante (ms dall'avvio del processo), il minimo, il massimo e la durata del tratto
# dalla riga prima. Audit del 7 ottobre 2026, parte A (docs\audit\ESITO.md).
#   -SenzaRete        MISSIVA_SENZA_RETE=1: nessun collegamento (decine di avvii di fila non aprono ogni volta Gmail)
#   -Arguments "--nascosta"   avvio nell'area di notifica
#   -Compare altro.exe        avvii alternati dei due eseguibili (prima e dopo), una tabella con le due colonne
#   -Skip 1           non conta il primo avvio (per esempio il primo dopo una compilazione o dopo un riavvio del PC: -Raw lo stampa)
# I numeri cambiano con lo stato del PC (a batteria circa il 40% in più): due varianti si confrontano solo alternate.
$ErrorActionPreference = "Stop"
$sandbox = "C:\Progetti\Missiva\prova-dati"; $log = Join-Path $sandbox "missiva.log"
if (-not (Test-Path $log)) { throw "Manca la copia dei dati: prima strumenti\prova.ps1 -Release -Build" }
$env:MISSIVA_DATA = $sandbox; $env:MISSIVA_NO_READ = "1"
if ($SenzaRete) { $env:MISSIVA_SENZA_RETE = "1" } else { Remove-Item Env:MISSIVA_SENZA_RETE -ErrorAction SilentlyContinue }
$exes = @($Exe) + @($Compare | Where-Object { $_ })
function Stop-Prova { Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { $exes -contains $_.Path } | ForEach-Object { $_.Kill(); $_.WaitForExit() } }
function Read-From($from) {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Position = $from; return (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
}
function Median($v) { $v = @($v | Sort-Object); if ($v.Count -eq 0) { return $null }; if ($v.Count % 2) { $v[($v.Count - 1) / 2] } else { [int][math]::Round(($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2) } }
function Start-One($path) {
    Stop-Prova; Start-Sleep -Milliseconds 1000
    $from = (Get-Item $log).Length
    if ($Arguments) { Start-Process $path -ArgumentList $Arguments | Out-Null } else { Start-Process $path | Out-Null }
    $sw = [Diagnostics.Stopwatch]::StartNew(); $text = ""
    # Con la finestra l'ultima riga è "elenco nel disegno"; nascosta, "OnStartup finito".
    $until = if ($Arguments -like "*--nascosta*") { "OnStartup finito a " } else { "elenco nel disegno" }
    while ($sw.ElapsedMilliseconds -lt 20000) {
        Start-Sleep -Milliseconds 200
        if ((Get-Item $log).Length -lt $from) { $from = 0 } # registro ruotato
        $text = Read-From $from
        if ($text -match [regex]::Escape($until)) { break }
    }
    Start-Sleep -Milliseconds $After
    $run = [ordered]@{}
    foreach ($line in (Read-From $from) -split "`r?`n") {
        if ($line -match "tempi: avvio, (.+) a (-?\d+) ms( \(thread \d+\))?$") {
            $name = $Matches[1] -replace " \([^)]*\)$", ""
            if ($name -notlike "assembly *" -and -not $run.Contains($name)) { $run[$name] = [int]$Matches[2] }
        }
        elseif ($line -match "tempi: finestra pronta (\d+) ms" -and -not $run.Contains("finestra pronta")) { $run["finestra pronta"] = [int]$Matches[1] }
    }
    return $run
}
function Table($runs) {
    $names = @(); foreach ($run in $runs) { foreach ($k in $run.Keys) { if ($names -notcontains $k) { $names += $k } } }
    $order = $names | Sort-Object { $n = $_; Median @($runs | Where-Object { $_.Contains($n) } | ForEach-Object { $_[$n] }) }
    $t = [ordered]@{}; $prev = $null
    foreach ($n in $order) {
        $v = @($runs | Where-Object { $_.Contains($n) } | ForEach-Object { $_[$n] })
        $d = if ($prev) { Median @($runs | Where-Object { $_.Contains($n) -and $_.Contains($prev) } | ForEach-Object { $_[$n] - $_[$prev] }) } else { $null }
        $t[$n] = "{0,5} ({1}..{2}) tratto {3,4}" -f (Median $v), ($v | Measure-Object -Minimum).Minimum, ($v | Measure-Object -Maximum).Maximum, $d
        $prev = $n
    }
    return $t
}
$all = @{}; foreach ($e in $exes) { $all[$e] = @() }
for ($r = 0; $r -lt $Runs; $r++) {
    foreach ($e in $exes) {
        $run = Start-One $e
        if ($Raw) { "avvio $r di $(Split-Path (Split-Path $e) -Leaf): " + (($run.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "; ") }
        if ($r -ge $Skip) { $all[$e] += , $run }
    }
}
Stop-Prova
$tables = @($exes | ForEach-Object { Table $all[$_] })
"== $($Runs - $Skip) avvii$(if ($exes.Count -gt 1) { ' alternati per variante' }), $(if ($SenzaRete) { 'senza rete' } else { 'in rete' })$(if ($Arguments) { ", $Arguments" }): mediana (minimo..massimo) e tratto dalla riga prima, in ms dall'avvio del processo"
if ($exes.Count -gt 1) { "{0,-46} {1,-30} {2,-30}" -f "", (Split-Path (Split-Path $exes[0]) -Leaf), (Split-Path (Split-Path $exes[1]) -Leaf) }
$names = @($tables[-1].Keys) + @($tables[0].Keys | Where-Object { -not $tables[-1].Contains($_) })
foreach ($n in $names) {
    $cells = $tables | ForEach-Object { if ($_.Contains($n)) { $_[$n] } else { "-" } }
    "{0,-46} {1}" -f $n.Substring(0, [Math]::Min(46, $n.Length)), (($cells | ForEach-Object { "{0,-30}" -f $_ }) -join " ")
}
