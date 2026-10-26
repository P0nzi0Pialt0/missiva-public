# Prove a finestra dell'arrivo dei messaggi nuovi (audit di affidabilita', arrivo, 25 ottobre 2026; docs\audit\affidabilita-arrivo.md):
# la versione di prova (Debug di questo albero) fuori dallo schermo (--foto), con due account IMAP verso due server finti
# "--imap-finto" su 127.0.0.1, notifiche solo nel registro (MISSIVA_NOTIFICHE_SOLO_REGISTRO=1). Nessun account vero, nessuna
# casella vera, nessuna finestra sullo schermo. Nato dalle sonde dell'audit (ramo affidabilita-arrivo, strumenti\sonde-arrivo);
# a differenza di quello chiude solo i processi che ha avviato lui (scritti in processi.txt nella cartella di lavoro), mai gli
# altri dello stesso eseguibile (la suite di un altro agente, le prove di un altro albero). Prima "dotnet build".
#   -Lavoro cartella              dove stanno dati, registro e server finti (di base %TEMP%\missiva-arrivo)
#   -Start [-Arguments "..."]     avvia server finti e prova (cartelle nuove); prima chiude quelli avviati da qui
#   -Stop                         chiude prova e server finti avviati da qui
#   -Key "riga"                   una riga in tasti.txt (MISSIVA_KEYS): "rete tornata", "ripresa", "stato account", "stato elenco"
#   -Server 1|2 -Do "comando"     comanda il server finto: "arrivo Oggetto", "arrivo-datato 300 Oggetto", "arrivo-lento 5 Oggetto",
#                                 "muto", "muto idle", "muto tutto", "parla", "taglia"
#   -Measure -Server N -Do "..." -Until "regex" [-Limit 900]   manda il comando e aspetta la riga del registro della prova
#   -Cpu                          il tempo di processore della prova e dei due server finti (ms), per misurare un costo
#   -ServerArgs "riempi=120"      altri argomenti per i due server finti (con -Start)
#   -Restart [-Arguments "..."]   chiude e riavvia la sola prova, con gli stessi dati (copia locale compresa) e gli stessi
#                                 server finti (correzioni dell'audit di affidabilita', elenco: strumenti\arrivo\elenco.ps1)
param([string]$Lavoro = (Join-Path $env:TEMP 'missiva-arrivo'), [switch]$Start, [switch]$Stop, [string]$Key = '', [int]$Server = 0,
      [string]$Do = '', [string]$Arguments = '', [switch]$Measure, [string]$Until = 'notifica mostrata', [int]$Limit = 900, [string]$Tag = '',
      [switch]$Cpu, [string]$ServerArgs = '', [switch]$Restart)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path (Join-Path $PSScriptRoot '..\..\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe')).Path
$here = $Lavoro
New-Item -ItemType Directory -Force $here | Out-Null
$data = "$here\prova"
$log = "$data\missiva.log"
$ports = 14610, 14620
$pids = "$here\processi.txt"

function Lines([long]$from) {
    if (-not (Test-Path $log)) { return @() }
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite,Delete')
    try {
        if ($fs.Length -le $from) { return @() }
        $fs.Seek($from, 'Begin') | Out-Null
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        return $sr.ReadToEnd() -split "`r?`n" | Where-Object { $_ }
    } finally { $fs.Dispose() }
}
function Size { if (Test-Path $log) { (Get-Item $log).Length } else { 0 } }
# Solo i processi avviati da qui, e solo se sono ancora l'eseguibile di questo albero (un numero di processo si ricicla).
function Mine {
    if (-not (Test-Path $pids)) { return @() }
    foreach ($id in Get-Content $pids) {
        $p = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
        if ($p -and $p.Path -eq $exe) { $p }
    }
}

