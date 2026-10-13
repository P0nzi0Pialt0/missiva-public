using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Missiva.UI;

/// <summary>
/// L'icona di Missiva nell'area di notifica, vicino all'orologio, come Outlook classico e Thunderbird: Shell_NotifyIconW
/// con una finestra nascosta che riceve i suoi messaggi (HwndSource, senza WinForms). Un clic apre la finestra, il tasto
/// destro il menu (Apri, Nuovo messaggio, Esci); il suggerimento dice quanti messaggi sono da leggere. Se Esplora risorse
/// riparte (la barra delle applicazioni si ricrea) l'icona si rimette da sola (TaskbarCreated).
/// </summary>
sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    const uint NOTIFYICON_VERSION_4 = 4;
    const int WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;

    readonly HwndSource window;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly IntPtr icon;
    string tip;
    bool added;

    public event Action? OpenRequested, NewMessageRequested, ExitRequested;

    /// <param name="name">Il nome nel suggerimento: "Missiva", o "Missiva (prova)" per la versione di prova accanto a quella installata.</param>
    public TrayIcon(string name)
    {
        tip = name;
        // Finestra di primo livello nascosta (non HWND_MESSAGE, che non riceve TaskbarCreated), senza pulsante nella barra.
        window = new HwndSource(new HwndSourceParameters(name + ", area di notifica") { WindowStyle = 0, ExtendedWindowStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
        window.AddHook(Hook);
        // La prima icona dell'eseguibile, alla misura piccola di Windows (quella dell'area di notifica).
        ExtractIconEx(Environment.ProcessPath ?? "", 0, IntPtr.Zero, out icon, 1);
        Add();
    }

    public string Tip
    {
        get => tip;
        set
        {
            if (tip == value) return;
            tip = value;
            if (added) Notify(NIM_MODIFY, NIF_TIP | NIF_SHOWTIP);
        }
    }

    void Add()
    {
        added = Notify(NIM_ADD, NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!added) return;
        // Versione 4: il tasto destro arriva come WM_CONTEXTMENU, il clic come NIN_SELECT, anche dalla tastiera.
        var data = Data(0);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    bool Notify(uint message, uint flags) { var data = Data(flags); return Shell_NotifyIconW(message, ref data); }

    NotifyIconData Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = icon,
        szTip = tip.Length > 127 ? tip[..127] : tip,
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch (unchecked((int)lParam.ToInt64()) & 0xFFFF)
            {
                case NIN_SELECT or NIN_KEYSELECT: // dopo WM_LBUTTONUP, che quindi non serve
                    OpenRequested?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == taskbarCreated && taskbarCreated != 0)
        {
            Log.Write("area di notifica ricreata (Esplora risorse è ripartito): icona rimessa");
            Add();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Il menu del tasto destro, dello stesso stile dei menu di Missiva (chiaro o scuro). Prima la finestra nascosta passa in
    /// primo piano, come vuole Windows per i menu dell'area di notifica: altrimenti un clic fuori non lo chiuderebbe.
    /// </summary>
    public void ShowMenu()
    {
        SetForegroundWindow(window.Handle);
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        MenuItem Item(string text, Action? action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action?.Invoke();
            menu.Items.Add(item);
            return item;
        }
        Item("Apri Missiva", OpenRequested).FontWeight = FontWeights.SemiBold;
        Item("Nuovo messaggio", NewMessageRequested);
        menu.Items.Add(new Separator());
        Item("Esci", ExitRequested);
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (added) Notify(NIM_DELETE, 0);
        added = false;
        window.RemoveHook(Hook);
        window.Dispose();
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
