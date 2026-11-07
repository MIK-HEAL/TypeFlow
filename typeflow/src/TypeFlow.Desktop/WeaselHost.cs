using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;

namespace TypeFlow.Desktop;

// The reused Weasel TSF/server remains responsible for input and commits.
// This optional process consumes only presentation data and never injects keys.
public sealed class WeaselHost : Window
{
    private readonly CandidateSurface surface = new();
    private readonly ThemeLibrary library;
    private readonly CancellationTokenSource shutdown = new();
    private string themeDirectory = "";
    private ThemeConfigMonitor? configMonitor;
    private readonly DispatcherTimer configTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private nint hwnd;
    private readonly Func<bool> enabled;
    private bool listening;
    private string renderedTheme = "";
    private CandidateView? lastView;
    private NativeRect lastCaret;
    private int themeRevision;
    private int presentedThemeRevision;
    private readonly Dictionary<int, DateTimeOffset> recentTsfSources = new();
    public long PresentationCount { get; private set; }
    public long NativeTsfPresentationCount { get; private set; }
    public int LastSourcePid { get; private set; }
    public string LastPresentedTheme { get; private set; } = "";
    public IReadOnlyDictionary<int, DateTimeOffset> RecentTsfSources => recentTsfSources;
    public bool NativeVisible => hwnd != 0 && IsWindowVisible(hwnd);
    public WeaselHost(ThemeLibrary library, Func<bool>? enabled = null)
    {
        this.library = library;
        this.enabled = enabled ?? (() => true);
        configTimer.Tick += (_, _) => RefreshConfiguration(); configTimer.Start();
        Title = "TypeFlow 候选窗"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight; Content = surface;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
        Loaded += (_, _) =>
        {
            if (listening) return;
            Hide();
            Start();
        };
        Closed += (_, _) => { configTimer.Stop(); shutdown.Cancel(); };
        // Selection stays with Weasel's keyboard handling. No cross-process click injection.
        IsHitTestVisible = false;
    }
    public void Start()
    {
        if (listening) return;
        listening = true;
        _ = new WindowInteropHelper(this).EnsureHandle();
        _ = Listen(shutdown.Token);
    }
    private static nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }
    private async Task Listen(CancellationToken ct)
    {
        var name = "TypeFlow.Candidates.v2." + Process.GetCurrentProcess().SessionId;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
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
                            var accepted = enabled();
                            if (accepted) { Present(message); if (message.Visible) accepted = NativeVisible; } else Hide();
                            await pipe.WriteAsync(new byte[] { accepted ? (byte)'1' : (byte)'0' }, ct);
                            await pipe.FlushAsync(ct);
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
        RefreshConfiguration();
        var sameSource = message.SourcePid == LastSourcePid;
        PresentationCount++;
        if (message.Source == "tsf")
        {
            NativeTsfPresentationCount++;
            recentTsfSources[message.SourcePid] = DateTimeOffset.UtcNow;
            if (recentTsfSources.Count > 16)
                recentTsfSources.Remove(recentTsfSources.MinBy(entry => entry.Value).Key);
        }
        LastSourcePid = message.SourcePid;
        LastPresentedTheme = renderedTheme;
        var caret = new NativeRect { Left = message.Left, Top = message.Top, Right = message.Right, Bottom = message.Bottom };
        var monitor = MonitorFromRect(ref caret, 2);
        var visible = NativeVisible;
        // Some text stores alternate between nearly identical caret rectangles.
        if (visible && sameSource && caret.Left == lastCaret.Left &&
            Math.Abs((long)caret.Top - lastCaret.Top) < 6 && Math.Abs((long)caret.Bottom - lastCaret.Bottom) < 6 &&
            monitor == MonitorFromRect(ref lastCaret, 2))
            caret = lastCaret;
        var viewChanged = lastView == null || message.View.Preedit != lastView.Preedit ||
            message.View.Highlighted != lastView.Highlighted || !message.View.Candidates.SequenceEqual(lastView.Candidates);
        if (visible && !viewChanged && presentedThemeRevision == themeRevision && caret.Equals(lastCaret)) return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) { Hide(); return; }
        if (viewChanged) surface.SetView(message.View);
        // Only move before sizing when crossing monitors to pick up the new DPI.
        if (MonitorFromWindow(hwnd, 2) != monitor)
            SetWindowPos(hwnd, new nint(-1), caret.Left, caret.Bottom, 0, 0, 0x0010 | 0x0001);
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = Math.Min(info.Work.Right - info.Work.Left, (int)Math.Ceiling(surface.DesiredSize.Width * scale));
        var height = Math.Min(info.Work.Bottom - info.Work.Top, (int)Math.Ceiling(surface.DesiredSize.Height * scale));
        var x = Math.Clamp(caret.Left, info.Work.Left, info.Work.Right - width);
        var y = caret.Bottom + height <= info.Work.Bottom ? caret.Bottom : caret.Top - height;
        y = Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        if (!visible)
        {
            SetWindowPos(hwnd, new nint(-1), x, y, width, height, 0x0010);
            Show();
        }
        UpdateLayout();
        // StartupInfo may request SW_HIDE for a background process. Explicitly
        // show the HWND after WPF's first Show, without activating the editor.
        SetWindowPos(hwnd, new nint(-1), x, y, width, height, 0x0010 | 0x0040);
        lastView = message.View;
        lastCaret = caret;
        presentedThemeRevision = themeRevision;
    }
    private void RefreshConfiguration()
    {
        if (!enabled()) { Hide(); return; }
        var active = library.SelectedDirectory;
        try
        {
            if (active != themeDirectory)
            {
                var theme = ThemeFiles.Load(Path.Combine(active, "theme.json"));
                surface.SetTheme(theme, active); renderedTheme = theme.Name;
                themeRevision++;
                themeDirectory = active; configMonitor = new ThemeConfigMonitor(library, active);
            }
            var result = configMonitor?.Poll();
            if (result?.Changed == true)
            {
                var theme = ThemeFiles.Load(Path.Combine(active, "theme.json"));
                surface.SetTheme(theme, active); renderedTheme = theme.Name; themeRevision++;
            }
            if (result?.Error != null) Debug.WriteLine("Theme reload kept last good frame: " + result.Error);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or JsonException or NotSupportedException or ArgumentException)
        {
            Debug.WriteLine(e.Message);
            if (themeDirectory.Length == 0) surface.SetTheme(new Theme(), library.DefaultDirectory);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
}
