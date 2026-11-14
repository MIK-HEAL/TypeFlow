using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Wpf;

// Native status-bar renderer: no archive, INI, Sogou keys or OS input operations.
public sealed class StatusBarSurface : FrameworkElement, IDisposable
{
    private sealed record Sprite(StatusElement Element, bool[] Mask, int Width, int Height);
    private readonly Dictionary<string, IReadOnlyList<CompanionFrame>> clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> states = new();
    private readonly List<Sprite> sprites = [];
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
    private Sprite? hovered, pressed;
    public Rect SourceBounds { get; private set; }
    public event Action<string>? ActionRequested;

    public StatusBarSurface(ThemeStatusBar model, string directory)
    {
        long pixels = 0;
        foreach (var asset in ThemeFiles.StatusAssets(model))
        {
            var path = ThemeFiles.AssetPath(directory, asset);
            IReadOnlyList<CompanionFrame> frames = Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                ? CompanionAnimation.Load(path) : [new(ThemeImages.Load(path), 1000)];
            pixels += frames.Sum(x => (long)x.Image.PixelWidth * x.Image.PixelHeight);
            if (pixels > 32_000_000) throw new InvalidDataException("状态栏解码尺寸超过 3200 万像素。");
            clips[asset] = frames;
        }
        var bounds = Rect.Empty;
        foreach (var element in model.Elements)
        {
            var width = (int)Math.Ceiling(element.Width); var height = (int)Math.Ceiling(element.Height);
            var mask = new bool[checked(width * height)];
            var assets = element.States.SelectMany(s => new[] { s.Image, s.Hover, s.Pressed }).Where(x => x != null).Distinct();
            foreach (var asset in assets)
            foreach (var frame in clips[asset!])
            {
                var bitmap = new FormatConvertedBitmap(frame.Image, PixelFormats.Pbgra32, null, 0);
                var stride = bitmap.PixelWidth * 4; var data = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(data, stride, 0);
                if (bitmap.PixelWidth > width || bitmap.PixelHeight > height) throw new InvalidDataException("状态栏图片大于元素尺寸。");
                for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
                    if (data[y * stride + x * 4 + 3] >= 16) mask[y * width + x] = true;
            }
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (mask[y * width + x])
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (right >= left) bounds.Union(new Rect(element.X + left, element.Y + top, right - left + 1, bottom - top + 1));
            sprites.Add(new(element, mask, width, height));
        }
        SourceBounds = bounds.IsEmpty ? new Rect(0, 0, 1, 1) : bounds;
        timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
        Unloaded += (_, _) => timer.Stop();
    }

    public void SetState(string action, int index) { states[action] = index; InvalidateVisual(); }
    public string? HitAction(Point point) => Hit(point, true)?.Element.Action;
    private Sprite? Hit(Point point, bool actionsOnly)
    {
        foreach (var sprite in sprites.AsEnumerable().Reverse())
        {
            if (actionsOnly && sprite.Element.Action == "none") continue;
            var x = (int)Math.Floor(point.X + SourceBounds.X - sprite.Element.X);
            var y = (int)Math.Floor(point.Y + SourceBounds.Y - sprite.Element.Y);
            if (x >= 0 && y >= 0 && x < sprite.Width && y < sprite.Height && sprite.Mask[y * sprite.Width + x]) return sprite;
        }
        return null;
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        Hit(hitTestParameters.HitPoint, false) != null ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    protected override Size MeasureOverride(Size availableSize) => SourceBounds.Size;
    protected override void OnRender(DrawingContext dc)
    {
        foreach (var sprite in sprites)
        {
            var e = sprite.Element;
            var visual = e.States[Math.Clamp(states.GetValueOrDefault(e.Action), 0, e.States.Length - 1)];
            var isHover = ReferenceEquals(sprite, hovered) || (e.Action == "none" && IsMouseOver);
            var path = ReferenceEquals(sprite, pressed) && visual.Pressed != null ? visual.Pressed : isHover && visual.Hover != null ? visual.Hover : visual.Image;
            var frames = clips[path];
            var elapsed = Environment.TickCount64 % frames.Sum(f => (long)f.DelayMs);
            var current = frames[0];
            foreach (var frame in frames) { current = frame; elapsed -= frame.DelayMs; if (elapsed < 0) break; }
            dc.DrawImage(current.Image, new Rect(e.X - SourceBounds.X, e.Y - SourceBounds.Y, current.Image.Width, current.Image.Height));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        hovered = Hit(e.GetPosition(this), true);
        ToolTip = hovered?.Element.Label;
        Cursor = hovered == null ? Cursors.SizeAll : Cursors.Hand;
        InvalidateVisual(); base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e) { hovered = null; InvalidateVisual(); base.OnMouseLeave(e); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pressed = Hit(e.GetPosition(this), true);
        if (pressed == null) return;
        CaptureMouse(); e.Handled = true; InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (pressed == null) return;
        var command = ReferenceEquals(pressed, Hit(e.GetPosition(this), true)) ? pressed.Element.Action : null;
        ReleaseMouseCapture(); pressed = null; InvalidateVisual(); e.Handled = true;
        if (command != null) ActionRequested?.Invoke(command);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { pressed = null; InvalidateVisual(); base.OnLostMouseCapture(e); }
    public void Dispose() => timer.Stop();
}
