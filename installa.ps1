# Installa (o aggiorna) Missiva per l'utente corrente, senza permessi di amministratore:
# compila la versione finale, la prova, la mette in %LOCALAPPDATA%\Programs\Missiva, crea il collegamento nel menu
# Start e la registra in Impostazioni, App installate, da dove si disinstalla, e fra le app di posta per i link mailto:.
# Impostazioni e account non vengono toccati.
# Uso: pwsh -ExecutionPolicy Bypass -File installa.ps1
# Con -InCartella percorso compila e prova allo stesso modo, ma mette i file in quella cartella e si ferma lì: non
# tocca la Missiva installata, né il menu Start né il registro (serve a provare questo script).
param([string]$InCartella)
$ErrorActionPreference = "Stop"
$dir = if ($InCartella) { $InCartella } else { Join-Path $env:LOCALAPPDATA "Programs\Missiva" }
$exe = Join-Path $dir "Missiva.exe"

# Prima si compila e si prova, in una cartella a parte, e solo dopo si tocca la Missiva installata: se la compilazione
# non riesce o l'eseguibile nuovo non parte, quella di prima resta dov'è. Fino al 13 ottobre 2026 la cartella di
# installazione si svuotava prima di compilare, e quel giorno una compilazione che aveva riusato un file intermedio del
# giorno prima (in obj\...\R2R il pezzo di Missiva.dll che finisce nell'eseguibile, di un'altra compilazione rispetto al
# codice precompilato) ha lasciato un eseguibile che si chiudeva a ogni avvio ("MVID mismatch" nel registro eventi).
$staging = Join-Path $PSScriptRoot "bin\installazione"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
# I file intermedi del codice precompilato (ReadyToRun) si rifanno ogni volta da capo: sono quelli rimasti vecchi.
Get-ChildItem (Join-Path $PSScriptRoot "obj\Release") -Recurse -Directory -Filter R2R -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force

Write-Host "Compilo Missiva..."
dotnet publish (Join-Path $PSScriptRoot "Missiva.csproj") -c Release -o $staging --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Compilazione non riuscita. La Missiva installata non è stata toccata." }

# L'eseguibile nuovo si avvia una volta con --versione: esce subito con 0, senza finestre e senza toccare i dati
# (Program.Main). Se .NET non riesce a caricarlo esce con un errore, e ci si ferma qui.
$start = New-Object System.Diagnostics.ProcessStartInfo((Join-Path $staging "Missiva.exe"), "--versione")
$start.UseShellExecute = $false
$check = [System.Diagnostics.Process]::Start($start)
if (-not $check.WaitForExit(120000)) {
    $check.Kill()
    throw "L'eseguibile appena compilato non risponde. La Missiva installata non è stata toccata."
}
if ($check.ExitCode -ne 0) {
    throw ("L'eseguibile appena compilato non parte (codice 0x{0:X8}). La Missiva installata non è stata toccata." -f $check.ExitCode)
}

# La versione la dice l'eseguibile appena compilato (Missiva.csproj, MissivaGitVersion): il numero per App installate e
# il testo con la data e il commit dell'ultima modifica compresa. Quella di prima, se c'è, dall'eseguibile installato.
$info = (Get-Item (Join-Path $staging "Missiva.exe")).VersionInfo
$version = "{0}.{1}.{2}" -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
$described = if ($info.ProductVersion) { $info.ProductVersion } else { $version }
# (Fino al 14 ottobre 2026 era "0.3.0+" con il commit intero: se ne tengono sette cifre.)
$previous = if (Test-Path $exe) { (Get-Item $exe).VersionInfo.ProductVersion -replace '\+([0-9a-f]{7})[0-9a-f]{33}$', ' ($1)' }
Write-Host "Compilata e provata: Missiva $described."

# Missiva aperta dalla cartella di installazione blocca i file: le si chiede di uscire come con Esci, anche quando è
# nascosta nell'area di notifica (senza finestra CloseMainWindow non la trova), con "esci" sul suo canale con nome
# (Program.MailtoPipe, solo per questo utente); le scritture aperte chiedono se salvare la bozza. Le versioni di prima,
# che non conoscono "esci", si chiudono come premendo la X; senza risposta, dopo qualche secondo si terminano.
Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | ForEach-Object {
    Write-Host "Chiudo Missiva per aggiornarla..."
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "Missiva-mailto-$($_.SessionId)", [System.IO.Pipes.PipeDirection]::Out, [System.IO.Pipes.PipeOptions]::CurrentUserOnly)
        $pipe.Connect(3000)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $writer.WriteLine("esci")
        $writer.Flush()
        $pipe.Dispose()
    } catch { }
    if (-not $_.WaitForExit(30000)) {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(8000)) { $_.Kill(); $_.WaitForExit(10000) | Out-Null }
    }
}