if ($Key) { Add-Content "$here\tasti.txt" $Key -Encoding utf8; "tasto: $Key"; return }
if ($Cpu) {
    foreach ($p in Mine) { $p.Refresh(); "{0} {1:N0} ms" -f $p.Id, $p.TotalProcessorTime.TotalMilliseconds }
    return
}
if ($Measure) {
    $from = Size
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $sent = Get-Date
    if ($Server -gt 0) { Add-Content "$here\server$Server\comandi.txt" $Do -Encoding utf8 } else { Add-Content "$here\tasti.txt" $Do -Encoding utf8 }
    "[$($sent.ToString('HH:mm:ss.fff'))] $(if ($Server -gt 0) { "al server $Server" } else { 'tasto' }): $Do; aspetto /$Until/ (al piu' $Limit s)"
    $seen = 0; $found = $false
    while ($clock.Elapsed.TotalSeconds -lt $Limit) {
        Start-Sleep -Milliseconds 200
        $all = @(Lines $from)
        for ($i = $seen; $i -lt $all.Count; $i++) {
            $l = $all[$i]
            if ($l -notmatch 'XML:' -and $l -match 'IDLE|controllo|notifica|elenco:|non letti di|server IMAP finto: (arriv|conn|DONE)|rete|raggiungibile|fermo|interrotto|attesa della connessione|collegamento|ripresa') {
                "  +{0,7:N1} s  {1}" -f $clock.Elapsed.TotalSeconds, $l
            }
            if ($l -match $Until) { $found = $true }
        }
        $seen = $all.Count
        if ($found) { break }
    }
    if ($found) { "TROVATO /$Until/ dopo {0:N1} s{1}" -f $clock.Elapsed.TotalSeconds, $(if ($Tag) { " ($Tag)" }) } else { "NON TROVATO /$Until/ in $Limit s$(if ($Tag) { " ($Tag)" })" }
    return
}
if ($Server -gt 0 -and $Do) { Add-Content "$here\server$Server\comandi.txt" $Do -Encoding utf8; "al server $Server`: $Do"; return }
if ($Restart) {
    # Solo la prova (il suo numero e' in prova.txt), se e' ancora l'eseguibile di questo albero; i server finti restano.
    if (Test-Path "$here\prova.txt") {
        $old = Get-Process -Id ([int](Get-Content "$here\prova.txt")) -ErrorAction SilentlyContinue
        if ($old -and $old.Path -eq $exe) { $old.Kill(); $old.WaitForExit() }
    }
    $from = Size
    $env:MISSIVA_NO_READ = $null
    $env:MISSIVA_IMAP_FINTO = '1'
    $env:MISSIVA_NOTIFICHE_SOLO_REGISTRO = '1'
    $env:MISSIVA_DATA = $data
    $env:MISSIVA_KEYS = "$here\tasti.txt"
    $a = "--foto $Arguments".Trim()
    $p = Start-Process $exe -ArgumentList $a -PassThru
    Add-Content $pids $p.Id
    Set-Content "$here\prova.txt" $p.Id
    for ($k = 0; $k -lt 40; $k++) { Start-Sleep -Milliseconds 500; if ((Lines $from) -match 'avviata') { break } }
    Start-Sleep -Milliseconds 1500
    "prova riavviata con gli stessi dati (processo $($p.Id), argomenti $a)"
    return
}
foreach ($p in Mine) { $p.Kill(); $p.WaitForExit() }
Remove-Item $pids, "$here\prova.txt" -ErrorAction SilentlyContinue
if ($Stop) { 'prova e server finti avviati da qui chiusi'; return }
if (-not $Start) { 'niente da fare: -Start, -Stop, -Key, -Server/-Do, -Measure, -Cpu'; return }
Remove-Item $data, "$here\server1", "$here\server2", "$here\tasti.txt" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data, "$here\server1", "$here\server2" | Out-Null
Add-Type -AssemblyName System.Security
$secret = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes('prova'), $null, 'CurrentUser'))
$list = @()
for ($i = 0; $i -lt 2; $i++) {
    $who = @('mario', 'maria')[$i]
    $list += [ordered]@{
        Id = "contoprova$($i + 1)"; Kind = 'Imap'; Email = "$who@studio-prova.example"; DisplayName = @('Mario Rossi', 'Maria Rossi')[$i]
        Imap = [ordered]@{ ImapHost = '127.0.0.1'; ImapPort = $ports[$i]; ImapSecurity = 'Ssl'; SmtpHost = '127.0.0.1'; SmtpPort = $ports[$i] + 1; SmtpSecurity = 'StartTls'; UserName = "$who@studio-prova.example"; ProtectedPassword = $secret }
    }
}
$settings = [ordered]@{ Accounts = $list; Theme = 'Light'; TrayExplained = $true; UndoSendSeconds = 0; KeepInTray = $true; NotificationSound = $false }
$settings | ConvertTo-Json -Depth 6 | Set-Content "$data\impostazioni.json" -Encoding utf8
$env:MISSIVA_NO_READ = $null
$env:MISSIVA_IMAP_FINTO = '1'
$env:MISSIVA_NOTIFICHE_SOLO_REGISTRO = '1'
for ($i = 0; $i -lt 2; $i++) {
    $env:MISSIVA_DATA = "$here\server$($i + 1)"
    $s = Start-Process $exe -ArgumentList "--imap-finto $($ports[$i]) $ServerArgs".Trim() -PassThru
    Add-Content $pids $s.Id
}
Start-Sleep -Milliseconds 1500
$env:MISSIVA_DATA = $data
$env:MISSIVA_KEYS = "$here\tasti.txt"
$a = "--foto $Arguments".Trim()
$p = Start-Process $exe -ArgumentList $a -PassThru
Add-Content $pids $p.Id
Set-Content "$here\prova.txt" $p.Id
for ($k = 0; $k -lt 60; $k++) { Start-Sleep -Milliseconds 500; if ((Lines 0) -match 'IDLE su INBOX di maria') { break } }
Start-Sleep -Milliseconds 2000
"prova avviata (processo $($p.Id), argomenti $a); registro: $log"
Lines 0 | Where-Object { $_ -match 'IDLE|avviata|controllo' } | Select-Object -First 12
