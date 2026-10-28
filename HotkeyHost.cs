using System.Runtime.InteropServices;

namespace QuickMessage;

/// <summary>消息窗口：只负责接收全局热键 WM_HOTKEY，与界面生命周期解耦。</summary>
internal sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModControl = 0x0002;
    private const uint VkF3 = 0x72;
    private const int GwlpWndProc = -4;
    private static readonly IntPtr HwndMessage = new(-3);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr proc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly WndProc _wndProc;
    private readonly Action _onHotkey;
    private IntPtr _hwnd;
    private IntPtr _oldProc;

    public HotkeyHost(Action onHotkey)
    {
        _onHotkey = onHotkey;
        _wndProc = WindowProc;
    }

    /// <summary>创建隐藏的消息窗口并注册 Ctrl+F3，返回是否注册成功。</summary>
    public bool Start()
    {
        _hwnd = CreateWindowExW(0, "STATIC", "QuickMessageHotkey", 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        _oldProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProc));

        return RegisterHotKey(_hwnd, HotkeyId, ModControl, VkF3);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(_hwnd, HotkeyId);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && wParam == (IntPtr)HotkeyId)
        {
            _onHotkey();
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }
}
