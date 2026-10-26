# Prova a finestra F5 dell'audit di affidabilita', arrivo (docs\audit\affidabilita-arrivo.md): come dopo la ripresa del PC.
# Con la prova di finestra.ps1 aperta (stessa -Lavoro) e, prima, "muto idle" sul server 2 (prova F4). Subito dopo un
# controllo di riserva tutte le connessioni dell'account 1 muoiono senza chiudersi (le nuove funzionano), un messaggio arriva
# sull'account 1 e uno sull'account 2; poi il segnale (-Signal): "rete tornata" (come Windows che dice che la rete e' cambiata)
# o "ripresa" (come Windows che dice che il PC si e' ripreso dalla sospensione, solo dalle correzioni del 25 ottobre 2026).
# Si misura quando arrivano le due notifiche. Nato dalle sonde dell'audit (ramo affidabilita-arrivo, strumenti\sonde-arrivo).
param([string]$Lavoro = (Join-Path $env:TEMP 'missiva-arrivo'), [string]$Signal = 'rete tornata', [int]$Limit = 420)
$here = $Lavoro
$log = "$here\prova\missiva.log"
function Size { (Get-Item $log).Length }
function Lines([long]$from) {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite,Delete')
    try { if ($fs.Length -le $from) { return @() }; $fs.Seek($from, 'Begin') | Out-Null; (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() -split "`r?`n" | Where-Object { $_ } } finally { $fs.Dispose() }
}
# Aspetta il prossimo controllo di riserva (la riga "tempi:" della versione di prova).
$from = Size
$wait = [Diagnostics.Stopwatch]::StartNew()
while ($wait.Elapsed.TotalSeconds -lt 150 -and -not ((Lines $from) -match 'tempi: controllo dei nuovi messaggi')) { Start-Sleep -Milliseconds 200 }
$start = Size
$clock = [Diagnostics.Stopwatch]::StartNew()
"[{0}] controllo di riserva appena fatto; ora: muto sul server 1" -f (Get-Date -Format 'HH:mm:ss.fff')
Add-Content "$here\server1\comandi.txt" 'muto' -Encoding utf8
Start-Sleep -Milliseconds 1500
Add-Content "$here\server1\comandi.txt" 'arrivo Ordine dopo la ripresa (account 1)' -Encoding utf8
"[+{0,6:N1} s] arrivo sull'account 1" -f $clock.Elapsed.TotalSeconds
Start-Sleep -Milliseconds 1000
Add-Content "$here\server2\comandi.txt" 'arrivo Ordine dopo la ripresa (account 2)' -Encoding utf8
"[+{0,6:N1} s] arrivo sull'account 2" -f $clock.Elapsed.TotalSeconds
Start-Sleep -Milliseconds 3000
Add-Content "$here\tasti.txt" $Signal -Encoding utf8
$signalAt = $clock.Elapsed.TotalSeconds
"[+{0,6:N1} s] {1} (MISSIVA_KEYS)" -f $signalAt, $Signal
$seen = 0; $got = @{}
while ($clock.Elapsed.TotalSeconds -lt $Limit -and $got.Count -lt 2) {
    Start-Sleep -Milliseconds 200
    $all = @(Lines $start)
    for ($i = $seen; $i -lt $all.Count; $i++) {
        $l = $all[$i]
        if ($l -notmatch 'XML:' -and $l -match 'IDLE|controllo|notifica mostrata|elenco:|rete|raggiungibile|fermo|interrotto|attesa della connessione|collegamento|non letti|ripresa') { "  +{0,6:N1} s  {1}" -f $clock.Elapsed.TotalSeconds, $l }
        if ($l -match 'notifica mostrata: .* di (mario|maria)@') { $got[$Matches[1]] = $clock.Elapsed.TotalSeconds }
    }
    $seen = $all.Count
}
foreach ($k in 'mario', 'maria') {
    if ($got.ContainsKey($k)) { "notifica di {0}: {1:N1} s dopo l'inizio, {2:N1} s dopo il segnale" -f $k, $got[$k], ($got[$k] - $signalAt) }
    else { "notifica di ${k}: non arrivata in $Limit s" }
}
