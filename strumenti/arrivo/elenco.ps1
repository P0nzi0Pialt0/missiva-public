# Prove a finestra delle correzioni dell'audit di affidabilita', elenco (25 ottobre 2026; docs\audit\affidabilita-elenco.md):
# l'elenco, le cartelle e i numeri che restano indietro rispetto al server. Usa finestra.ps1 (stessa -Lavoro): la versione
# di prova (Debug di questo albero) fuori dallo schermo, due account IMAP verso due server finti su 127.0.0.1, notifiche solo
# nel registro. Chiude solo i processi avviati da finestra.ps1. Prima "dotnet build". Ogni prova parte da capo (-Start) e
# alla fine chiude tutto. Il tempo misurato va dal comando che crea la situazione alla riga "prova stato elenco" che la
# mostra risolta (lo stato si chiede ogni secondo).
#   -Prova E01        la prima pagina non arriva (server che non si raggiunge) con la copia locale visibile; poi il server torna
#   -Prova E01rete    lo stesso, e al ritorno del server "rete tornata"
#   -Prova E02        un arrivo gia' letto altrove
#   -Prova E03        IDLE morto, un messaggio tolto dal server intanto, poi l'IDLE si ricollega
#   -Prova E05        Tutti gli account con il secondo account che non risponde
#   -Prova E06        un arrivo in una cartella dell'utente: il suo numero (al piu' 6 minuti)
#   -Prova E06aperta  lo stesso con la cartella aperta: la sua riga
#   -Prova E10        con il filtro Contrassegnati un arrivo non contrassegnato
#   -Prova E12        120 messaggi, uno rimesso con la data di un mese fa, F5, poi la pagina dopo: l'ordine dei gruppi
#   -Prova ripresa    quattro arrivi con le connessioni morte, poi "ripresa": quando sono nell'elenco
#   -Prova costo      la cartella Clienti aperta per 6 minuti: comandi IMAP ai server finti e processore della prova (E-06)
param([string]$Lavoro = (Join-Path $env:TEMP 'missiva-elenco'), [Parameter(Mandatory)][string]$Prova, [int]$Limit = 150)
$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot 'finestra.ps1'
$exe = (Resolve-Path (Join-Path $PSScriptRoot '..\..\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe')).Path
$here = $Lavoro
$log = "$here\prova\missiva.log"

