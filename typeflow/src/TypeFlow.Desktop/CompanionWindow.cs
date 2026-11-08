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
    private nint handle;

    public CompanionWindow()
    {
        Title = "TypeFlow 桌面挂件";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Content = image;
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
        MouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(this); CaptureMouse(); };
        MouseMove += (_, e) =>
        {
            if (dragStart is not { } start || !IsMouseCaptured) return;
            var movement = e.GetPosition(this) - start;
            Left += movement.X;
            Top += movement.Y;
        };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        LostMouseCapture += (_, _) => dragStart = null;
        Closed += (_, _) => timer.Stop();
    }

    public bool NativeVisible => handle != 0 && IsWindowVisible(handle);
    public int FrameIndex => frame;
    public bool Hovering => hover.Count > 0 && ReferenceEquals(playing, hover);

    public void SetAnimations(IReadOnlyList<CompanionFrame> idleFrames, IReadOnlyList<CompanionFrame> hoverFrames)
    {
        idle = idleFrames;
        hover = hoverFrames;
        Width = Math.Max(idle[0].Image.Width, hover.Count > 0 ? hover[0].Image.Width : 0);
        Height = Math.Max(idle[0].Image.Height, hover.Count > 0 ? hover[0].Image.Height : 0);
        if (double.IsNaN(Left))
        {
            var work = SystemParameters.WorkArea;
            Left = Math.Max(work.Left, work.Right - Width - 24);
            Top = Math.Max(work.Top, work.Bottom - Height - 24);
        }
        Play(IsMouseOver && hover.Count > 0 ? hover : idle);
    }

    public void ShowCompanion()
    {
        if (NativeVisible || idle.Count == 0) return;
        Show();
        // The background launcher uses SW_HIDE. Reveal the popup without moving focus.
        SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0040 | 0x0010 | 0x0004 | 0x0002 | 0x0001);
        DisplayFrame(frame);
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
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
