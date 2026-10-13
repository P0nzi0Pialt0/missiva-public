param([Parameter(Mandatory)][string]$Button, [string]$Window = "Missiva")
# Preme un pulsante (per nome o testo) in una finestra della versione di prova, con UI Automation. Vale anche per le
# schede (Home, Visualizza), che si selezionano.
# Solo pulsanti che non modificano la casella di posta: niente Elimina, Archivia, Sposta, Invia e simili.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$pid0 = (Get-Process Missiva | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | Sort-Object { $_.MainWindowHandle -eq 0 } | Select-Object -First 1).Id
if (-not $pid0) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $pid0
$win = $null
for ($i = 0; $i -lt 20 -and -not $win; $i++) {
    $win = $A::RootElement.FindAll($T::Descendants, $cond) |
        Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -eq $Window } | Select-Object -First 1
    if (-not $win) { Start-Sleep -Milliseconds 300 }
}
if (-not $win) { throw "finestra '$Window' non trovata" }
$buttons = $win.FindAll($T::Descendants, (New-Object System.Windows.Automation.OrCondition (
    (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Button)),
    (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::RadioButton)))))
$target = $buttons | Where-Object { $_.Current.Name -like "*$Button*" -or ($_.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq $Button }) } | Select-Object -First 1
if (-not $target) { throw "pulsante '$Button' non trovato" }
if ($target.Current.ControlType -eq [System.Windows.Automation.ControlType]::RadioButton) {
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
} else {
    $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
"premuto '$Button'"
