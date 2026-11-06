using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TypeFlow.Core;

namespace TypeFlow.Desktop;

public interface ICandidateRenderer
{
    void SetTheme(Theme theme, string directory);
    void SetView(CandidateView view);
}
public sealed record CanvasElement(string Id, string Name, Rect Bounds);

public sealed class CandidateSurface : FrameworkElement, ICandidateRenderer
{
    private Theme theme = new();
    private CandidateView view = new("", []);
    private Dictionary<string, (long Ticks, long Length, BitmapSource Image)> cache = [];
    private Dictionary<string, BitmapSource> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Rect Bounds, int Index)> hitboxes = [];
    private Point? dragStart;
    private Vector dragOffset;
    public bool IsEditing { get; set; }
    public bool ShowGuides { get; set; }
    public string? SelectedElement { get; set; }
    public event Action<int>? CandidateSelected;
    public event Action<string>? ElementSelected;
    public event Action<string, double, double>? ElementMoved;
    public CandidateSurface() { Focusable = true; }

    public void SetTheme(Theme value, string directory)
    {
        ThemeFiles.Validate(value, directory);
        var loaded = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        var nextCache = new Dictionary<string, (long, long, BitmapSource)>();
        long pixels = 0;
        foreach (var relative in ThemeFiles.ReferencedImages(value))
        {
            var path = ThemeFiles.AssetPath(directory, relative);
            var file = new FileInfo(path);
            var image = cache.TryGetValue(path, out var saved) && saved.Ticks == file.LastWriteTimeUtc.Ticks && saved.Length == file.Length
                ? saved.Image : ThemeImages.Load(path);
            pixels += (long)image.PixelWidth * image.PixelHeight;
            if (pixels > 16_000_000) throw new InvalidDataException("皮肤图片解码总量超过 1600 万像素。");
            loaded[relative] = image; nextCache[path] = (file.LastWriteTimeUtc.Ticks, file.Length, image);
        }
        if (value.Background is { Mode: "nineSlice", Image: { } background }) ThemeImages.ValidateSlice(loaded[background], value.Background.Slice);
        foreach (var layer in value.Layers.Where(x => x.Mode == "nineSlice")) ThemeImages.ValidateSlice(loaded[layer.Image], layer.Slice);
        // Publish only after every asset validates. Failed edits retain the last good frame.
        theme = value; images = loaded; cache = nextCache;
        InvalidateMeasure(); InvalidateVisual();
    }
    public void SetView(CandidateView value) { view = value; InvalidateMeasure(); InvalidateVisual(); }
    private FormattedText Text(string text, string color) => new(text, CultureInfo.GetCultureInfo("zh-CN"),
        FlowDirection.LeftToRight, new Typeface(theme.Text.FontFamily + ", Microsoft YaHei UI"), theme.Text.FontSize,
        Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static string Label(Candidate candidate, int i) => $"{(candidate.Label.Length == 0 ? (i + 1).ToString(CultureInfo.InvariantCulture) : candidate.Label)} {candidate.Text}";
    private (Rect Preedit, Rect Candidates) TextBounds()
    {
        var padding = theme.Layout.Padding ?? new();
        var p = theme.Layout.PreeditMargin ?? padding;
        var c = theme.Layout.CandidateMargin ?? new(padding.Left, theme.Layout.RowGap, padding.Right, padding.Bottom);
        var preedit = Text(view.Preedit.Length == 0 ? " " : view.Preedit, theme.Text.PreeditColor);
        var width = view.Candidates.Select((x, i) => Text(Label(x, i), theme.Text.Color).Width + 8).Sum() + Math.Max(0, view.Candidates.Count - 1) * theme.Layout.CandidateGap;
        var posP = theme.Layout.PreeditPosition ?? new(p.Left, p.Top);
        var posC = theme.Layout.CandidatePosition ?? new(c.Left, posP.Y + preedit.Height + p.Bottom + c.Top);
        return (new(posP.X, posP.Y, Math.Max(4, preedit.Width), preedit.Height), new(posC.X, posC.Y, Math.Max(4, width), preedit.Height));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var (p, c) = TextBounds();
        var padding = theme.Layout.Padding ?? new();
        var pm = theme.Layout.PreeditMargin ?? padding; var cm = theme.Layout.CandidateMargin ?? padding;
        // Decorations don't resize the canvas; anchors stay stable as a layer moves.
        return new(Math.Min(4096, Math.Max(theme.Layout.MinWidth, Math.Max(p.Right + pm.Right, c.Right + cm.Right))),
            Math.Min(2048, Math.Max(theme.Layout.MinHeight, Math.Max(p.Bottom + pm.Bottom, c.Bottom + cm.Bottom))));
    }
    private Rect LayerBounds(ThemeLayer layer, Size size)
    {
        var image = images[layer.Image];
        var w = layer.Width == 0 ? image.PixelWidth : layer.Width; var h = layer.Height == 0 ? image.PixelHeight : layer.Height;
        var x = layer.Anchor is "topCenter" or "center" or "bottomCenter" ? (size.Width - w) / 2 :
            layer.Anchor is "topRight" or "centerRight" or "bottomRight" ? size.Width - w : 0;
        var y = layer.Anchor is "centerLeft" or "center" or "centerRight" ? (size.Height - h) / 2 :
            layer.Anchor is "bottomLeft" or "bottomCenter" or "bottomRight" ? size.Height - h : 0;
        return new(x + layer.X, y + layer.Y, w, h);
    }
    public IReadOnlyList<CanvasElement> Elements
    {
        get
        {
            var (p, c) = TextBounds(); var size = RenderSize.Width > 0 ? RenderSize : DesiredSize;
            var behind = theme.Layers.Where(x => x.Visible && x.Plane == "behindText").Select(x => new CanvasElement("layer:" + x.Id, x.Name, LayerBounds(x, size)));
            var above = theme.Layers.Where(x => x.Visible && x.Plane == "aboveText").Select(x => new CanvasElement("layer:" + x.Id, x.Name, LayerBounds(x, size)));
            return behind.Concat(new[] { new CanvasElement("preedit", "拼音文字", p), new CanvasElement("candidates", "候选文字", c) }).Concat(above).ToArray();
        }
    }
    private Rect Offset(string id, Rect bounds)
    {
        if (id == SelectedElement && dragStart != null) bounds.Offset(dragOffset);
        return bounds;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawRectangle(Brush(theme.Background.Color), null, new Rect(RenderSize));
        if (theme.Background is { Mode: not "solid", Image: { } image })
            PaintImage(dc, images[image], new Rect(RenderSize), theme.Background.Mode, theme.Background.Slice, theme.Background.HorizontalFill, theme.Background.VerticalFill);
        DrawLayers(dc, "behindText");
        var (p, c) = TextBounds(); p = Offset("preedit", p); c = Offset("candidates", c);
        dc.DrawText(Text(view.Preedit.Length == 0 ? " " : view.Preedit, theme.Text.PreeditColor), p.TopLeft);
        hitboxes.Clear(); var x = c.Left;
        for (var i = 0; i < view.Candidates.Count; i++)
        {
            var selected = i == view.Highlighted;
            var text = Text(Label(view.Candidates[i], i), selected ? theme.Selection.TextColor : theme.Text.Color);
            var rect = new Rect(x, c.Top - 2, text.Width + 8, text.Height + 4);
            if (selected) dc.DrawRoundedRectangle(Brush(theme.Selection.BackgroundColor), null, rect, 4, 4);
            dc.DrawText(text, new(x + 4, c.Top)); hitboxes.Add((rect, i)); x = rect.Right + theme.Layout.CandidateGap;
        }
        DrawLayers(dc, "aboveText");
        if (IsEditing && ShowGuides)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(32, 70, 100, 150)), 0.5);
            for (var gx = 0; gx < ActualWidth; gx += 16) dc.DrawLine(gridPen, new(gx, 0), new(gx, ActualHeight));
            for (var gy = 0; gy < ActualHeight; gy += 16) dc.DrawLine(gridPen, new(0, gy), new(ActualWidth, gy));
            foreach (var element in Elements)
            {
                var pen = new Pen(element.Id == SelectedElement ? Brushes.DodgerBlue : Brushes.SlateGray, element.Id == SelectedElement ? 2 : 0.7) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, pen, Offset(element.Id, element.Bounds));
            }
        }
        dc.Pop();
    }
    private void DrawLayers(DrawingContext dc, string plane)
    {
        foreach (var layer in theme.Layers.Where(x => x.Visible && x.Plane == plane))
        {
            dc.PushOpacity(layer.Opacity);
            PaintImage(dc, images[layer.Image], Offset("layer:" + layer.Id, LayerBounds(layer, RenderSize)), layer.Mode, layer.Slice, layer.HorizontalFill, layer.VerticalFill);
            dc.Pop();
        }
    }
    private static void PaintImage(DrawingContext dc, BitmapSource image, Rect target, string mode, Insets? slice, string horizontal, string vertical)
    {
        if (mode != "nineSlice")
        {
            Patch(dc, image, new(0, 0, image.PixelWidth, image.PixelHeight), target, mode == "tile", mode == "tile"); return;
        }
        var s = slice ?? new(0, 0, 0, 0);
        var sx = new[] { 0.0, s.Left, image.PixelWidth - s.Right, (double)image.PixelWidth };
        var sy = new[] { 0.0, s.Top, image.PixelHeight - s.Bottom, (double)image.PixelHeight };
        var scaleX = Math.Min(1, target.Width / Math.Max(1, s.Left + s.Right));
        var scaleY = Math.Min(1, target.Height / Math.Max(1, s.Top + s.Bottom));
        var dx = new[] { target.Left, target.Left + s.Left * scaleX, target.Right - s.Right * scaleX, target.Right };
        var dy = new[] { target.Top, target.Top + s.Top * scaleY, target.Bottom - s.Bottom * scaleY, target.Bottom };
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
            Patch(dc, image, new(sx[col], sy[row], sx[col + 1] - sx[col], sy[row + 1] - sy[row]),
                new(dx[col], dy[row], Math.Max(0, dx[col + 1] - dx[col]), Math.Max(0, dy[row + 1] - dy[row])), col == 1 && horizontal == "tile", row == 1 && vertical == "tile");
    }
    private static void Patch(DrawingContext dc, BitmapSource image, Rect source, Rect target, bool tileX, bool tileY)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0) return;
        dc.DrawRectangle(new ImageBrush(image) {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = source, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new(target.X, target.Y, tileX ? source.Width : target.Width, tileY ? source.Height : target.Height),
            Stretch = Stretch.Fill, TileMode = tileX || tileY ? TileMode.Tile : TileMode.None
        }, null, target);
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var point = e.GetPosition(this);
        if (IsEditing)
        {
            var hit = Elements.LastOrDefault(x => x.Bounds.Contains(point));
            if (hit != null) { SelectedElement = hit.Id; ElementSelected?.Invoke(hit.Id); dragStart = point; dragOffset = new(); CaptureMouse(); Focus(); InvalidateVisual(); }
            e.Handled = true; return;
        }
        foreach (var (bounds, index) in hitboxes) if (bounds.Contains(point)) { CandidateSelected?.Invoke(index); e.Handled = true; break; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        { var delta = e.GetPosition(this) - start; dragOffset = new(Math.Round(delta.X), Math.Round(delta.Y)); InvalidateVisual(); }
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (dragStart == null) return;
        var delta = dragOffset; dragStart = null; dragOffset = new(); ReleaseMouseCapture();
        if (SelectedElement is { } id && delta.Length > 0) ElementMoved?.Invoke(id, delta.X, delta.Y);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragStart = null; dragOffset = new(); InvalidateVisual(); base.OnLostMouseCapture(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsEditing || SelectedElement == null || !Elements.Any(x => x.Id == SelectedElement)) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new(step, 0), Key.Up => new(0, -step), Key.Down => new(0, step), _ => new() };
        if (delta.Length > 0) { ElementMoved?.Invoke(SelectedElement, delta.X, delta.Y); e.Handled = true; }
    }
    public void SavePng(string path, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale < 0.5 || scale > 3) throw new ArgumentOutOfRangeException(nameof(scale));
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Arrange(new Rect(DesiredSize)); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