# Missiva è un solo file: la cartella si svuota, così non restano file di versioni precedenti. Windows può tenere il
# file bloccato ancora per un attimo dopo la chiusura: si riprova.
for ($try = 1; (Test-Path $dir) -and (Get-ChildItem $dir).Count -gt 0; $try++) {
    try { Get-ChildItem $dir | Remove-Item -Recurse -Force -ErrorAction Stop }
    catch {
        if ($try -ge 10) { throw "Non riesco a sostituire Missiva: il file è ancora in uso. Chiudi Missiva e riprova." }
        Start-Sleep -Milliseconds 500
    }
}

New-Item -ItemType Directory -Force $dir | Out-Null
Get-ChildItem $staging | Move-Item -Destination $dir -Force
Remove-Item $staging -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "disinstalla.ps1") $dir -Force
if ($InCartella) {
    Write-Host "Missiva $described compilata e provata in $dir. Quella installata non è stata toccata."
    return
}

# Collegamento nel menu Start
$link = Join-Path ([Environment]::GetFolderPath("Programs")) "Missiva.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dir
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = "Posta, senza il nuovo Outlook"
$shortcut.Save()

# Voce in Impostazioni, App installate
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Missiva"
New-Item $key -Force | Out-Null
$size = [int]((Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
# La disinstallazione parte con Windows PowerShell (powershell.exe 5.1), che c'è in ogni Windows: PowerShell 7 (pwsh.exe)
# su un Windows normale non c'è, e "Disinstalla" in App installate non avrebbe fatto nulla. disinstalla.ps1 gira con tutti e due.
$windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
$values = @{
    DisplayName = "Missiva"; DisplayVersion = $version; Publisher = "Missiva"; DisplayIcon = "$exe,0"
    InstallLocation = $dir; EstimatedSize = $size; NoModify = 1; NoRepair = 1
    UninstallString = "`"$windowsPowerShell`" -NoProfile -ExecutionPolicy Bypass -File `"$dir\disinstalla.ps1`""
}
foreach ($name in $values.Keys) {
    $type = if ($values[$name] -is [int]) { "DWord" } else { "String" }
    New-ItemProperty $key -Name $name -Value $values[$name] -PropertyType $type -Force | Out-Null
}

# Missiva fra le app di posta di Windows, per i link mailto: delle pagine web e dei PDF (solo per l'utente, in HKCU).
# Sceglierla resta a te, in Impostazioni, App, App predefinite, Missiva (MAILTO): Windows non lascia farlo ai programmi.
$progId = "HKCU:\Software\Classes\Missiva.Mailto"
New-Item "$progId\shell\open\command" -Force | Out-Null
Set-Item $progId -Value "Link di posta (mailto:)"
New-ItemProperty $progId -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null
New-Item "$progId\DefaultIcon" -Force | Out-Null
Set-Item "$progId\DefaultIcon" -Value "$exe,0"
# --mailto davanti: Windows mette il link al posto di %1 senza toccare le virgolette, e un link con dentro una virgoletta
# arriva spezzato in più argomenti. Dopo --mailto Missiva prende solo il link e non legge nient'altro come un'opzione.
Set-Item "$progId\shell\open\command" -Value "`"$exe`" --mailto `"%1`""
$capabilities = "HKCU:\Software\Missiva\Capabilities"
New-Item "$capabilities\URLAssociations" -Force | Out-Null
New-ItemProperty $capabilities -Name ApplicationName -Value "Missiva" -PropertyType String -Force | Out-Null
New-ItemProperty $capabilities -Name ApplicationDescription -Value "Posta, senza il nuovo Outlook" -PropertyType String -Force | Out-Null
New-ItemProperty $capabilities -Name ApplicationIcon -Value "$exe,0" -PropertyType String -Force | Out-Null
New-ItemProperty "$capabilities\URLAssociations" -Name mailto -Value "Missiva.Mailto" -PropertyType String -Force | Out-Null
# RegisteredApplications è di tutte le app: si crea solo se manca (New-Item -Force la rifarebbe vuota).
$registered = "HKCU:\Software\RegisteredApplications"
if (-not (Test-Path $registered)) { New-Item $registered | Out-Null }
New-ItemProperty $registered -Name Missiva -Value "Software\Missiva\Capabilities" -PropertyType String -Force | Out-Null

Write-Host "Missiva $described installata in $dir. La trovi nel menu Start."
if ($previous) { Write-Host "Prima c'era Missiva $previous." }
Write-Host "Per aprire con Missiva i link mailto: sceglila in Impostazioni, App, App predefinite, Missiva."
