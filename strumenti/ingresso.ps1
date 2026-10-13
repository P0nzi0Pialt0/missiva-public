param(
    [ValidateSet("rotella", "comando", "nessuno")][string]$Gesto = "rotella",
    [ValidateSet("tasto", "clic", "niente")][string]$Poi = "tasto",
    [string]$Ritardi = "40,120,250,500,900",
    [int]$Tacche = 30, [int]$Passo = 25, [int]$Delta = -120,
    [string]$Comando = "", [int]$DopoComando = 1500,
    [int]$Tasto = 0x7E, [double]$Pausa = 2.8, [switch]$Durante, [string]$Prima = "cima", [string]$Insieme = "",
    [string]$Keys = $env:MISSIVA_KEYS
)
# Tasti e clic VERI (SendInput) verso la finestra della versione di prova mentre l'elenco scorre o subito dopo, con il
# ritardo fra l'invio e l'arrivo a PreviewKeyDown o PreviewMouseDown (audit del 7 ottobre, B-04). La prova va avviata con
# MISSIVA_INGRESSO=1 (Core\InputProbe.cs scrive l'istante dell'arrivo con lo stesso orologio) e con MISSIVA_KEYS.
#   -Gesto rotella: tacche vere della rotella con SendInput sul punto dell'elenco (-Tacche, -Passo ms, -Delta; -Delta -30 e
#                   -Passo 8 somigliano a un touchpad); -Gesto comando: un comando di MISSIVA_KEYS (-Comando "replay
#                   rotella-vera-033456.csv" o "scorri 60"), e il tasto parte -DopoComando ms dopo averlo scritto;
#   -Poi tasto: un tasto (di base F15, che non fa nulla) -Ritardi ms dopo l'ultima tacca; -Poi clic: un clic sinistro sul
#               punto dell'elenco (apre il messaggio sotto il cursore: nella prova non lo segna letto);
#   -Durante: il tasto o il clic partono a metà del gesto invece che dopo;
#   -Prima: i comandi di MISSIVA_KEYS prima di ogni prova, separati da ";" (di base "cima"; con "apri posta 2;apri posta 1"
#           l'elenco si ricarica e le righe tornano mai misurate, come scorrendo in una casella grande);
#   -Insieme: un comando di MISSIVA_KEYS scritto appena prima del gesto (per esempio "down"): arriva durante il gesto, a
#             priorità Input, per il confronto con lo stesso tasto vero (-Poi tasto -Tasto 0x28 -Durante).
# Regole: si manda ingresso solo se la finestra in primo piano è quella della versione di prova (mai la Missiva installata);
# alla fine il primo piano e il cursore tornano dov'erano. Il mouse si muove davvero: non usarlo mentre gira.
$ErrorActionPreference = "Stop"
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Diagnostics; using System.Threading;
public static class Ingresso {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] public struct U { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public U u; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, INPUT[] i, int size);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  public static uint Target; public static IntPtr TargetWnd;
  public static uint Owner(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static void Guard() { if (Owner(GetForegroundWindow()) != Target) throw new Exception("la finestra in primo piano non e' quella di prova: mi fermo"); }
  static void GuardPoint() {
    POINT p; GetCursorPos(out p); RECT r; GetWindowRect(TargetWnd, out r);
    if (p.X < r.L || p.X >= r.R || p.Y < r.T || p.Y >= r.B) throw new Exception("il cursore non e' sulla finestra di prova: mi fermo");
    if (Owner(GetAncestor(WindowFromPoint(p), 2)) != Target) throw new Exception("sotto il cursore c'e' un'altra finestra: mi fermo");
  }
  public static bool Activate(IntPtr h) {
    uint p; uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out p); uint me = GetCurrentThreadId();
    AttachThreadInput(me, fg, true); SetForegroundWindow(h); AttachThreadInput(me, fg, false);
    Thread.Sleep(200); return Owner(GetForegroundWindow()) == Owner(h);
  }
  static long Send(INPUT i) { Guard(); long t = Stopwatch.GetTimestamp(); if (SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Exception("SendInput non ha accettato l'ingresso (sessione bloccata?)"); return t; }
  public static void Move(int x, int y) {
    Guard(); RECT r; GetWindowRect(TargetWnd, out r);
    if (x < r.L || x >= r.R || y < r.T || y >= r.B) throw new Exception("punto fuori dalla finestra di prova: mi fermo");
    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    var i = new INPUT { type = 0 }; i.u.mi = new MOUSEINPUT { dx = (int)((long)x * 65535 / (w - 1)), dy = (int)((long)y * 65535 / (h - 1)), dwFlags = 0x8001 }; Send(i);
  }
  public static long Wheel(int delta) { GuardPoint(); var i = new INPUT { type = 0 }; i.u.mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = 0x800 }; return Send(i); }
  public static long Click() {
    GuardPoint(); var d = new INPUT { type = 0 }; d.u.mi = new MOUSEINPUT { dwFlags = 2 }; long t = Send(d);
    Wait(40); var u = new INPUT { type = 0 }; u.u.mi = new MOUSEINPUT { dwFlags = 4 }; Send(u); return t;
  }
  public static long Key(ushort vk) {
    var d = new INPUT { type = 1 }; d.u.ki = new KEYBDINPUT { vk = vk }; long t = Send(d);
    Wait(15); var u = new INPUT { type = 1 }; u.u.ki = new KEYBDINPUT { vk = vk, flags = 2 }; Send(u); return t;
  }
  public static void Wait(double ms) { var s = Stopwatch.StartNew(); while (s.Elapsed.TotalMilliseconds < ms) { if (ms - s.Elapsed.TotalMilliseconds > 3) Thread.Sleep(1); else Thread.SpinWait(200); } }
  // Il gesto e poi il tasto o il clic, tutto qui dentro per non perdere tempo fra un invio e l'altro: gli istanti dell'ultima tacca e dell'invio.
  public static long[] Trial(int notches, int every, int delta, double delay, int then, ushort vk, bool during) {
    long last = 0, sent = 0;
    for (int n = 0; n < notches; n++) {
      last = Wheel(delta);
      if (during && n == notches / 2) sent = then == 1 ? Key(vk) : then == 2 ? Click() : 0;
      if (n < notches - 1) Wait(every);
    }
    if (!during) { Wait(delay); sent = then == 1 ? Key(vk) : then == 2 ? Click() : 0; }
    return new long[] { last, sent };
  }
}
"@
[Ingresso]::SetProcessDPIAware() | Out-Null
$proc = Get-Process Missiva -ErrorAction SilentlyContinue | Where-Object { ($_.Path -like "*\bin\Debug\*" -or $_.Path -like "*\prova-release\*") -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw "La versione di prova non e' aperta (strumenti\prova.ps1, con MISSIVA_INGRESSO=1 e MISSIVA_KEYS)" }
if (-not $Keys) { throw "Serve il file di MISSIVA_KEYS (-Keys)" }
$log = 'C:\Progetti\Missiva\prova-dati\missiva.log'
$freq = [System.Diagnostics.Stopwatch]::Frequency
[Ingresso]::Target = [uint32]$proc.Id; [Ingresso]::TargetWnd = $proc.MainWindowHandle
function Nuove($da) { @(Get-Content $log | Select-Object -Skip $da) }

