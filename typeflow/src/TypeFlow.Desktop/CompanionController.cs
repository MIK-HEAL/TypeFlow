using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using TypeFlow.Core;
using TypeFlow.Rendering.Wpf;
using TypeFlow.Themes;

namespace TypeFlow.Desktop;

internal sealed class CompanionController : IDisposable
{
    private readonly ThemeLibrary library;
    private readonly Func<bool> enabled;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private string directory = "";
    private ThemeConfigMonitor? monitor;
    private DateTime? configWrite;
    private CompanionWindow? window;
    private bool refreshing;
    private bool disposed;

    public CompanionController(ThemeLibrary library, Func<bool> enabled)
    {
        this.library = library;
        this.enabled = enabled;
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    public bool Visible => window?.NativeVisible == true;
    public int FrameIndex => window?.FrameIndex ?? 0;
    public bool Hovering => window?.Hovering == true;

    public async void Refresh()
    {
        if (disposed) return;
        if (!enabled()) { window?.HideCompanion(); return; }
        if (refreshing) return;
        refreshing = true;
        try
        {
            var selected = library.SelectedDirectory;
            if (!selected.Equals(directory, ThemeFiles.FileSystemComparison))
            {
                window?.Close();
                window = null;
                directory = selected;
                monitor = new ThemeConfigMonitor(library, directory);
                configWrite = null;
            }
            var reload = monitor!.Poll();
            var config = Path.Combine(selected, "theme.json");
            var write = File.GetLastWriteTimeUtc(config);
            if (reload.Error == null && (reload.Changed || write != configWrite))
            {
                configWrite = write;
                var companion = ThemeFiles.Load(config).Companion;
                if (companion == null)
                {
                    window?.Close();
                    window = null;
                    return;
                }
                var (idle, hover) = await Task.Run(() =>
                {
                    var idleFrames = CompanionAnimation.Load(ThemeFiles.AssetPath(selected, companion.Idle));
                    IReadOnlyList<CompanionFrame> hoverFrames = [];
                    if (companion.Hover is { } hoverAsset)
                    {
                        try { hoverFrames = CompanionAnimation.Load(ThemeFiles.AssetPath(selected, hoverAsset)); }
                        catch (Exception e) when (IsAssetError(e)) { Debug.WriteLine(e.Message); }
                    }
                    return (idleFrames, hoverFrames);
                });
                if (disposed || !selected.Equals(library.SelectedDirectory, ThemeFiles.FileSystemComparison)) return;
                window ??= new CompanionWindow();
                window.SetAnimations(idle, hover);
            }
            if (enabled()) window?.ShowCompanion();
        }
        catch (Exception e) when (IsAssetError(e)) { Debug.WriteLine(e.Message); }
        finally { refreshing = false; }
    }

    private static bool IsAssetError(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException
        or FormatException or JsonException or ArgumentException or NotSupportedException or COMException;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        window?.Close();
    }
}
