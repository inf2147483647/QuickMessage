using Avalonia;

namespace QuickMessage;

internal static class Program
{
    private const string MutexName = @"Local\QuickMessage.SingleInstance";
    private const string ShowEventName = @"Local\QuickMessage.Show";

    public const string WindowTitle = "QuickMessage";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            // 已有实例在运行：通知它显示窗口，本进程直接退出。
            SignalExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void SignalExistingInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 软件渲染使用分层窗口绘制，保证窗口背景的半透明在任何显卡环境下都生效。
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            .LogToTrace();
}