# Il punto dell'elenco in pixel dello schermo, lontano da azioni rapide e caselle (comando "punto elenco").
$da = (Get-Content $log).Count
Add-Content $Keys "punto elenco"; Start-Sleep -Milliseconds 700
$punto = Nuove $da | Where-Object { $_ -match 'punto elenco: (-?\d+) (-?\d+)' } | Select-Object -Last 1
if (-not $punto) { throw "La prova non ha risposto a 'punto elenco'" }
$null = $punto -match 'punto elenco: (-?\d+) (-?\d+)'; $px = [int]$Matches[1]; $py = [int]$Matches[2]

$davanti = [Ingresso]::GetForegroundWindow()
$cursore = New-Object Ingresso+POINT; [Ingresso]::GetCursorPos([ref]$cursore) | Out-Null
if (-not [Ingresso]::Activate($proc.MainWindowHandle)) { throw "Non riesco a portare in primo piano la finestra di prova (sessione bloccata?)" }
try {
    if ($Gesto -eq "rotella" -or $Poi -eq "clic") { [Ingresso]::Move($px, $py); Start-Sleep -Milliseconds 300 }
    $then = switch ($Poi) { "tasto" { 1 } "clic" { 2 } default { 0 } }
    foreach ($r in ($Ritardi -split ',')) {
        foreach ($c in ($Prima -split ';')) { Add-Content $Keys $c; Start-Sleep -Milliseconds 1200 }
        $da = (Get-Content $log).Count
        if ($Gesto -eq "comando") {
            Add-Content $Keys $Comando; [Ingresso]::Wait($DopoComando + [double]$r)
            $t = [Ingresso]::Trial(0, 0, 0, 0, $then, [uint16]$Tasto, $false)
        }
        elseif ($Gesto -eq "rotella") { if ($Insieme) { Add-Content $Keys $Insieme }; $t = [Ingresso]::Trial($Tacche, $Passo, $Delta, [double]$r, $then, [uint16]$Tasto, [bool]$Durante) }
        else { $t = [Ingresso]::Trial(0, 0, 0, [double]$r, $then, [uint16]$Tasto, $false) }
        Start-Sleep -Milliseconds ([int]($Pausa * 1000))
        $righe = Nuove $da
        $arrivo = $righe | Where-Object { $_ -match 'ingresso: (tasto|clic) .*qpc=(\d+)' } | Select-Object -First 1
        $testo = if ($Gesto -eq "rotella") { "$Tacche tacche da $Delta ogni $Passo ms, $Poi $(if ($Durante) { 'a meta' } else { "a +$r ms" })" } elseif ($Gesto -eq "comando") { "$Comando, $Poi a +$($DopoComando + [int]$r) ms dal comando" } else { "da fermo, $Poi" }
        if ($arrivo) {
            $null = $arrivo -match 'qpc=(\d+)'; $ms = ([long]$Matches[1] - $t[1]) * 1000.0 / $freq
            "{0}: arrivato dopo {1:0.0} ms | {2}" -f $testo, $ms, ($arrivo -replace '^\S+ \S+\s+ingresso: ', '' -replace 'qpc=\d+, ', '')
        }
        elseif ($then -ne 0) { "${testo}: NON ARRIVATO (nessuna riga 'ingresso:' nel registro)" }
        else { $testo }
        $righe | Where-Object { $_ -match 'ingresso: nel|coda del gesto|tempi: clic, testo|tempi: HTML pulito|corpo dalla copia|clic di prova|tasto di prova' } | ForEach-Object { "    " + ($_ -replace '^\S+ \S+\s+', '') }
    }
}
finally {
    [Ingresso]::SetCursorPos($cursore.X, $cursore.Y) | Out-Null
    if ($davanti -ne [IntPtr]::Zero) { [Ingresso]::Activate($davanti) | Out-Null }
}
