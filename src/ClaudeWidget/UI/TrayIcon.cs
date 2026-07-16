using System.Drawing;
using System.Runtime.InteropServices;

namespace ClaudeWidget.UI;

public sealed class TrayIcon : IDisposable
{
    private static readonly Guid TrayIconGuid = new("7f3c9e1a-4b2d-4e6f-9a1c-3d5e7f9b1c2d");
    private static readonly int TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;
    private const int NIF_GUID = 0x00000020;
    private const int NIF_SHOWTIP = 0x00000080;

    private const int NOTIFYICON_VERSION_4 = 4;

    private const int WM_APP = 0x8000;
    private const int TrayCallbackMessage = WM_APP + 1;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string lpString);

    private sealed class TrayNativeWindow : NativeWindow
    {
        public event Action<Message>? MessageReceived;

        public TrayNativeWindow()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            MessageReceived?.Invoke(m);
            base.WndProc(ref m);
        }
    }

    private readonly TrayNativeWindow _window = new();
    private Icon? _icon;
    private string _text = "";
    private bool _added;

    public ContextMenuStrip? ContextMenuStrip { get; set; }
    public event MouseEventHandler? MouseClick;

    public Icon? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            if (_added) Modify(NIF_ICON);
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_added) Modify(NIF_TIP | NIF_SHOWTIP);
        }
    }

    public TrayIcon()
    {
        _window.MessageReceived += OnMessage;
        Add();
    }

    private NOTIFYICONDATAW BuildData(int flags)
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon?.Handle ?? IntPtr.Zero,
            szTip = _text,
            szInfo = "",
            szInfoTitle = "",
            guidItem = TrayIconGuid,
        };
    }

    private void Add()
    {
        var data = BuildData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP | NIF_GUID);
        Shell_NotifyIconW(NIM_ADD, ref data);
        _added = true;

        var versionData = BuildData(NIF_GUID);
        versionData.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref versionData);
    }

    private void Modify(int flags)
    {
        var data = BuildData(flags | NIF_GUID);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void ShowBalloonTip(int timeoutMs, string title, string text)
    {
        var data = BuildData(NIF_INFO | NIF_GUID);
        data.szInfo = text;
        data.szInfoTitle = title;
        data.uTimeoutOrVersion = timeoutMs;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void OnMessage(Message m)
    {
        if (m.Msg == TaskbarCreatedMessage)
        {
            Add();
            return;
        }
        if (m.Msg != TrayCallbackMessage) return;

        var evt = (int)(m.LParam.ToInt64() & 0xFFFF);
        if (evt == WM_LBUTTONUP)
        {
            MouseClick?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, Cursor.Position.X, Cursor.Position.Y, 0));
        }
        else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU)
        {
            SetForegroundWindow(_window.Handle);
            ContextMenuStrip?.Show(Cursor.Position);
        }
    }

    public void Dispose()
    {
        var data = BuildData(NIF_GUID);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _window.DestroyHandle();
    }
}
