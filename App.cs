using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace QuickMessage;

public sealed class App : Application
{
    private const string ShowEventName = @"Local\QuickMessage.Show";

    private EditorWindow? _window;
    private TrayIconHost? _tray;
    private HotkeyHost? _hotkey;
    private EventWaitHandle? _showEvent;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _window = new EditorWindow();

            _tray = new TrayIconHost(ShowEditor, ExitApplication);
            if (!_tray.Start())
            {
                // 托盘创建失败时仍可通过热键使用。
                _tray.Dispose();
                _tray = null;
            }

            _hotkey = new HotkeyHost(ShowEditor);
            if (!_hotkey.Start())
            {
                // 热键被占用时依然保留托盘与手动显示能力。
                _hotkey.Dispose();
                _hotkey = null;
            }

            // 第二个实例通过命名事件唤醒本实例。
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, _) => Dispatcher.UIThread.Post(ShowEditor),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            desktop.Exit += (_, _) => DisposeResources();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowEditor() => Dispatcher.UIThread.Post(() => _window?.ShowEditor());

    private void ExitApplication()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void DisposeResources()
    {
        _hotkey?.Dispose();
        _hotkey = null;

        _showEvent?.Dispose();
        _showEvent = null;

        _tray?.Dispose();
        _tray = null;
    }
}
