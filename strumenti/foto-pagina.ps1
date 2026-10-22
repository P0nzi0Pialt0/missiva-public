param([string[]]$Solo = @("principale", "scuro", "scrittura", "pec", "calendario", "persone"), [string]$Uscita = "", [string]$Lavoro = "",
      [ValidateSet("it", "en")][string]$Lingua = "it")
# Le foto della pagina GitHub (README.md, docs\immagini) con i dati finti di Core\ProvaBozze.Foto.cs: account, persone e
# messaggi inventati, domini .example, server IMAP finti su 127.0.0.1 (--imap-finto porta foto studio|personale|pec), mai
# la posta vera né la rete. Va compilata prima la versione di Debug (dotnet build). Uso:
#   pwsh -File strumenti\foto-pagina.ps1 [-Solo principale,scuro,scrittura,pec,calendario,persone] [-Uscita cartella] [-Lavoro cartella] [-Lingua it|en]
# Con -Lingua en le foto della pagina inglese (README.en.md): Missiva in inglese (--lingua en), i server finti con le
# caselle inglesi (foto studio en), le foto in docs\immagini\en con i nomi inglesi (main-light.png, main-dark.png,
# compose.png, calendar.png, people.png, security.png, certified-email.png).
# Ogni foto ha la sua prova, avviata con --foto su una cartella dei dati nuova e chiusa appena fatta la foto: la finestra
# nasce fuori dallo schermo, senza il fuoco e senza pulsante nella barra delle applicazioni (l'icona nell'area di
# notifica c'è finché la prova è aperta); notifiche, loghi e aggiornamenti spenti nelle impostazioni finte.
# La foto è quello che PrintWindow vede (sfondo di Windows, pulsanti della finestra, WebView2) con sopra la parte di WPF
# che la prova scrive da sé ("foto cartella" in MISSIVA_KEYS): fuori dallo schermo WPF non la mostra a DWM.
# Le foto vanno in -Uscita (di base docs\immagini, o docs\immagini\en con -Lingua en), larghe al più 1600 pixel; quelle
# intere della finestra (2640 pixel sullo schermo dell'autore, al 200%) e i dati delle prove restano in una cartella
# nuova dentro -Lavoro (di base i file temporanei).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$exe = Join-Path $root "bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe"
if (-not (Test-Path $exe)) { throw "manca la versione di Debug: dotnet build" }
if (-not $Uscita) { $Uscita = Join-Path $root ($Lingua -eq "en" ? "docs\immagini\en" : "docs\immagini") }
$english = $Lingua -eq "en"
# I nomi delle foto nella lingua della pagina: il README italiano usa i nomi italiani, quello inglese i nomi inglesi.
$nomi = $english ? @{ "principale-chiaro.png" = "main-light.png"; "principale-scuro.png" = "main-dark.png"; "scrittura.png" = "compose.png"; "calendario.png" = "calendar.png"
                      "persone.png" = "people.png"; "sicurezza.png" = "security.png"; "posta-certificata.png" = "certified-email.png" } : @{}
function Get-Nome([string]$Nome) { $nomi.ContainsKey($Nome) ? $nomi[$Nome] : $Nome }
# Le caselle delle foto (come ProvaBozze.FotoMailboxes e FotoMailboxesEn) e le categorie dei loro messaggi.
$domini = $english ? @{ studio = "northlight"; personale = "homemail"; pec = "pec-esempio" } : @{ studio = "studio-esempio"; personale = "posta-esempio"; pec = "pec-esempio" }
$categorie = $english ? @{ studio = @("Clients", "Accounting"); personale = @("Home") } : @{ studio = @("Clienti", "Amministrazione"); personale = @("Casa") }
# La domanda di una scrittura aperta all'uscita, e il suo pulsante Scarta, come li scrive Missiva (Assets\lingue\en).
$domanda = $english ? "Save the draft?" : "Salvare la bozza?"
$scarta = $english ? "Discard" : "Scarta"
New-Item -ItemType Directory -Force $Uscita | Out-Null
$work = Join-Path ($Lavoro ? $Lavoro : [IO.Path]::GetTempPath()) ("missiva-foto-$Lingua-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory $work | Out-Null
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class FotoPagina {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint f);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static List<KeyValuePair<IntPtr, string>> Windows(uint pid) {
    var list = new List<KeyValuePair<IntPtr, string>>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); list.Add(new KeyValuePair<IntPtr, string>(h, sb.ToString())); }
      return true; }, IntPtr.Zero);
    return list;
  }
  public static List<RECT> Children(IntPtr parent) {
    var list = new List<RECT>();
    EnumChildWindows(parent, (h, l) => { if (GetParent(h) == parent && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); list.Add(r); } return true; }, IntPtr.Zero);
    return list;
  }
  public static RECT Frame(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }
}
"@
[FotoPagina]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null # pixel fisici

