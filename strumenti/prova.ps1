param([string]$Arguments = "", [switch]$Build, [switch]$Stop, [switch]$Release, [string]$Publish = "", [switch]$Keep)
# Avvia la versione di prova (Debug) su una COPIA dei dati di Missiva, così le prove non toccano impostazioni e account
# veri, e senza segnare come letti i messaggi aperti. Uso: pwsh -File strumenti\prova.ps1 [-Build] [-Arguments "--filter Unread"]
# -Stop chiude soltanto la versione di prova. Registro della prova: C:\Progetti\Missiva\prova-dati\missiva.log
# -Release avvia invece la versione finale in C:\Progetti\Missiva\prova-release; con -Build la compila prima come
# installa.ps1 (dotnet publish Release, un solo file) più i ganci di prova (MissivaProva: copia dei dati, niente messaggi
# segnati letti, righe "tempi:"). Gli argomenti da riga di comando della Debug lì non ci sono. -Publish aggiunge
# proprietà alla compilazione, per esempio -Publish "-p:PublishReadyToRun=true". -Keep non rifà la copia dei dati: per
# provare impostazioni cambiate a mano nella copia (per esempio un account finto).
# Attenzione: la casella di posta resta quella vera. Nelle prove niente azioni che la modificano.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$debugExe = Join-Path $root "bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe"
$releaseDir = "C:\Progetti\Missiva\prova-release"
$releaseExe = Join-Path $releaseDir "Missiva.exe"
$real = Join-Path $env:LOCALAPPDATA "Missiva"
$sandbox = "C:\Progetti\Missiva\prova-dati"

# Il server IMAP finto (Missiva.exe --imap-finto porta) è lo stesso eseguibile ma resta aperto: si chiude a parte.
$servers = @(Get-CimInstance Win32_Process -Filter "Name = 'Missiva.exe'" | Where-Object { $_.CommandLine -like "*--imap-finto*" } | ForEach-Object ProcessId)
Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { ($_.Path -eq $debugExe -or $_.Path -eq $releaseExe) -and $servers -notcontains $_.Id } | ForEach-Object { $_.Kill(); $_.WaitForExit() }
if ($Stop) { "versione di prova chiusa"; return }

$exe = if ($Release) { $releaseExe } else { $debugExe }
if ($Release -and $Build) {
    Remove-Item $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
    $extra = @($Publish -split " " | Where-Object { $_ })
    dotnet publish (Join-Path $root "Missiva.csproj") -c Release -o $releaseDir --nologo -v q -p:MissivaProva=true @extra 2>&1 | Select-String -Pattern " error |Errori"
    if ($LASTEXITCODE -ne 0) { throw "Compilazione non riuscita." }
    "versione finale di prova: $([math]::Round((Get-Item $releaseExe).Length / 1MB)) MB"
}
elseif ($Build) {
    dotnet build (Join-Path $root "Missiva.csproj") --nologo -v q 2>&1 | Select-String -Pattern " error |Errori"
    if ($LASTEXITCODE -ne 0) { throw "Compilazione non riuscita." }
}

# Impostazioni, token e copia locale presi ogni volta da quelli veri (il registro della prova resta), tranne con -Keep.
New-Item -ItemType Directory -Force $sandbox | Out-Null
if (-not $Keep) {
    foreach ($name in "impostazioni.json", "msal.cache") {
        $from = Join-Path $real $name
        if (Test-Path $from) { Copy-Item $from $sandbox -Force }
    }
    # La coda delle azioni fatte senza rete: quella vera, o nessuna (una lasciata da una prova di prima ripartirebbe, solo nel registro).
    $queue = Join-Path $real "coda.dat"
    if (Test-Path $queue) { Copy-Item $queue $sandbox -Force } else { Remove-Item (Join-Path $sandbox "coda.dat") -Force -ErrorAction SilentlyContinue }
    if (Test-Path (Join-Path $real "cache")) {
        Remove-Item (Join-Path $sandbox "cache") -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $real "cache") $sandbox -Recurse -Force
    }
}

$env:MISSIVA_DATA = $sandbox
$env:MISSIVA_NO_READ = "1"
$p = if ($Arguments) { Start-Process $exe -ArgumentList $Arguments -PassThru } else { Start-Process $exe -PassThru }
# Con --nascosta (avvio con Windows, nell'area di notifica) la finestra non c'è: non la si aspetta.
for ($i = 0; $i -lt 40 -and $Arguments -notlike "*--nascosta*"; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.MainWindowHandle -ne 0) { break }
}
"versione di prova avviata (processo $($p.Id)); registro: $sandbox\missiva.log"
