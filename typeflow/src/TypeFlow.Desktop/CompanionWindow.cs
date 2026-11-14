using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TypeFlow.Rendering.Wpf;

namespace TypeFlow.Desktop;

internal sealed class CompanionWindow : Window
{
    private readonly Image image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background);
    private IReadOnlyList<CompanionFrame> idle = [], hover = [], playing = [];
    private int frame;
    private Point? dragStart;
    private Point dragOrigin;
    private bool dragged;
    private nint handle;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Action<Func<DesktopPreferences, DesktopPreferences>> save;
    private readonly Action<string> action;
    private readonly StackPanel panel = new();

    public CompanionWindow(Func<DesktopPreferences> preferences, Action<Func<DesktopPreferences, DesktopPreferences>> save, Action<string> action)
    {
        this.preferences = preferences;
        this.save = update => {
            try { save(update); }
            catch (Exception e) { MessageBox.Show(e.Message, "挂件设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        this.action = action;
        Title = "TypeFlow 桌面挂件";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Content = panel;
        panel.Children.Add(image);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (label, command, tip) in new[] {
            ("语音", "voice", "打开 Windows 语音输入（Win + H）"),
            ("简 / 繁", "script", "切换简繁体（TypeFlow：Ctrl + Shift + 4）"),
            ("中 / 英", "language", "切换中英文（TypeFlow：Ctrl + Shift + 2）"),
            ("设置", "settings", "皮肤、挂件与候选窗设置") })
        {
            var button = new Button { Content = label, ToolTip = tip, Focusable = false, Padding = new(7, 5, 7, 5), Margin = new(1), FontSize = 12 };
            button.Click += (_, _) => action(command);
            buttons.Children.Add(button);
        }
        panel.Children.Add(new Border { Background = Brushes.WhiteSmoke, CornerRadius = new(8), Padding = new(3), Child = buttons });
        var menu = new ContextMenu();
        var locked = new MenuItem { Header = "锁定挂件位置", IsCheckable = true };
        locked.Click += (_, _) => this.save(p => p with { CompanionLocked = locked.IsChecked });
        var reset = new MenuItem { Header = "恢复挂件位置" };
        reset.Click += (_, _) => ResetPosition();
        var settings = new MenuItem { Header = "挂件与皮肤设置…" };
        settings.Click += (_, _) => action("settings");
        var hide = new MenuItem { Header = "隐藏挂件（可在设置中恢复）" };
        hide.Click += (_, _) => { this.save(p => p with { CompanionEnabled = false }); HideCompanion(); };
        menu.Items.Add(locked); menu.Items.Add(reset); menu.Items.Add(settings); menu.Items.Add(new Separator()); menu.Items.Add(hide);
        menu.Opened += (_, _) => locked.IsChecked = preferences().CompanionLocked;
        ContextMenu = menu;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            SetWindowLongPtrW(handle, exStyle, GetWindowLongPtrW(handle, exStyle) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(handle)?.AddHook(KeepEditorFocus);
        };
        timer.Tick += (_, _) => DisplayFrame((frame + 1) % playing.Count);
        MouseEnter += (_, _) => Play(hover.Count > 0 ? hover : idle);
        MouseLeave += (_, _) => Play(idle);
        image.MouseLeftButtonDown += (_, e) => {
            dragStart = PointToScreen(e.GetPosition(this)); dragOrigin = new(Left, Top); dragged = false;
            image.CaptureMouse(); e.Handled = true;
        };
        image.MouseMove += (_, e) =>
        {
            if (dragStart is not { } start || !image.IsMouseCaptured) return;
            var movement = PointToScreen(e.GetPosition(this)) - start;
            var dpi = VisualTreeHelper.GetDpi(this);
            if (!dragged && Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX &&
                Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY) return;
            dragged = true;
            if (preferences().CompanionLocked) return;
            Left = dragOrigin.X + movement.X / dpi.DpiScaleX;
            Top = dragOrigin.Y + movement.Y / dpi.DpiScaleY;
        };
        image.MouseLeftButtonUp += (_, e) => {
            if (dragStart == null) return;
            var wasDragged = dragged;
            image.ReleaseMouseCapture();
            if (wasDragged) { KeepOnScreen(); this.save(p => p with { CompanionLeft = Left, CompanionTop = Top }); }
            else action(preferences().CompanionClick);
            e.Handled = true;
        };
        image.LostMouseCapture += (_, _) => dragStart = null;
        Closed += (_, _) => timer.Stop();
    }

    public bool NativeVisible => handle != 0 && IsWindowVisible(handle);
    public int FrameIndex => frame;
    public bool Hovering => hover.Count > 0 && ReferenceEquals(playing, hover);

    public void SetAnimations(IReadOnlyList<CompanionFrame> idleFrames, IReadOnlyList<CompanionFrame> hoverFrames)
    {
        idle = idleFrames;
        hover = hoverFrames;
        image.Width = Math.Max(idle[0].Image.Width, hover.Count > 0 ? hover[0].Image.Width : 0);
        image.Height = Math.Max(idle[0].Image.Height, hover.Count > 0 ? hover[0].Image.Height : 0);
        Width = Math.Max(240, image.Width);
        Height = image.Height + 40;
        if (double.IsNaN(Left))
        {
            var saved = preferences();
            if (saved.CompanionLeft is { } x && saved.CompanionTop is { } y) { Left = x; Top = y; }
            else DefaultPosition();
        }
        Play(IsMouseOver && hover.Count > 0 ? hover : idle);
    }

    public void ShowCompanion()
    {
        if (NativeVisible || idle.Count == 0) return;
        Show();
        KeepOnScreen();
        // The background launcher uses SW_HIDE. Reveal the popup without moving focus.
        SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0040 | 0x0010 | 0x0004 | 0x0002 | 0x0001);
        DisplayFrame(frame);
    }

    private void DefaultPosition()
    {
        var work = SystemParameters.WorkArea;
        Left = Math.Max(work.Left, work.Right - Width - 24);
        Top = Math.Max(work.Top, work.Bottom - Height - 24);
    }

    public void ResetPosition()
    {
        DefaultPosition(); KeepOnScreen();
        save(p => p with { CompanionLeft = Left, CompanionTop = Top });
    }

    private void KeepOnScreen()
    {
        if (handle == 0 || !GetWindowRect(handle, out var rect)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(handle, 2), ref info)) return;
        var x = Math.Clamp(rect.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - (rect.Right - rect.Left)));
        var y = Math.Clamp(rect.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - (rect.Bottom - rect.Top)));
        SetWindowPos(handle, 0, x, y, 0, 0, 0x0010 | 0x0004 | 0x0001);
    }

    public void HideCompanion()
    {
        timer.Stop();
        Hide();
    }

    private void Play(IReadOnlyList<CompanionFrame> clip)
    {
        if (clip.Count == 0 || ReferenceEquals(playing, clip)) return;
        timer.Stop();
        playing = clip;
        DisplayFrame(0);
    }

    private void DisplayFrame(int index)
    {
        frame = index;
        image.Source = playing[index].Image;
        timer.Interval = TimeSpan.FromMilliseconds(playing[index].DelayMs);
        if (NativeVisible && playing.Count > 1) timer.Start();
    }

    private static nint KeepEditorFocus(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x21) return 0; // WM_MOUSEACTIVATE
        handled = true;
        return 3; // MA_NOACTIVATE
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
