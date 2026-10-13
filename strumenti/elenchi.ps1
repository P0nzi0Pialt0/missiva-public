param([int]$List = -1, [int]$Index = 0, [int[]]$Messages = @(), [int]$Pause = 3)
# UI Automation sulla versione di prova. Senza parametri elenca le liste della finestra principale (preferiti, cartelle
# di ogni account, messaggi) con le prime voci. -List N -Index I seleziona la voce I della lista N (per esempio una
# cartella). -Messages 0,1,2 seleziona quei messaggi dell'elenco uno dopo l'altro (pausa -Pause secondi) e stampa le
# righe "tempi:" scritte nel registro nel frattempo.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$log = "C:\Progetti\Missiva\prova-dati\missiva.log"
$pid0 = (Get-Process Missiva | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | Sort-Object { $_.MainWindowHandle -eq 0 } | Select-Object -First 1).Id
if (-not $pid0) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$main = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.AndCondition (
    (New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $pid0),
    (New-Object System.Windows.Automation.PropertyCondition $A::NameProperty, "Missiva"))))
$listCond = New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::List)
$itemCond = New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::ListItem)
$textCond = New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Text)
# Solo le liste di WPF: quelle delle email nel riquadro (elenchi puntati HTML) cambierebbero la numerazione. E non le
# pastiglie di allegati e categorie dentro le righe dei messaggi, che UI Automation vede come liste.
$lists = @($main.FindAll($T::Descendants, $listCond) | Where-Object { $_.Current.FrameworkId -eq "WPF" })
$inRows = @{}
foreach ($l in $lists | Where-Object { $_.Current.AutomationId -eq "MessageList" }) {
    foreach ($inner in $l.FindAll($T::Descendants, $listCond)) { $inRows[($inner.GetRuntimeId() -join ".")] = $true }
}
$lists = @($lists | Where-Object { -not $inRows.ContainsKey(($_.GetRuntimeId() -join ".")) })

if ($Messages.Count -gt 0) {
    $start = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
    $messageList = $lists | Where-Object { $_.Current.AutomationId -eq "MessageList" } | Select-Object -First 1
    foreach ($i in $Messages) {
        $items = $messageList.FindAll($T::Descendants, $itemCond)
        "--- messaggio $i di $($items.Count)"
        $items[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Seconds $Pause
    }
    if (Test-Path $log) { Get-Content $log | Select-Object -Skip $start | Where-Object { $_ -match "tempi:|ERRORE" } }
    return
}

for ($k = 0; $k -lt $lists.Count; $k++) {
    # I messaggi con le intestazioni per data stanno dentro i gruppi, non direttamente nella lista.
    $scope = if ($lists[$k].Current.AutomationId -eq "MessageList") { $T::Descendants } else { $T::Children }
    $items = $lists[$k].FindAll($scope, $itemCond)
    $texts = @($items | Select-Object -First 4 | ForEach-Object {
        ($_.FindAll($T::Descendants, $textCond) | Select-Object -First 2 | ForEach-Object { $_.Current.Name }) -join " " })
    "lista $k ($($lists[$k].Current.AutomationId)): $($items.Count) voci: $($texts -join ' | ')"
}
if ($List -ge 0) {
    $items = $lists[$List].FindAll($T::Children, $itemCond)
    $items[$Index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    "selezionata la voce $Index della lista $List"
}
