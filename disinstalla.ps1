# Disinstalla Missiva: toglie il programma, il collegamento nel menu Start, la voce in App installate, Missiva fra le
# app di posta (link mailto:) e l'avvio all'accesso a Windows.
# Impostazioni, account e registro restano in %LOCALAPPDATA%\Missiva, per un'eventuale reinstallazione.
# Gira con Windows PowerShell 5.1 (powershell.exe, quello che c'è in ogni Windows e che App installate avvia) e con
# PowerShell 7. Il file è UTF-8 con BOM: senza, Windows PowerShell lo leggerebbe con la codifica locale e storpierebbe le
# lettere accentate dei messaggi. Chi lo modifica lo salvi con il BOM.
$ErrorActionPreference = "Stop"
$dir = Join-Path $env:LOCALAPPDATA "Programs\Missiva"

# I messaggi in italiano se la lingua di visualizzazione di Windows è l'italiano, altrimenti in inglese, come Missiva con
# "Come Windows" (Missiva in inglese, 21 ottobre 2026). Solo per provare lo script: MISSIVA_PROVA_LINGUA=it o en sceglie
# la lingua dei messaggi senza toccare le impostazioni di Windows.
$italiano = (Get-UICulture).TwoLetterISOLanguageName -eq "it"
if ($env:MISSIVA_PROVA_LINGUA -in "it", "en") { $italiano = $env:MISSIVA_PROVA_LINGUA -eq "it" }
function Testo([string]$it, [string]$en) { if ($italiano) { $it } else { $en } }

# Lo script sta nella cartella da cancellare: si copia altrove e riparte da lì, con lo stesso PowerShell che lo sta eseguendo.
if ($PSCommandPath -like "$dir\*") {
    $copy = Join-Path $env:TEMP "missiva-disinstalla.ps1"
    Copy-Item $PSCommandPath $copy -Force
    Start-Process (Get-Process -Id $PID).Path -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$copy`""
    exit
}

# Il canale di Missiva è solo per questo utente (lo crea così Missiva). L'opzione CurrentUserOnly dal lato di chi si collega
# c'è solo in PowerShell 7 (.NET): con Windows PowerShell 5.1 ci si collega senza, e "esci" arriva lo stesso.
$pipeOptions = [System.IO.Pipes.PipeOptions]::None
if ([enum]::GetNames([System.IO.Pipes.PipeOptions]) -contains "CurrentUserOnly") { $pipeOptions = [System.IO.Pipes.PipeOptions]"CurrentUserOnly" }

# Missiva aperta, anche nascosta nell'area di notifica: "esci" sul suo canale, come in installa.ps1.
Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" } | ForEach-Object {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "Missiva-mailto-$($_.SessionId)", [System.IO.Pipes.PipeDirection]::Out, $pipeOptions)
        $pipe.Connect(3000)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $writer.WriteLine("esci")
        $writer.Flush()
        $pipe.Dispose()
    } catch { }
    if (-not $_.WaitForExit(30000)) {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(10000)) { $_.Kill() }
    }
}
Start-Sleep -Milliseconds 500
# Avvia Missiva all'accesso a Windows (Impostazioni, Generali, Avvio e chiusura): si toglie il suo valore in Run.
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name Missiva -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "Missiva.lnk") -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Missiva" -Recurse -ErrorAction SilentlyContinue
# Missiva come app di posta per i link mailto: (installa.ps1): in RegisteredApplications si toglie solo il suo valore.
Remove-ItemProperty "HKCU:\Software\RegisteredApplications" -Name Missiva -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Missiva" -Recurse -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Classes\Missiva.Mailto" -Recurse -ErrorAction SilentlyContinue
# Il profilo dell'anteprima protetta (AppContainer Missiva.Anteprima, che Missiva crea alla prima anteprima di un PDF o
# di un'immagine): si toglie con la funzione di Windows, che cancella anche la sua cartella e il suo registro.
try {
    Add-Type -Namespace MissivaDisinstalla -Name Profilo -MemberDefinition '[DllImport("userenv.dll", CharSet = CharSet.Unicode)] public static extern int DeleteAppContainerProfile(string name);'
    [MissivaDisinstalla.Profilo]::DeleteAppContainerProfile("Missiva.Anteprima") | Out-Null
} catch { }
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host (Testo "Missiva è stata disinstallata. Le impostazioni restano in $env:LOCALAPPDATA\Missiva." "Missiva has been uninstalled. Its settings remain in $env:LOCALAPPDATA\Missiva.")
Start-Sleep -Seconds 3
