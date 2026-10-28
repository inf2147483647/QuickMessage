using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuickMessage;

/// <summary>无边框半透明输入窗口：Ctrl+F3 显示，Esc 隐藏并清空内容。</summary>
public sealed class EditorWindow : Window
{
    private const float DefaultFontSize = 30f;
    private const float MinFontSize = 8f;
    private const float MaxFontSize = 200f;
    private const float FontSizeStep = 2f;

    // 整窗半透明由 Win32 分层窗口实现，任何渲染路径下都生效。
    private const int GwlpExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const byte WindowAlpha = 115; // 0.45 不透明度

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    private static readonly string FontSizeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickMessage",
        "font-size.txt");

    private readonly TextBox _editor;
    private float _fontSize;

    public EditorWindow()
    {
        Title = Program.WindowTitle;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 850;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;
        BorderThickness = new Thickness(0);
        BorderBrush = Brushes.Transparent;
        FocusAdorner = null;

        _fontSize = LoadFontSize();

        _editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = _fontSize,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10),
            VerticalContentAlignment = VerticalAlignment.Top,
            FocusAdorner = null,
        };

        Content = new Border
        {
            Background = Brushes.Black,
            Child = _editor,
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public bool AllowClose { get; set; }

    public void ShowEditor()
    {
        Show();
        Topmost = true;
        Activate();
        _editor.Focus();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyLayeredTransparency();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 关窗（Alt+F4 等）只是隐藏，程序继续驻留托盘。
        if (!AllowClose)
        {
            e.Cancel = true;
            HideEditor();
        }

        base.OnClosing(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideEditor();
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        if (e.Key is Key.OemPlus or Key.Add)
        {
            ChangeFontSize(FontSizeStep);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            ChangeFontSize(-FontSizeStep);
            e.Handled = true;
        }
    }

    private void ApplyLayeredTransparency()
    {
        if (TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        var hwnd = handle.Handle;
        var exStyle = GetWindowLong(hwnd, GwlpExStyle);
        SetWindowLong(hwnd, GwlpExStyle, exStyle | WsExLayered);
        SetLayeredWindowAttributes(hwnd, 0, WindowAlpha, LwaAlpha);
    }

    private void HideEditor()
    {
        _editor.Text = string.Empty;
        Hide();
    }

    private void ChangeFontSize(float delta)
    {
        var size = Math.Clamp(_fontSize + delta, MinFontSize, MaxFontSize);

        if (size == _fontSize)
        {
            return;
        }

        _fontSize = size;
        _editor.FontSize = size;
        SaveFontSize(size);
    }

    private static float LoadFontSize()
    {
        try
        {
            var text = File.ReadAllText(FontSizeFile);

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
            {
                return Math.Clamp(size, MinFontSize, MaxFontSize);
            }
        }
        catch (Exception)
        {
            // 首次运行或文件损坏时使用默认字号。
        }

        return DefaultFontSize;
    }

    private static void SaveFontSize(float size)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FontSizeFile)!);

            // 先写临时文件再替换，避免写入中断留下半个文件。
            var temp = FontSizeFile + ".tmp";
            File.WriteAllText(temp, size.ToString(CultureInfo.InvariantCulture));
            File.Move(temp, FontSizeFile, overwrite: true);
        }
        catch (Exception)
        {
            // 写入失败时保持运行，仅此次字号不持久化。
        }
    }
}