# Le prove di questo eseguibile, e nessun altro processo (mai la Missiva installata).
function Get-Prove { @(Get-CimInstance Win32_Process -Filter "Name = 'Missiva.exe'" | Where-Object { $_.ExecutablePath -eq $exe }) }
if ((Get-Prove).Count -gt 0) { throw "c'è già una prova aperta di questo eseguibile: chiudila prima (prova.ps1 -Stop)" }

$ids = @{ studio = "5a1d0c3e9b7f4e2a8c6d1f0b3e5a7c90"; personale = "7c2e4a6b8d0f1e3a5c7b9d1f2e4a6c80"; pec = "9e4b6d8f0a2c4e6b8d0f2a4c6e8b0d20"; microsoft = "3b5d7f9a1c3e5b7d9f1a3c5e7b9d1f30" }
$ports = @{ studio = 14300; personale = 14310; pec = 14320 }
$script:app = $null; $script:data = $null

function Start-Foto([string]$Nome, [string[]]$Caselle = @(), [switch]$Microsoft, [string]$Tema = "Light", [string]$Argomenti = "") {
    # La prova: i server finti delle caselle, le impostazioni finte, la finestra con --foto.
    $script:data = Join-Path $work $Nome
    New-Item -ItemType Directory $script:data | Out-Null
    $env:MISSIVA_IMAP_FINTO = "1"; $env:MISSIVA_NO_READ = "1"
    foreach ($c in $Caselle) {
        $env:MISSIVA_DATA = Join-Path $script:data "server-$c"
        New-Item -ItemType Directory $env:MISSIVA_DATA | Out-Null
        Start-Process $exe -ArgumentList (@("--imap-finto", $ports[$c], "foto", $c) + ($english ? @("en") : @())) -WindowStyle Hidden | Out-Null
    }
    foreach ($c in $Caselle) {
        for ($i = 0; $i -lt 100; $i++) {
            $tcp = New-Object System.Net.Sockets.TcpClient
            try { $tcp.Connect("127.0.0.1", $ports[$c]); break } catch { Start-Sleep -Milliseconds 200 } finally { $tcp.Dispose() }
        }
    }
    $password = [Convert]::ToBase64String([System.Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes("foto"), $null, 'CurrentUser'))
    $accounts = @()
    if ($Microsoft) { $accounts += [ordered]@{ Id = $ids.microsoft; Kind = "Microsoft"; Email = "ada.galli@$($domini.studio).example"; DisplayName = "Ada Galli" } }
    foreach ($c in $Caselle) {
        $email = "ada.galli@$($domini[$c]).example"
        $accounts += [ordered]@{ Id = $ids[$c]; Kind = "Imap"; Email = $email; DisplayName = "Ada Galli"
            Imap = [ordered]@{ ImapHost = "127.0.0.1"; ImapPort = $ports[$c]; ImapSecurity = "Ssl"; SmtpHost = "127.0.0.1"; SmtpPort = $ports[$c] + 1; SmtpSecurity = "StartTls"; UserName = $email; ProtectedPassword = $password } }
    }
    $settings = [ordered]@{
        Accounts = $accounts; Theme = $Tema; NotificationsOff = @($accounts | ForEach-Object { $_.Id }); NotificationSound = $false
        KeepInTray = $false; TrayExplained = $true; UpdatesAuto = $false; ShowSenderLogos = $false; StartWithWindows = $false
        LocalCategories = @{ ($ids.studio) = $categorie.studio; ($ids.personale) = $categorie.personale }
        CategoryColors = @{ "$($ids.studio)|$($categorie.studio[0])" = 7; "$($ids.studio)|$($categorie.studio[1])" = 4; "$($ids.personale)|$($categorie.personale[0])" = 1 }
    }
    $settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $script:data "impostazioni.json") -Encoding utf8NoBOM
    if ($Caselle -contains "pec") {
        $cer = Join-Path $script:data "server-pec\radice-foto.cer"
        for ($i = 0; $i -lt 50 -and -not (Test-Path $cer); $i++) { Start-Sleep -Milliseconds 200 }
        $Argomenti = "$Argomenti --pec-radice `"$cer`""
    }
    $env:MISSIVA_DATA = $script:data
    $env:MISSIVA_KEYS = Join-Path $script:data "tasti.txt"
    $script:app = Start-Process $exe -ArgumentList "--foto --lingua $Lingua $Argomenti".Trim() -PassThru
    for ($i = 0; $i -lt 200 -and -not (Get-Finestra "Missiva" -Quiet); $i++) { Start-Sleep -Milliseconds 50 }
    Start-Sleep 5 # account, cartelle, elenco e WebView2
}

function Send-Tasto([string]$Riga, [int]$Attesa = 3) { Set-Content $env:MISSIVA_KEYS $Riga; Start-Sleep $Attesa }

function Get-Finestra([string]$Titolo, [switch]$Quiet) {
    $w = [FotoPagina]::Windows([uint32]$script:app.Id) | Where-Object { $_.Value -like $Titolo } | Select-Object -First 1
    if (-not $w -and -not $Quiet) { throw "finestra '$Titolo' non trovata" }
    if ($w) { $w.Key }
}

function Set-Dimensione([string]$Titolo, [int]$Larghezza, [int]$Altezza) {
    # In DIP, come WPF: la cornice visibile di quella misura (i bordi invisibili si aggiungono).
    $h = Get-Finestra $Titolo
    $scale = [FotoPagina]::GetDpiForWindow($h) / 96.0
    $r = New-Object FotoPagina+RECT; [FotoPagina]::GetWindowRect($h, [ref]$r) | Out-Null
    $f = [FotoPagina]::Frame($h)
    [FotoPagina]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, [int]($Larghezza * $scale) + ($r.R - $r.L) - ($f.R - $f.L), [int]($Altezza * $scale) + ($r.B - $r.T) - ($f.B - $f.T), 0x0016) | Out-Null # NOMOVE, NOZORDER, NOACTIVATE
    Start-Sleep 2
}

function Save-Finestra([string]$Titolo, [string]$Out) {
    # PrintWindow della cornice visibile, con sopra la parte di WPF scritta dalla prova e sopra ancora le finestre figlie (WebView2).
    $h = Get-Finestra $Titolo
    $folder = Join-Path $script:data ("wpf-" + (Get-Date -Format "HHmmssfff"))
    $wpf = Join-Path $folder ("{0:X}.png" -f $h.ToInt64())
    Send-Tasto "foto $folder" 0
    for ($i = 0; $i -lt 50 -and -not (Test-Path $wpf); $i++) { Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path $wpf)) { throw "la prova non ha scritto $wpf" }
    $r = New-Object FotoPagina+RECT; [FotoPagina]::GetWindowRect($h, [ref]$r) | Out-Null
    $f = [FotoPagina]::Frame($h)
    $full = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($full); $dc = $g.GetHdc()
    [FotoPagina]::PrintWindow($h, $dc, 2) | Out-Null # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($dc)
    $shot = $full.Clone()
    $top = [System.Drawing.Image]::FromFile($wpf)
    $origin = New-Object FotoPagina+POINT; [FotoPagina]::ClientToScreen($h, [ref]$origin) | Out-Null
    $g.DrawImage($top, ($origin.X - $r.L), ($origin.Y - $r.T), $top.Width, $top.Height)
    foreach ($c in [FotoPagina]::Children($h)) {
        $area = New-Object System.Drawing.Rectangle ($c.L - $r.L), ($c.T - $r.T), ($c.R - $c.L), ($c.B - $c.T)
        if ($area.Width -gt 0 -and $area.Height -gt 0) { $g.DrawImage($shot, $area, $area, [System.Drawing.GraphicsUnit]::Pixel) }
    }
    $g.Dispose(); $shot.Dispose(); $top.Dispose()
    $bmp = $full.Clone((New-Object System.Drawing.Rectangle ($f.L - $r.L), ($f.T - $r.T), ($f.R - $f.L), ($f.B - $f.T)), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $full.Dispose()
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "fotografata '$Titolo' in $Out"
}

function Stop-Foto {
    # "esci", come Esci dall'area di notifica (l'icona se ne va pulita); se una scrittura chiede che cosa fare della bozza,
    # Scarta nella domanda (con UI Automation: è fuori dallo schermo); poi i server finti.
    if ($script:app -and -not $script:app.HasExited) {
        Send-Tasto "esci" 0
        for ($i = 0; $i -lt 12 -and -not $script:app.HasExited; $i++) { Start-Sleep -Milliseconds 250 }
        if (-not $script:app.HasExited) {
            $A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
            $question = $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $script:app.Id)) |
                ForEach-Object { $_; $_.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Window))) } |
                Where-Object { $_.Current.Name -eq "Missiva" -and $_.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::NameProperty, $domanda)) } | Select-Object -First 1
            $discard = if ($question) {
                $question.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, ([System.Windows.Automation.ControlType]::Button))) |
                    Where-Object { $_.Current.Name -eq $scarta } | Select-Object -First 1
            }
            if ($discard) { $discard.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
            for ($i = 0; $i -lt 28 -and -not $script:app.HasExited; $i++) { Start-Sleep -Milliseconds 250 }
        }
    }
    foreach ($p in Get-Prove) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep 1
}

# ---------------------------------------------------------------- immagini

function Save-Ridotta([System.Drawing.Bitmap]$Bitmap, [string]$Out, [int]$Massimo = 1600) {
    $w = $Bitmap.Width; $h = $Bitmap.Height
    if ($w -gt $Massimo) { $h = [int][Math]::Round($h * $Massimo / $w); $w = $Massimo }
    $small = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
    $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY) # bordi senza alone
    $g.DrawImage($Bitmap, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), 0, 0, $Bitmap.Width, $Bitmap.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    $g.Dispose()
    $small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
    "salvata $Out (${w}x$h, $([Math]::Round((Get-Item $Out).Length / 1KB)) KB)"
}

function Open-Png([string]$Path) { $b = [System.Drawing.Bitmap]::FromFile($Path); $c = New-Object System.Drawing.Bitmap $b; $b.Dispose(); $c }

function Save-Pagina([string]$Grezza, [string]$Nome, [double[]]$Ritaglio = @()) {
    # Ritaglio: sinistra, alto, larghezza e altezza in frazioni della foto (le misure non dipendono dalla scala dello schermo).
    $b = Open-Png $Grezza
    if ($Ritaglio.Count -eq 4) {
        $area = New-Object System.Drawing.Rectangle ([int]($Ritaglio[0] * $b.Width)), ([int]($Ritaglio[1] * $b.Height)), ([int]($Ritaglio[2] * $b.Width)), ([int]($Ritaglio[3] * $b.Height))
        $c = $b.Clone($area, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb); $b.Dispose(); $b = $c
    }
    Save-Ridotta $b (Join-Path $Uscita (Get-Nome $Nome))
    $b.Dispose()
}

function Save-Sopra([string]$Sotto, [string]$Sopra, [string]$Nome, [double]$Sinistra, [double]$Alto) {
    # Una finestra sopra l'altra (la posizione in frazioni di quella sotto), con l'ombra e gli angoli arrotondati delle finestre di Windows 11.
    $back = Open-Png $Sotto; $top = Open-Png $Sopra
    $X = [int]($Sinistra * $back.Width); $Y = [int]($Alto * $back.Height)
    $g = [System.Drawing.Graphics]::FromImage($back)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    for ($i = 28; $i -ge 1; $i--) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int](70 * [Math]::Pow((29 - $i) / 28.0, 2) / 10), 0, 0, 0))
        $g.FillRectangle($brush, $X - $i, $Y - $i + 12, $top.Width + 2 * $i, $top.Height + 2 * $i)
        $brush.Dispose()
    }
    $radius = 16
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, 2 * $radius, 2 * $radius, 180, 90)
    $path.AddArc($X + $top.Width - 2 * $radius, $Y, 2 * $radius, 2 * $radius, 270, 90)
    $path.AddArc($X + $top.Width - 2 * $radius, $Y + $top.Height - 2 * $radius, 2 * $radius, 2 * $radius, 0, 90)
    $path.AddArc($X, $Y + $top.Height - 2 * $radius, 2 * $radius, 2 * $radius, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path); $g.DrawImage($top, $X, $Y, $top.Width, $top.Height); $g.ResetClip()
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 0, 0, 0)), 2
    $g.DrawPath($pen, $path)
    $g.Dispose(); $top.Dispose()
    Save-Ridotta $back (Join-Path $Uscita (Get-Nome $Nome))
    $back.Dispose()
}

# ---------------------------------------------------------------- le foto

try {
    if ($Solo -contains "principale") {
        # La Posta in arrivo di tutti gli account con la newsletter aperta; poi la frode sulle coordinate bancarie, quando
        # la rubrica ha letto la Posta inviata (il sosia, il nome di Luca Bianchi da un altro indirizzo, si vede solo allora).
        Start-Foto principale studio, personale
        Send-Tasto "apri tutti"; Send-Tasto "clic 0" 5
        Save-Finestra "Missiva" "$work\principale-chiaro.png"
        $log = Join-Path $script:data "missiva.log"
        for ($i = 0; $i -lt 60 -and -not (Select-String -Path $log -Pattern "rubrica: .* $($domini.personale)" -Quiet); $i++) { Start-Sleep -Milliseconds 500 }
        Send-Tasto "clic 3" 5
        Save-Finestra "Missiva" "$work\sicurezza.png"
        Stop-Foto
        Save-Pagina "$work\principale-chiaro.png" "principale-chiaro.png"
        Save-Pagina "$work\sicurezza.png" "sicurezza.png" @(0.55, 0.1463, 0.4402, 0.8372) # il riquadro di lettura
    }
    if ($Solo -contains "scuro") {
        Start-Foto scuro studio, personale -Tema Dark
        Send-Tasto "apri tutti"; Send-Tasto "clic 0" 5
        Save-Finestra "Missiva" "$work\principale-scuro.png"
        Stop-Foto
        Save-Pagina "$work\principale-scuro.png" "principale-scuro.png"
    }
    if ($Solo -contains "scrittura") {
        Start-Foto scrittura studio, personale -Argomenti "--formato-finto"
        Start-Sleep 1
        Set-Dimensione "Re: *" 800 620
        Save-Finestra "Re: *" "$work\scrittura.png"
        Stop-Foto
        Save-Pagina "$work\scrittura.png" "scrittura.png"
    }
    if ($Solo -contains "pec") {
        Start-Foto pec studio, pec
        Send-Tasto "apri posta 2"; Send-Tasto "clic 0" 5
        Save-Finestra "Missiva" "$work\pec-messaggio.png"
        Send-Tasto "allegato 0" 4
        Set-Dimensione "IT0*" 760 470
        Save-Finestra "IT0*" "$work\pec-fattura.png"
        Stop-Foto
        Save-Sopra "$work\pec-messaggio.png" "$work\pec-fattura.png" "posta-certificata.png" 0.3977 0.4207
    }
    if ($Solo -contains "calendario") {
        Start-Foto calendario -Microsoft -Argomenti "--calendario --calendario-finto"
        Start-Sleep 2
        Save-Finestra "Missiva" "$work\calendario.png"
        Stop-Foto
        Save-Pagina "$work\calendario.png" "calendario.png"
    }
    if ($Solo -contains "persone") {
        # Luca Bianchi, la scheda più ricca: in italiano la sesta persona dell'elenco, in inglese la settima (ContactSamples.Photo).
        Start-Foto persone -Microsoft -Argomenti "--persone --contatti-finti --persona $($english ? 6 : 5)"
        Start-Sleep 2
        Save-Finestra "Missiva" "$work\persone.png"
        Stop-Foto
        Save-Pagina "$work\persone.png" "persone.png"
    }
}
finally {
    foreach ($p in Get-Prove) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
}
"foto intere e dati delle prove in $work"
