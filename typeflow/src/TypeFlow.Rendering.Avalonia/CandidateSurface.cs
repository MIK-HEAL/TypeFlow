using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Avalonia;

public sealed record CanvasElement(string Id, string Name, Rect Bounds);

public sealed class CandidateSurface : Control, ICandidateRenderer, IDisposable
{
    private Theme theme = new();
    private CandidateView view = new("", []);
    private Dictionary<string, Bitmap> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Rect Bounds, int Index)> hitboxes = [];
    private Point? dragStart;
    private Vector dragOffset;
    private bool disposed;
    public bool IsEditing { get; set; }
    public string? SelectedElement { get; set; }
    public event Action<int>? CandidateSelected;
    public event Action<string>? ElementSelected;
    public event Action<string, double, double>? ElementMoved;
    public CandidateSurface() { Focusable = true; }

    public void SetTheme(Theme value, string directory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ThemeFiles.Validate(value, directory);
        var loaded = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            long pixels = 0;
            foreach (var relative in ThemeFiles.ReferencedImages(value))
            {
                var image = ThemeImages.Load(ThemeFiles.AssetPath(directory, relative));
                loaded.Add(relative, image);
                pixels += (long)image.PixelSize.Width * image.PixelSize.Height;
                if (pixels > 16_000_000) throw new InvalidDataException("皮肤图片解码总量超过 1600 万像素。");
            }
            if (value.Background is { Mode: "nineSlice", Image: { } background })
                ThemeImages.ValidateSlice(loaded[background], value.Background.Slice);
            foreach (var layer in value.Layers.Where(x => x.Mode == "nineSlice"))
                ThemeImages.ValidateSlice(loaded[layer.Image], layer.Slice);
        }
        catch { foreach (var image in loaded.Values) image.Dispose(); throw; }
        foreach (var image in images.Values) image.Dispose();
        images = loaded; theme = value;
        InvalidateMeasure(); InvalidateVisual();
    }
    public void SetView(CandidateView value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        view = value; InvalidateMeasure(); InvalidateVisual();
    }
    private FormattedText Text(string text, string color) => new(text, CultureInfo.GetCultureInfo("zh-CN"),
        FlowDirection.LeftToRight, new Typeface(theme.Text.FontFamily + ", PingFang SC, Noto Sans CJK SC, sans-serif"),
        theme.Text.FontSize, Brush(color));
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static string Label(Candidate c, int i) => $"{(c.Label.Length == 0 ? (i + 1).ToString(CultureInfo.InvariantCulture) : c.Label)} {c.Text}";
    private (Rect Preedit, Rect Candidates) TextBounds()
    {
        var padding = theme.Layout.Padding ?? new();
        var p = theme.Layout.PreeditMargin ?? padding;
        var c = theme.Layout.CandidateMargin ?? new(padding.Left, theme.Layout.RowGap, padding.Right, padding.Bottom);
        var text = Text(view.Preedit.Length == 0 ? " " : view.Preedit, theme.Text.PreeditColor);
        var width = view.Candidates.Select((x, i) => Text(Label(x, i), theme.Text.Color).Width + 8).Sum() +
            Math.Max(0, view.Candidates.Count - 1) * theme.Layout.CandidateGap;
        var posP = theme.Layout.PreeditPosition ?? new(p.Left, p.Top);
        var posC = theme.Layout.CandidatePosition ?? new(c.Left, posP.Y + text.Height + p.Bottom + c.Top);
        return (new(posP.X, posP.Y, Math.Max(4, text.Width), text.Height),
            new(posC.X, posC.Y, Math.Max(4, width), text.Height));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var (p, c) = TextBounds();
        var padding = theme.Layout.Padding ?? new();
        var pm = theme.Layout.PreeditMargin ?? padding; var cm = theme.Layout.CandidateMargin ?? padding;
        return new(Math.Min(4096, Math.Max(theme.Layout.MinWidth, Math.Max(p.Right + pm.Right, c.Right + cm.Right))),
            Math.Min(2048, Math.Max(theme.Layout.MinHeight, Math.Max(p.Bottom + pm.Bottom, c.Bottom + cm.Bottom))));
    }
    private Rect LayerBounds(ThemeLayer layer, Size size)
    {
        var image = images[layer.Image];
        var w = layer.Width == 0 ? image.PixelSize.Width : layer.Width;
        var h = layer.Height == 0 ? image.PixelSize.Height : layer.Height;
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
            var (p, c) = TextBounds(); var size = Bounds.Width > 0 ? Bounds.Size : DesiredSize;
            return theme.Layers.Where(x => x.Visible && x.Plane == "behindText")
                .Select(x => new CanvasElement("layer:" + x.Id, x.Name, LayerBounds(x, size)))
                .Concat([new("preedit", "拼音文字", p), new("candidates", "候选文字", c)])
                .Concat(theme.Layers.Where(x => x.Visible && x.Plane == "aboveText")
                    .Select(x => new CanvasElement("layer:" + x.Id, x.Name, LayerBounds(x, size)))).ToArray();
        }
    }
    private Rect Offset(string id, Rect bounds) => id == SelectedElement && dragStart != null ? bounds.Translate(dragOffset) : bounds;
    public override void Render(DrawingContext dc)
    {
        if (disposed) return;
        using var clip = dc.PushClip(new Rect(Bounds.Size));
        dc.DrawRectangle(Brush(theme.Background.Color), null, new Rect(Bounds.Size));
        if (theme.Background is { Mode: not "solid", Image: { } background })
            PaintImage(dc, images[background], new Rect(Bounds.Size), theme.Background.Mode, theme.Background.Slice,
                theme.Background.HorizontalFill, theme.Background.VerticalFill);
        DrawLayers(dc, "behindText");
        var (p, c) = TextBounds(); p = Offset("preedit", p); c = Offset("candidates", c);
        dc.DrawText(Text(view.Preedit.Length == 0 ? " " : view.Preedit, theme.Text.PreeditColor), p.TopLeft);
        hitboxes.Clear(); var x = c.Left;
        for (var i = 0; i < view.Candidates.Count; i++)
        {
            var selected = i == view.Highlighted;
            var text = Text(Label(view.Candidates[i], i), selected ? theme.Selection.TextColor : theme.Text.Color);
            var bounds = new Rect(x, c.Top - 2, text.Width + 8, text.Height + 4);
            if (selected) dc.DrawRectangle(Brush(theme.Selection.BackgroundColor), null, bounds, 4, 4);
            dc.DrawText(text, new(x + 4, c.Top)); hitboxes.Add((bounds, i));
            x = bounds.Right + theme.Layout.CandidateGap;
        }
        DrawLayers(dc, "aboveText");
        if (IsEditing)
            foreach (var element in Elements)
                dc.DrawRectangle(null, new Pen(element.Id == SelectedElement ? Brushes.DodgerBlue : Brushes.SlateGray,
                    element.Id == SelectedElement ? 2 : .7), Offset(element.Id, element.Bounds));
    }
    private void DrawLayers(DrawingContext dc, string plane)
    {
        foreach (var layer in theme.Layers.Where(x => x.Visible && x.Plane == plane))
        {
            using var opacity = dc.PushOpacity(layer.Opacity);
            PaintImage(dc, images[layer.Image], Offset("layer:" + layer.Id, LayerBounds(layer, Bounds.Size)),
                layer.Mode, layer.Slice, layer.HorizontalFill, layer.VerticalFill);
        }
    }
    private static void PaintImage(DrawingContext dc, Bitmap image, Rect target, string mode, Insets? slice, string horizontal, string vertical)
    {
        if (mode != "nineSlice")
        {
            Patch(dc, image, new(0, 0, image.PixelSize.Width, image.PixelSize.Height), target, mode == "tile", mode == "tile");
            return;
        }
        var s = slice ?? new(0, 0, 0, 0);
        var sx = new[] { 0.0, s.Left, image.PixelSize.Width - s.Right, image.PixelSize.Width };
        var sy = new[] { 0.0, s.Top, image.PixelSize.Height - s.Bottom, image.PixelSize.Height };
        var scaleX = Math.Min(1, target.Width / Math.Max(1, s.Left + s.Right));
        var scaleY = Math.Min(1, target.Height / Math.Max(1, s.Top + s.Bottom));
        var dx = new[] { target.Left, target.Left + s.Left * scaleX, target.Right - s.Right * scaleX, target.Right };
        var dy = new[] { target.Top, target.Top + s.Top * scaleY, target.Bottom - s.Bottom * scaleY, target.Bottom };
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
            Patch(dc, image, new(sx[col], sy[row], sx[col + 1] - sx[col], sy[row + 1] - sy[row]),
                new(dx[col], dy[row], Math.Max(0, dx[col + 1] - dx[col]), Math.Max(0, dy[row + 1] - dy[row])),
                col == 1 && horizontal == "tile", row == 1 && vertical == "tile");
    }
    private static void Patch(DrawingContext dc, Bitmap image, Rect source, Rect target, bool tileX, bool tileY)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0) return;
        var rx = image.Size.Width / image.PixelSize.Width; var ry = image.Size.Height / image.PixelSize.Height;
        var sourceDip = new Rect(source.X * rx, source.Y * ry, source.Width * rx, source.Height * ry);
        using var transform = dc.PushTransform(Matrix.CreateTranslation(target.X, target.Y));
        dc.DrawRectangle(new ImageBrush(image)
        {
            SourceRect = new(sourceDip, RelativeUnit.Absolute),
            DestinationRect = new(new Rect(0, 0, tileX ? source.Width : target.Width,
                tileY ? source.Height : target.Height), RelativeUnit.Absolute),
            Stretch = Stretch.Fill, TileMode = tileX || tileY ? TileMode.Tile : TileMode.None
        }, null, new Rect(target.Size));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (IsEditing)
        {
            var hit = Elements.LastOrDefault(x => x.Bounds.Contains(point));
            if (hit != null) { SelectedElement = hit.Id; ElementSelected?.Invoke(hit.Id); dragStart = point; dragOffset = default; e.Pointer.Capture(this); Focus(); InvalidateVisual(); }
            e.Handled = true; return;
        }
        foreach (var (bounds, index) in hitboxes)
            if (bounds.Contains(point)) { CandidateSelected?.Invoke(index); e.Handled = true; break; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragStart is { } start && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        { var delta = e.GetPosition(this) - start; dragOffset = new(Math.Round(delta.X), Math.Round(delta.Y)); InvalidateVisual(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragStart == null) return;
        var delta = dragOffset; dragStart = null; dragOffset = default; e.Pointer.Capture(null);
        if (SelectedElement is { } id && delta.Length > 0) ElementMoved?.Invoke(id, delta.X, delta.Y);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { dragStart = null; dragOffset = default; InvalidateVisual(); base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEditing || SelectedElement == null || !Elements.Any(x => x.Id == SelectedElement)) return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new(step, 0), Key.Up => new(0, -step), Key.Down => new(0, step), _ => default };
        if (delta.Length > 0) { ElementMoved?.Invoke(SelectedElement, delta.X, delta.Y); e.Handled = true; }
    }
    public void SavePng(string path, double scale = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!double.IsFinite(scale) || scale < .5 || scale > 3) throw new ArgumentOutOfRangeException(nameof(scale));
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Arrange(new Rect(DesiredSize));
        using var output = new RenderTargetBitmap(new((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale)), new(96 * scale, 96 * scale));
        output.Render(this); output.Save(path, new PngBitmapEncoderOptions());
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var image in images.Values) image.Dispose();
        images.Clear();
    }
}