function Size([string]$file = $log) { if (Test-Path $file) { (Get-Item $file).Length } else { 0 } }
function Lines([long]$from, [string]$file = $log) {
    if (-not (Test-Path $file)) { return @() }
    $fs = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite,Delete')
    try { if ($fs.Length -le $from) { return @() }; $fs.Seek($from, 'Begin') | Out-Null; (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() -split "`r?`n" | Where-Object { $_ } } finally { $fs.Dispose() }
}
function Key([string]$text) { Add-Content "$here\tasti.txt" $text -Encoding utf8 }
function ToServer([int]$n, [string]$text) { Add-Content "$here\server$n\comandi.txt" $text -Encoding utf8 }
# La riga "prova stato elenco" chiesta adesso.
function State {
    $from = Size
    Key 'stato elenco'
    for ($k = 0; $k -lt 40; $k++) {
        Start-Sleep -Milliseconds 150
        $line = Lines $from | Where-Object { $_ -match 'prova stato elenco' } | Select-Object -First 1
        if ($line) { return $line }
    }
    return ''
}
# L'UID dell'ultimo messaggio arrivato sul server N (dal suo registro), dopo la posizione data.
function ArrivedUid([int]$n, [long]$from) {
    for ($k = 0; $k -lt 30; $k++) {
        $line = Lines $from "$here\server$n\missiva.log" | Where-Object { $_ -match "arrivato l'UID (\d+)" } | Select-Object -Last 1
        if ($line -match "arrivato l'UID (\d+)") { return $Matches[1] }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Rows([string]$state) { if ($state -match 'tutte le righe \[([^\]]*)\]') { @($Matches[1] -split ' ' | Where-Object { $_ }) } else { @() } }
# Aspetta (chiedendo lo stato ogni secondo) che la condizione sullo stato sia vera; il tempo dal via.
function Until([scriptblock]$test, [Diagnostics.Stopwatch]$clock, [int]$limit = $Limit) {
    while ($clock.Elapsed.TotalSeconds -lt $limit) {
        $s = State
        if (& $test $s) { return @($clock.Elapsed.TotalSeconds, $s) }
        Start-Sleep -Milliseconds 850
    }
    return @($null, (State), $limit)
}
function Report($name, $result) {
    if ($null -ne $result[0]) { "{0}: dopo {1:N1} s" -f $name, $result[0] } else { "{0}: non in {1} s" -f $name, $result[2] }
    "  stato: $($result[1] -replace '^.*prova stato elenco: ', '')"
}
function StopProva {
    if (Test-Path "$here\prova.txt") {
        $p = Get-Process -Id ([int](Get-Content "$here\prova.txt")) -ErrorAction SilentlyContinue
        if ($p -and $p.Path -eq $exe) { $p.Kill(); $p.WaitForExit() }
    }
}

$serverArgs = if ($Prova -eq 'E12') { 'riempi=120' } else { '' }
$extra = if ($Prova -eq 'E10') { '--filter Flagged' } else { '' }
& $tool -Lavoro $here -Start -ServerArgs $serverArgs -Arguments $extra | Select-Object -First 1
Start-Sleep -Seconds 12 # il primo controllo dopo l'avvio (10 s) e la copia locale della prima pagina
try {
    switch ($Prova) {
        { $_ -in 'E01', 'E01rete' } {
            # Missiva chiusa; intanto quattro arrivi; riaperta con il server che non si raggiunge: la copia locale e basta.
            StopProva
            $from = Size "$here\server1\missiva.log"
            ToServer 1 'irraggiungibile'
            1..4 | ForEach-Object { ToServer 1 "arrivo Arrivato con Missiva chiusa $_" }
            Start-Sleep -Milliseconds 1500
            $uids = @(Lines $from "$here\server1\missiva.log" | Where-Object { $_ -match "arrivato l'UID (\d+)" } | ForEach-Object { if ($_ -match "arrivato l'UID (\d+)") { $Matches[1] } })
            & $tool -Lavoro $here -Restart | Out-Null
            Start-Sleep -Seconds 4
            "con il server che non si raggiunge: $((State) -replace '^.*prova stato elenco: ', '')"
            "arrivati con Missiva chiusa: $($uids -join ' ')"
            $clock = [Diagnostics.Stopwatch]::StartNew()
            ToServer 1 'raggiungibile'
            if ($Prova -eq 'E01rete') { Start-Sleep -Milliseconds 300; Key 'rete tornata' }
            Report "i quattro nell'elenco" (Until { param($s) $r = Rows $s; @($uids | Where-Object { $r -contains $_ }).Count -eq 4 } $clock)
        }
        'E02' {
            $from = Size "$here\server1\missiva.log"
            $clock = [Diagnostics.Stopwatch]::StartNew()
            ToServer 1 'arrivo-letto Letto sul telefono appena arrivato'
            $uid = ArrivedUid 1 $from
            Report "l'arrivo gia' letto (UID $uid) nell'elenco" (Until { param($s) (Rows $s) -contains $uid } $clock 60)
        }
        'E03' {
            $before = State
            $gone = (Rows $before)[0]
            ToServer 1 'muto idle'
            Start-Sleep -Milliseconds 800
            ToServer 1 "togli $gone"
            Start-Sleep -Milliseconds 800
            $clock = [Diagnostics.Stopwatch]::StartNew()
            ToServer 1 'taglia'
            Report "la riga tolta dal server (UID $gone) fuori dall'elenco" (Until { param($s) -not ((Rows $s) -contains $gone) } $clock 60)
        }
        'E05' {
            # Il secondo account non risponde piu' (connessioni aperte e nuove mute); sul primo un messaggio tolto senza avvisi:
            # quando la vista di tutti gli account mostra le pagine del server, la sua riga (della copia locale) esce. Lo stesso
            # UID manca anche alla copia del secondo account (i due server hanno gli stessi UID), che resta a schermo finche'
            # il secondo account non risponde.
            ToServer 2 'togli 1'
            Start-Sleep -Milliseconds 500
            Key 'apri posta 2'
            Start-Sleep -Seconds 3
            Key 'apri posta 1'
            Start-Sleep -Seconds 3
            $gone = (Rows (State))[0]
            ToServer 1 "togli $gone"
            ToServer 2 'muto tutto'
            Start-Sleep -Milliseconds 800
            $clock = [Diagnostics.Stopwatch]::StartNew()
            Key 'apri tutti'
            Report "Tutti gli account con le pagine del server del primo account (riga $gone uscita)" (Until { param($s) $s -match 'tutti gli account' -and -not ((Rows $s) -contains $gone) } $clock 200)
        }
        { $_ -in 'E06', 'E06aperta' } {
            if ($Prova -eq 'E06aperta') { Key 'apri cartella Clienti'; Start-Sleep -Seconds 3 }
            $from = Size "$here\server1\missiva.log"
            $clock = [Diagnostics.Stopwatch]::StartNew()
            ToServer 1 'arrivo-cartella Arrivato in Clienti'
            $uid = ArrivedUid 1 $from
            if ($Prova -eq 'E06') { Report "il numero di Clienti" (Until { param($s) $s -match 'Clienti=1/' } $clock 400) }
            else { Report "il messaggio (UID $uid) nella cartella aperta" (Until { param($s) (Rows $s) -contains $uid } $clock 400) }
        }
        'E10' {
            $from = Size "$here\server1\missiva.log"
            ToServer 1 'arrivo Non contrassegnato'
            $uid = ArrivedUid 1 $from
            Start-Sleep -Seconds 4
            $s = State
            "con il filtro Contrassegnati, l'arrivo non contrassegnato (UID $uid) $(if ((Rows $s) -contains $uid) { 'E'' in cima' } else { 'non e'' nell''elenco' })"
            "  stato: $($s -replace '^.*prova stato elenco: ', '')"
        }
        'E12' {
            $from = Size "$here\server1\missiva.log"
            ToServer 1 'arrivo-datato 3000000 Rimesso dal telefono con la data di un mese fa'
            $uid = ArrivedUid 1 $from
            Start-Sleep -Seconds 3
            Key 'f5'
            Start-Sleep -Seconds 4
            Key 'vscroll 999999'
            Start-Sleep -Seconds 4
            $s = State
            "UID $uid; gruppi dopo la pagina dopo: $(if ($s -match 'gruppi ([^;]*);') { $Matches[1] })"
            "  stato: $($s -replace '^.*prova stato elenco: ', '')"
        }
        'ripresa' {
            ToServer 1 'muto'
            Start-Sleep -Milliseconds 800
            $from = Size "$here\server1\missiva.log"
            1..4 | ForEach-Object { ToServer 1 "arrivo Arrivato col PC sospeso $_" }
            Start-Sleep -Milliseconds 1500
            $uids = @(Lines $from "$here\server1\missiva.log" | Where-Object { $_ -match "arrivato l'UID (\d+)" } | ForEach-Object { if ($_ -match "arrivato l'UID (\d+)") { $Matches[1] } })
            Key 'chiudi finestra'
            Start-Sleep -Seconds 2
            $clock = [Diagnostics.Stopwatch]::StartNew()
            Key 'ripresa'
            Report "i quattro ($($uids -join ' ')) nell'elenco, finestra nell'area di notifica" (Until { param($s) $r = Rows $s; @($uids | Where-Object { $r -contains $_ }).Count -eq 4 } $clock 60)
        }
        'costo' {
            # Il costo di E-06 a finestra ferma: la cartella Clienti aperta (che nessuno guarda) per 6 minuti dopo un minuto di
            # assestamento; i comandi IMAP arrivati ai due server finti e il processore della prova in quel tempo.
            Key 'apri cartella Clienti'
            Start-Sleep -Seconds 60
            $p = Get-Process -Id ([int](Get-Content "$here\prova.txt"))
            $p.Refresh(); $cpu0 = $p.TotalProcessorTime.TotalMilliseconds
            $from1 = Size "$here\server1\missiva.log"; $from2 = Size "$here\server2\missiva.log"
            Start-Sleep -Seconds 360
            $p.Refresh(); $cpu = $p.TotalProcessorTime.TotalMilliseconds - $cpu0
            $c1 = @(Lines $from1 "$here\server1\missiva.log" | Where-Object { $_ -match 'server IMAP finto: [A-Z]' -and $_ -notmatch 'arrivato|connessioni' })
            $c2 = @(Lines $from2 "$here\server2\missiva.log" | Where-Object { $_ -match 'server IMAP finto: [A-Z]' -and $_ -notmatch 'arrivato|connessioni' })
            $kinds = ($c1 + $c2) | ForEach-Object { if ($_ -match 'server IMAP finto: (UID [A-Z]+|[A-Z]+)') { $Matches[1] } } | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Count) $($_.Name)" }
            "in 360 s con Clienti aperta: comandi IMAP {0} al server 1 e {1} al server 2 ({2}); processore della prova {3:N0} ms ({4:N2} ms/s)" -f $c1.Count, $c2.Count, ($kinds -join ', '), $cpu, ($cpu / 360)
        }
        default { "prova sconosciuta: $Prova" }
    }
}
finally {
    & $tool -Lavoro $here -Stop | Out-Null
}
