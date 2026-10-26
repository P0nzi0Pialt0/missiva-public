# Prova a finestra F9 delle correzioni dell'arrivo (audit di affidabilita', 25 ottobre 2026, A-04): la ripresa del PC con la
# rete non ancora pronta. Con la prova di finestra.ps1 aperta (stessa -Lavoro): "rete assente" (i collegamenti falliscono come
# senza rete), un arrivo sull'account 1, il segnale "ripresa", e dopo -Ready secondi "rete tornata in silenzio" (la rete
# torna senza che Windows lo dica, come il DNS che risponde qualche secondo dopo la ripresa). Si misura quando arriva la
# notifica: il controllo della ripresa deve ripetersi da solo dopo pochi secondi, non aspettare il giro del controllo di riserva.
param([string]$Lavoro = (Join-Path $env:TEMP 'missiva-arrivo'), [double]$Ready = 4, [int]$Limit = 150)
$here = $Lavoro
$log = "$here\prova\missiva.log"
function Size { (Get-Item $log).Length }
function Lines([long]$from) {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite,Delete')
    try { if ($fs.Length -le $from) { return @() }; $fs.Seek($from, 'Begin') | Out-Null; (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() -split "`r?`n" | Where-Object { $_ } } finally { $fs.Dispose() }
}
$start = Size
Add-Content "$here\tasti.txt" 'rete assente' -Encoding utf8
Start-Sleep -Milliseconds 600
Add-Content "$here\server1\comandi.txt" 'arrivo Arrivato con la rete non ancora pronta' -Encoding utf8
Start-Sleep -Milliseconds 600
$clock = [Diagnostics.Stopwatch]::StartNew()
Add-Content "$here\tasti.txt" 'ripresa' -Encoding utf8
"[+{0,6:N1} s] ripresa (MISSIVA_KEYS), la rete torna fra $Ready s" -f $clock.Elapsed.TotalSeconds
$sent = $false; $seen = 0; $got = $null
while ($clock.Elapsed.TotalSeconds -lt $Limit -and $null -eq $got) {
    Start-Sleep -Milliseconds 200
    if (-not $sent -and $clock.Elapsed.TotalSeconds -ge $Ready) {
        Add-Content "$here\tasti.txt" 'rete tornata in silenzio' -Encoding utf8
        "[+{0,6:N1} s] rete tornata in silenzio" -f $clock.Elapsed.TotalSeconds
        $sent = $true
    }
    $all = @(Lines $start)
    for ($i = $seen; $i -lt $all.Count; $i++) {
        $l = $all[$i]
        if ($l -notmatch 'XML:|tempi:' -and $l -match 'controllo|notifica mostrata|ripresa|rete|riprovo') { "  +{0,6:N1} s  {1}" -f $clock.Elapsed.TotalSeconds, $l }
        if ($l -match 'notifica mostrata: .* di mario@') { $got = $clock.Elapsed.TotalSeconds }
    }
    $seen = $all.Count
}
if ($null -ne $got) { "notifica di mario: {0:N1} s dopo la ripresa, {1:N1} s dopo la rete tornata" -f $got, ($got - $Ready) } else { "notifica di mario: non arrivata in $Limit s" }
