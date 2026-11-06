using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;

namespace TypeFlow.Desktop;

public sealed class LayoutEditorWindow : Window
{
    private readonly ThemeEditSession session;
    private readonly CandidateSurface surface = new() { IsEditing = true, ShowGuides = true };
    private readonly ListBox elements = new() { BorderThickness = new(0), Background = Brushes.Transparent };
    private readonly StackPanel properties = new() { Margin = new(14) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
    private readonly TextBox name = new() { MinWidth = 240, Padding = new(8, 6, 8, 6) };
    private readonly Button undo = new() { Content = "撤销" }, redo = new() { Content = "重做" };
    private string selected = "preedit";
    private bool refreshing, saved;
    private readonly List<Action> commits = [];
    private readonly HashSet<TextBox> invalidFields = [];
    private readonly string initialName;
    public event Action<string, bool>? Saved;
    public LayoutEditorWindow(ThemeLibrary library, string sourceDirectory, CandidateView preview)
    {
        session = new(library, sourceDirectory);
        Title = "TypeFlow · 高级皮肤布局"; Width = 1380; Height = 860; MinWidth = 1080; MinHeight = 680;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13; Background = new SolidColorBrush(Color.FromRgb(242, 245, 250));
        initialName = session.Current.Name + " · 自定义"; name.Text = initialName;
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var header = new StackPanel { Margin = new(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "高级皮肤布局", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "拖动文字与装饰图片，自由组织你的候选窗。所有修改另存为新皮肤。", Foreground = Brushes.SlateGray, Margin = new(0, 5, 0, 0) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var saveButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(saveButtons, Dock.Right); footer.Children.Add(saveButtons);
        Button(saveButtons, "另存副本", () => Save(false)); Button(saveButtons, "保存并应用", () => Save(true), primary: true);
        footer.Children.Add(name);
        var columns = new Grid(); root.Children.Add(columns);
        columns.ColumnDefinitions.Add(new() { Width = new(205) }); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new(300) });
        var left = new DockPanel { Margin = new(0, 0, 14, 0) }; columns.Children.Add(left);
        var leftTitle = new TextBlock { Text = "文字与图层", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) };
        DockPanel.SetDock(leftTitle, Dock.Top); left.Children.Add(leftTitle);
        var layerActions = new StackPanel(); DockPanel.SetDock(layerActions, Dock.Bottom); left.Children.Add(layerActions);
        Button(layerActions, "＋ 添加装饰图片", AddLayer);
        var ordering = new StackPanel { Orientation = Orientation.Horizontal }; layerActions.Children.Add(ordering);
        Button(ordering, "后移", () => LayerAction(id => session.ReorderLayer(id, -1))); Button(ordering, "前移", () => LayerAction(id => session.ReorderLayer(id, 1)));
        Button(layerActions, "移除选中图层", () => LayerAction(id => { session.RemoveLayer(id); Select("preedit"); }));
        left.Children.Add(elements);
        var middle = new DockPanel { Margin = new(0, 0, 14, 0) }; Grid.SetColumn(middle, 1); columns.Children.Add(middle);
        var tools = new WrapPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(tools, Dock.Top); middle.Children.Add(tools);
        foreach (var b in new[] { undo, redo }) { b.Padding = new(10, 6, 10, 6); b.Margin = new(0, 0, 6, 0); tools.Children.Add(b); }
        undo.Click += (_, _) => Run(() => { session.Undo(); BuildProperties(); }); redo.Click += (_, _) => Run(() => { session.Redo(); BuildProperties(); });
        var guides = new CheckBox { Content = "网格与选框", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
        guides.Click += (_, _) => { surface.ShowGuides = guides.IsChecked == true; surface.InvalidateVisual(); }; tools.Children.Add(guides);
        var zoom = new ComboBox { ItemsSource = new[] { "75%", "100%", "150%", "200%" }, SelectedIndex = 1, Width = 80 };
        zoom.SelectionChanged += (_, _) => { var factor = new[] { .75, 1.0, 1.5, 2.0 }[zoom.SelectedIndex]; surface.LayoutTransform = new ScaleTransform(factor, factor); }; tools.Children.Add(zoom);
        Button(tools, "恢复源布局", () => Run(() => { session.Reset(); BuildProperties(); }));
        var instructions = new StackPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(instructions, Dock.Bottom); middle.Children.Add(instructions);
        instructions.Children.Add(new TextBlock { Text = "拖动定位 · 方向键微调 1 DIP · Shift＋方向键微调 10 DIP\n图层可在文字上方或下方绘制；超出画布的部分会裁剪。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        instructions.Children.Add(status);
        var canvas = new Border { Background = Checkerboard(), CornerRadius = new(8), BorderBrush = Brushes.LightSlateGray, BorderThickness = new(1), Padding = new(24) };
        canvas.Child = new ScrollViewer { Content = new Border { Child = surface, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        middle.Children.Add(canvas);
        var inspector = new Border { Background = Brushes.White, CornerRadius = new(8), Child = new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetColumn(inspector, 2); columns.Children.Add(inspector);
        elements.SelectionChanged += (_, _) => { if (!refreshing && elements.SelectedItem is Item item) Select(item.Id); };
        surface.ElementSelected += Select;
        surface.ElementMoved += (id, dx, dy) => Run(() => { session.Move(id, dx, dy, surface.Elements); BuildProperties(); });
        surface.SetView(preview.Preedit.Length > 0 || preview.Candidates.Count > 0 ? preview : Program.Demo);
        session.Changed += Refresh;
        Refresh(); Select("preedit");
        Closing += (_, e) =>
        {
            if (!saved && (session.IsDirty || name.Text != initialName) && MessageBox.Show(this, "放弃尚未保存的布局修改？", "TypeFlow", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
        Closed += (_, _) => session.Dispose();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Z) { undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
            if (e.Key == Key.Y) { redo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled = true; }
        };
    }
    private static Brush Checkerboard()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var gray = new SolidColorBrush(Color.FromRgb(230, 235, 242));
        drawing.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        drawing.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new(0, 0, 24, 24), Stretch = Stretch.None };
    }
    private static void Button(Panel panel, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new(12, 7, 12, 7), Margin = new(0, 4, 7, 4) };
        if (primary) { button.Background = Brushes.RoyalBlue; button.Foreground = Brushes.White; }
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private void Run(Action action)
    {
        try { action(); status.Foreground = Brushes.SlateGray; }
        catch (Exception e) { status.Text = e.Message; status.Foreground = Brushes.Firebrick; }
    }
    private void Select(string id)
    {
        selected = id; surface.SelectedElement = id; surface.InvalidateVisual();
        refreshing = true; elements.SelectedItem = elements.Items.Cast<Item>().FirstOrDefault(x => x.Id == id); refreshing = false;
        BuildProperties();
    }
    private void Refresh()
    {
        var theme = session.Current; surface.SetTheme(theme, session.Directory);
        refreshing = true;
        elements.ItemsSource = new[] { new Item("background", "背景与画布"), new Item("preedit", "拼音文字"), new Item("candidates", "候选文字") }
            .Concat(theme.Layers.Select(x => new Item("layer:" + x.Id, (x.Visible ? "▧ " : "○ ") + x.Name))).ToArray();
        if (!elements.Items.Cast<Item>().Any(x => x.Id == selected)) selected = "preedit";
        elements.SelectedItem = elements.Items.Cast<Item>().Single(x => x.Id == selected); refreshing = false;
        surface.SelectedElement = selected; undo.IsEnabled = session.CanUndo; redo.IsEnabled = session.CanRedo;
        status.Text = session.IsDirty ? "布局有未保存修改；另存副本后可应用。" : "源皮肤保持不变。选择元素开始编辑。";
    }
    private void Heading(string text) => properties.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 12) });
    private void Note(string text) => properties.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new(0, 4, 0, 10) });
    private void Field(string label, string value, Action<string> apply)
    {
        properties.Children.Add(new TextBlock { Text = label, Margin = new(0, 6, 0, 4) });
        var box = new TextBox { Text = value, Padding = new(7, 5, 7, 5) }; properties.Children.Add(box);
        var last = value;
        void Commit()
        {
            if (box.Text == last) return;
            try { apply(box.Text); last = box.Text; invalidFields.Remove(box); box.ClearValue(Border.BorderBrushProperty); status.Foreground = Brushes.SlateGray; }
            catch (Exception e) { invalidFields.Add(box); box.BorderBrush = Brushes.Firebrick; status.Text = e.Message; status.Foreground = Brushes.Firebrick; }
        }
        commits.Add(Commit);
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
    }
    private void Number(string label, double value, Action<double> apply) => Field(label, value.ToString("0.##", CultureInfo.InvariantCulture), text =>
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidDataException("请填写有效数字。");
        apply(number);
    });
    private void Choice(string label, string value, (string Id, string Label)[] choices, Action<string> apply)
    {
        properties.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 4) });
        var box = new ComboBox { ItemsSource = choices.Select(x => new Item(x.Id, x.Label)).ToArray(), Padding = new(5) };
        box.SelectedItem = box.Items.Cast<Item>().FirstOrDefault(x => x.Id == value); properties.Children.Add(box);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Item item && item.Id != value) Run(() => { apply(item.Id); BuildProperties(); }); };
    }
    private void BuildProperties()
    {
        commits.Clear(); invalidFields.Clear(); properties.Children.Clear(); var t = session.Current;
        if (selected == "background")
        {
            Heading("背景与画布");
            Number("画布最小宽度 / DIP", t.Layout.MinWidth, x => session.Update(v => v with { Layout = v.Layout with { MinWidth = x } }));
            Number("画布最小高度 / DIP", t.Layout.MinHeight, x => session.Update(v => v with { Layout = v.Layout with { MinHeight = x } }));
            Note("文字超出最小尺寸时画布会扩展；装饰图层不会撑大画布。");
            Button(properties, "替换背景图片…", () => PickImage(path => { var image = session.CopyImage(path); session.Update(v => v with { Background = v.Background with { Image = image, Mode = "stretch", Slice = null } }); BuildProperties(); }));
            Field("底色 #AARRGGBB", t.Background.Color, x => session.Update(v => v with { Background = v.Background with { Color = x } }));
            Choice("背景绘制", t.Background.Mode, [("solid", "纯色"), ("stretch", "整图拉伸"), ("tile", "原尺寸平铺"), ("nineSlice", "九宫格")], x => session.Update(v => v with { Background = v.Background with { Mode = x } }));
            if (t.Background.Mode == "nineSlice") Slice(() => { var b = session.Current.Background; return (b.Slice ?? new(0, 0, 0, 0), b.HorizontalFill, b.VerticalFill); },
                (s, h, v) => session.Update(theme => theme with { Background = theme.Background with { Slice = s, HorizontalFill = h, VerticalFill = v } }));
        }
        else if (selected is "preedit" or "candidates")
        {
            var isP = selected == "preedit"; Heading(isP ? "拼音文字位置" : "候选文字位置");
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var elementId = selected;
            var b = surface.Elements.Single(x => x.Id == elementId).Bounds;
            void Place(double? x = null, double? y = null)
            {
                var now = surface.Elements.Single(e => e.Id == elementId).Bounds;
                session.Update(v => v with { Layout = isP ? v.Layout with { PreeditPosition = new(x ?? now.X, y ?? now.Y) } : v.Layout with { CandidatePosition = new(x ?? now.X, y ?? now.Y) } });
            }
            Number("X / DIP（画布左上角起）", b.X, x => Place(x: x)); Number("Y / DIP", b.Y, y => Place(y: y));
            Button(properties, "恢复自动定位", () => Run(() => { session.Update(v => v with { Layout = isP ? v.Layout with { PreeditPosition = null } : v.Layout with { CandidatePosition = null } }); BuildProperties(); }));
            Number("文字字号 / DIP", t.Text.FontSize, x => session.Update(v => v with { Text = v.Text with { FontSize = x } }));
            if (!isP) Number("候选间距 / DIP", t.Layout.CandidateGap, x => session.Update(v => v with { Layout = v.Layout with { CandidateGap = x } }));
            Note("拖动后使用独立坐标。自动定位仍保留导入时的两行边距。");
        }
        else if (t.Layers.FirstOrDefault(x => "layer:" + x.Id == selected) is { } layer)
        {
            Heading("装饰图层"); var id = layer.Id;
            Field("名称", layer.Name, x => session.EditLayer(id, v => v with { Name = x }));
            Choice("锚点", layer.Anchor, [("topLeft", "左上"), ("topCenter", "上中"), ("topRight", "右上"), ("centerLeft", "左中"), ("center", "中心"), ("centerRight", "右中"), ("bottomLeft", "左下"), ("bottomCenter", "下中"), ("bottomRight", "右下")], x => session.EditLayer(id, v => v with { Anchor = x }));
            Number("X 偏移 / DIP", layer.X, x => session.EditLayer(id, v => v with { X = x }));
            Number("Y 偏移 / DIP", layer.Y, x => session.EditLayer(id, v => v with { Y = x }));
            Number("宽度 / DIP（0 为原图尺寸）", layer.Width, x => session.EditLayer(id, v => v with { Width = x }));
            Number("高度 / DIP（0 为原图尺寸）", layer.Height, x => session.EditLayer(id, v => v with { Height = x }));
            Number("不透明度 / %", layer.Opacity * 100, x => session.EditLayer(id, v => v with { Opacity = x / 100 }));
            Choice("绘制位置", layer.Plane, [("behindText", "文字下方"), ("aboveText", "文字上方")], x => session.EditLayer(id, v => v with { Plane = x }));
            Choice("显示", layer.Visible ? "yes" : "no", [("yes", "显示"), ("no", "隐藏")], x => session.EditLayer(id, v => v with { Visible = x == "yes" }));
            Choice("图像绘制", layer.Mode, [("stretch", "整图拉伸"), ("tile", "原尺寸平铺"), ("nineSlice", "九宫格")], x => session.EditLayer(id, v => v with { Mode = x }));
            if (layer.Mode == "nineSlice") Slice(() => { var l = session.Current.Layers.Single(x => x.Id == id); return (l.Slice ?? new(0, 0, 0, 0), l.HorizontalFill, l.VerticalFill); },
                (s, h, v) => session.EditLayer(id, l => l with { Slice = s, HorizontalFill = h, VerticalFill = v }));
            Button(properties, "替换图层图片…", () => PickImage(path => { var image = session.CopyImage(path); session.EditLayer(id, l => l with { Image = image, Slice = null }); BuildProperties(); }));
        }
    }
    private void Slice(Func<(Insets Slice, string Horizontal, string Vertical)> read, Action<Insets, string, string> apply)
    {
        var (s, horizontal, vertical) = read();
        void Change(Func<Insets, Insets>? transform = null, string? h = null, string? v = null)
        { var now = read(); apply(transform?.Invoke(now.Slice) ?? now.Slice, h ?? now.Horizontal, v ?? now.Vertical); }
        Note("切片单位为源图像素；边界之和须小于源图尺寸。");
        Number("左边界 / px", s.Left, x => Change(i => i with { Left = x }));
        Number("上边界 / px", s.Top, x => Change(i => i with { Top = x }));
        Number("右边界 / px", s.Right, x => Change(i => i with { Right = x }));
        Number("下边界 / px", s.Bottom, x => Change(i => i with { Bottom = x }));
        Choice("中间列水平填充", horizontal, [("stretch", "拉伸"), ("tile", "平铺")], x => Change(h: x));
        Choice("中间行垂直填充", vertical, [("stretch", "拉伸"), ("tile", "平铺")], x => Change(v: x));
    }
    private void PickImage(Action<string> apply)
    {
        var dialog = new OpenFileDialog { Filter = "静态图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) == true) Run(() => apply(dialog.FileName));
    }
    private void AddLayer() => PickImage(path => { var id = session.AddLayer(path); Select(id); });
    private void LayerAction(Action<string> action)
    {
        if (selected.StartsWith("layer:", StringComparison.Ordinal)) Run(() => { action(selected[6..]); BuildProperties(); });
        else status.Text = "请先选择一个装饰图层。";
    }
    private void Save(bool apply) => Run(() =>
    {
        // Flush the property currently being edited before publishing.
        foreach (var commit in commits.ToArray()) commit();
        if (invalidFields.Count != 0) throw new InvalidDataException("请先修正右侧标红的参数，再保存。");
        Keyboard.ClearFocus();
        var path = session.SaveCopy(name.Text);
        Saved?.Invoke(path, apply); saved = true; Close();
    });
    // Layout verification without showing a window or activating an input session.
    public void SavePreview(string path)
    {
        var content = (FrameworkElement)Content;
        content.Measure(new Size(Width, Height)); content.Arrange(new Rect(0, 0, Width, Height)); content.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, Width, Height));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null,
                new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed record Item(string Id, string Name) { public override string ToString() => Name; }
}
