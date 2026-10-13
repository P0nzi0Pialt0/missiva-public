param([Parameter(Mandatory)][string]$Text, [string]$Field = "SearchBox", [string]$Window = "Missiva")
# Scrive un testo in una casella della versione di prova (per nome, di base la ricerca), dandole prima il fuoco, con
# UI Automation. Non preme Invio. Attenzione: dopo il SetValue la prova può restare ferma per 2-5 secondi (UI Automation
# tiene occupata l'interfaccia), quindi i tempi misurati subito dopo sono falsi. Per i tempi dei suggerimenti meglio
# MISSIVA_KEYS con le righe "ctrl+e" e poi "testo parole" (vedi Core\DevTools.cs).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$pid0 = (Get-Process Missiva | Where-Object { $_.Path -like "*bin\Debug*" -or $_.Path -like "*\prova-release\*" } | Sort-Object { $_.MainWindowHandle -eq 0 } | Select-Object -First 1).Id
if (-not $pid0) { throw "La versione di prova non è aperta: avviala con strumenti\prova.ps1" }
$win = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.AndCondition (
    (New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $pid0),
    (New-Object System.Windows.Automation.PropertyCondition $A::NameProperty, $Window))))
if (-not $win) { throw "finestra '$Window' non trovata" }
$box = $win.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::AutomationIdProperty, $Field))
if (-not $box) { throw "casella '$Field' non trovata" }
$box.SetFocus()
$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
"scritto '$Text' in $Field"
