using System.Runtime.InteropServices;

namespace QuickMessage;

/// <summary>Win32 托盘图标：左键显示窗口，右键弹出菜单。</summary>
internal sealed class TrayIconHost : IDisposable
{
    private const int TrayId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint WmNull = 0x0000;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const int GwlpWndProc = -4;
    private const uint WsPopup = 0x80000000;
    private const uint MfString = 0x0000;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int NimAdd = 0;
    private const int NimDelete = 2;
    private const int MenuShow = 1;
    private const int MenuExit = 2;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr proc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr itemId, string text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly WndProc _wndProc;
    private readonly Action _onShow;
    private readonly Action _onExit;
    private readonly uint _taskbarCreated;
    private IntPtr _hwnd;
    private IntPtr _oldProc;
    private IntPtr _icon;
    private IntPtr _menu;

    public TrayIconHost(Action onShow, Action onExit)
    {
        _onShow = onShow;
        _onExit = onExit;
        _wndProc = WindowProc;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>创建隐藏的接收窗口并添加托盘图标，返回是否添加成功。</summary>
    public bool Start()
    {
        _hwnd = CreateWindowExW(0, "STATIC", "QuickMessageTray", WsPopup, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        _oldProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));
        _icon = LoadTrayIcon();

        _menu = CreatePopupMenu();
        AppendMenuW(_menu, MfString, (IntPtr)MenuShow, "显示 (Ctrl+F3)");
        AppendMenuW(_menu, MfString, (IntPtr)MenuExit, "退出");

        return AddIcon(writeLog: true);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        RemoveIcon();

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_menu != IntPtr.Zero)
        {
            DestroyMenu(_menu);
            _menu = IntPtr.Zero;
        }

        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private bool AddIcon(bool writeLog)
    {
        var data = CreateData();

        if (Shell_NotifyIconW(NimAdd, ref data))
        {
            return true;
        }

        if (writeLog)
        {
            TryLogError($"Shell_NotifyIcon(NIM_ADD) 失败，Win32 错误码 {Marshal.GetLastWin32Error()}");
        }

        return false;
    }

    private void RemoveIcon()
    {
        var data = CreateData();
        Shell_NotifyIconW(NimDelete, ref data);
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = TrayId,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = (int)CallbackMessage,
        hIcon = _icon,
        szTip = "QuickMessage（Ctrl+F3 唤起）",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                    _onShow();
                    return IntPtr.Zero;
                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }
        else if (_taskbarCreated != 0 && msg == _taskbarCreated)
        {
            // 资源管理器重启后托盘区被清空，需要重新添加图标。
            AddIcon(writeLog: false);
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        SetForegroundWindow(_hwnd);
        GetCursorPos(out var point);

        // TrackPopupMenuEx 在 TPM_RETURNCMD 下直接返回选中项的命令 ID。
        var command = TrackPopupMenuEx(_menu, TpmReturnCmd | TpmRightButton, point.X, point.Y, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);

        if (command == MenuShow)
        {
            _onShow();
        }
        else if (command == MenuExit)
        {
            _onExit();
        }
    }

    private static IntPtr LoadTrayIcon()
    {
        var directory = Path.Combine(Path.GetTempPath(), "QuickMessage");
        var path = Path.Combine(directory, "app.ico");

        try
        {
            Directory.CreateDirectory(directory);

            using var stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream("QuickMessage.Assets.app.ico");
            if (stream is null)
            {
                return IntPtr.Zero;
            }

            using (var file = File.Create(path))
            {
                stream.CopyTo(file);
            }

            var cx = GetSystemMetrics(SmCxSmIcon);
            var cy = GetSystemMetrics(SmCySmIcon);
            var icon = LoadImageW(IntPtr.Zero, path, ImageIcon, cx, cy, LrLoadFromFile);

            return icon != IntPtr.Zero
                ? icon
                : LoadImageW(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile);
        }
        catch (Exception ex)
        {
            TryLogError("加载托盘图标失败：" + ex.Message);
            return IntPtr.Zero;
        }
    }

    private static void TryLogError(string message)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "qm-tray.txt"), message);
        }
        catch (Exception)
        {
            // 诊断日志失败不影响主流程。
        }
    }
}
