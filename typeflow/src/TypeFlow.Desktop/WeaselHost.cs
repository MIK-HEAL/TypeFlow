using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TypeFlow.Core;

namespace TypeFlow.Desktop;

// The reused Weasel TSF/server remains responsible for input and commits.
// This optional process consumes only presentation data and never injects keys.
public sealed class WeaselHost : Window
{
    private readonly CandidateSurface surface = new();
    private readonly ThemeLibrary library;
    private readonly CancellationTokenSource shutdown = new();
    private string themeDirectory = "";
    private nint hwnd;
    public WeaselHost(ThemeLibrary library)
    {
        this.library = library;
        Title = "TypeFlow 候选窗"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight; Content = surface;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
        Loaded += async (_, _) => { Hide(); await Listen(shutdown.Token); };
        Closed += (_, _) => shutdown.Cancel();
        // Selection stays with Weasel's keyboard handling. No cross-process click injection.
        IsHitTestVisible = false;
    }
    private static nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }
    private async Task Listen(CancellationToken ct)
    {
        var name = "TypeFlow.Weasel.v1." + Process.GetCurrentProcess().SessionId;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                var buffer = new byte[8192];
                using var line = new MemoryStream();
                int count;
                while ((count = await pipe.ReadAsync(buffer, ct)) > 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (buffer[i] == 10)
                        {
                            var message = JsonSerializer.Deserialize<CandidatePresentation>(line.ToArray(), ThemeFiles.Json)
                                ?? throw new InvalidDataException("候选窗消息为空。");
                            line.SetLength(0);
                            Present(message);
                        }
                        else
                        {
                            if (line.Length > 65536) throw new InvalidDataException("候选窗消息过大。");
                            line.WriteByte(buffer[i]);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException or JsonException or ArgumentException or NotSupportedException)
            { Debug.WriteLine(e.Message); if (!ct.IsCancellationRequested) await Task.Delay(100); }
            Hide();
        }
    }
    private void Present(CandidatePresentation message)
    {
        if (!message.Visible || message.View == null || (message.View.Preedit.Length == 0 && message.View.Candidates.Count == 0)) { Hide(); return; }
        if (message.View.Candidates.Count > 10 || message.View.Candidates.Any(x => x.Text.Length > 4096)) throw new InvalidDataException("候选窗数据超出限制。");
        var active = library.ActiveDirectory;
        if (active != themeDirectory)
        {
            try { surface.SetTheme(ThemeFiles.Load(Path.Combine(active, "theme.json")), active); themeDirectory = active; }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException or NotSupportedException or ArgumentException)
            { surface.SetTheme(new Theme(), library.DefaultDirectory); themeDirectory = ""; }
        }
        surface.SetView(message.View);
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Show(); UpdateLayout();
        var caret = new NativeRect { Left = message.Left, Top = message.Top, Right = message.Right, Bottom = message.Bottom };
        var monitor = MonitorFromRect(ref caret, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;
        // Move first so WPF receives the target monitor's DPI before final sizing.
        SetWindowPos(hwnd, new nint(-1), message.Left, message.Bottom, 0, 0, 0x0010 | 0x0001);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = Math.Min(info.Work.Right - info.Work.Left, (int)Math.Ceiling(surface.DesiredSize.Width * scale));
        var height = Math.Min(info.Work.Bottom - info.Work.Top, (int)Math.Ceiling(surface.DesiredSize.Height * scale));
        var x = Math.Clamp(message.Left, info.Work.Left, info.Work.Right - width);
        var y = message.Bottom + height <= info.Work.Bottom ? message.Bottom : message.Top - height;
        y = Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        SetWindowPos(hwnd, new nint(-1), x, y, width, height, 0x0010);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
