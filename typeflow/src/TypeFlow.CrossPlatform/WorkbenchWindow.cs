using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Rendering.Avalonia;
using TypeFlow.Themes;

namespace TypeFlow.CrossPlatform;

// This host only coordinates shared services and the replaceable renderer.
// It is a workbench, not a registered operating-system input method.
public sealed class WorkbenchWindow : Window
{
    private readonly ThemeLibrary library;
    private readonly IInputEngine? engine;
    private readonly CandidateSurface surface = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ListBox themes = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
    private readonly TextBox input = new() { PlaceholderText = "在这里输入拼音", Text = "ni gu miao miao" };
    private readonly TextBox committed = new() { IsReadOnly = true, PlaceholderText = "选词结果", MinHeight = 68, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox copyName = new() { PlaceholderText = "副本名称", MinWidth = 180 };
    private readonly CheckBox editing = new() { Content = "拖动布局", IsChecked = false };
    private readonly Expander importReport = new() { IsVisible = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ThemeEditSession? edit;
    private ThemeConfigMonitor? monitor;
    private string selected = "";
    private bool refreshing, discardClose;
    public static CandidateView Demo { get; } = new("ni gu miao miao", [new("尼古喵喵"), new("你好"), new("输入法"), new("皮肤")]);
    public string CurrentEntry => selected;

    public WorkbenchWindow(ThemeLibrary library, IInputEngine? engine = null)
    {
        this.library = library; this.engine = engine;
        Title = "TypeFlow · 皮肤工作台"; Width = 1160; Height = 770; MinWidth = 900; MinHeight = 620;
        Background = new SolidColorBrush(Color.Parse("#F3F5F9"));
        FontFamily = new("Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, sans-serif");
        var root = new Grid { Margin = new(28), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 22 };
        Content = root;
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(new TextBlock { Text = "TypeFlow", FontSize = 30, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = App.ProductVersion + (engine == null ? " · 皮肤与布局工作台 · 演示候选模式" : " · 皮肤与布局工作台 · 已连接真实 Rime"), Foreground = Brushes.SlateGray });
        root.Children.Add(header);
        var body = new Grid { ColumnDefinitions = new("260,*"), ColumnSpacing = 22 }; Grid.SetRow(body, 1); root.Children.Add(body);
        var sidebar = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Spacing = 8, Margin = new(0, 0, 0, 14) }; DockPanel.SetDock(actions, Dock.Top); sidebar.Children.Add(actions);
        actions.Children.Add(new TextBlock { Text = "皮肤库", FontSize = 18, FontWeight = FontWeight.SemiBold });
        AddAsync(actions, "导入 SSF…", ImportSsf);
        AddAsync(actions, "导入原生皮肤目录…", ImportNative);
        AddAsync(actions, "关联 JSON / JSONC…", LinkConfig);
        themes.SelectionChanged += (_, _) =>
        {
            if (refreshing || themes.SelectedItem is not ThemeEntry entry) return;
            if (edit?.IsDirty == true)
            {
                SetStatus("请先保存副本或放弃修改，再切换皮肤。");
                ReloadList(); return;
            }
            Guard(() => OpenTheme(entry.Directory));
        };
        sidebar.Children.Add(themes); body.Children.Add(Card(sidebar));
        var content = new StackPanel { Spacing = 18 };
        var scroller = new ScrollViewer { Content = content };
        Grid.SetColumn(scroller, 1); body.Children.Add(scroller);
        var preview = new StackPanel { Spacing = 14 };
        preview.Children.Add(new TextBlock { Text = "候选窗预览", FontSize = 18, FontWeight = FontWeight.SemiBold });
        preview.Children.Add(new TextBlock { Text = "选择皮肤、编辑配置或拖动文字，即时查看布局。", Foreground = Brushes.SlateGray });
        preview.Children.Add(importReport);
        preview.Children.Add(new Border
        {
            Padding = new(24), Background = new SolidColorBrush(Color.Parse("#E7ECF4")), CornerRadius = new(8),
            Child = new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        });
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        editing.IsCheckedChanged += (_, _) => { surface.IsEditing = editing.IsChecked == true; surface.InvalidateVisual(); };
        controls.Children.Add(editing);
        Add(controls, "撤销", () => edit?.Undo()); Add(controls, "重做", () => edit?.Redo());
        Add(controls, "字号 −", () => ChangeFont(-1)); Add(controls, "字号 ＋", () => ChangeFont(1));
        Add(controls, "放弃修改", () => edit?.Reset());
        preview.Children.Add(controls);
        var publish = new WrapPanel(); publish.Children.Add(copyName);
        Add(publish, "保存副本", () => SaveCopy(copyName.Text ?? ""));
        Add(publish, "设为当前皮肤", () =>
        {
            if (edit?.IsDirty == true) { SetStatus("请先保存副本，再设为当前皮肤。"); return; }
            library.Activate(selected); SetStatus("已设为当前皮肤。");
        });
        preview.Children.Add(publish); content.Children.Add(Card(preview));
        var test = new StackPanel { Spacing = 12 };
        test.Children.Add(new TextBlock { Text = engine == null ? "演示输入" : "Rime 输入测试", FontSize = 18, FontWeight = FontWeight.SemiBold });
        test.Children.Add(new TextBlock
        {
            Text = engine == null ? "当前候选为固定演示数据。输入框用于预览拼音文字；点击候选可试选。" : "输入拼音，用空格、数字或鼠标选词。结果显示在下方。",
            Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap
        });
        test.Children.Add(input); test.Children.Add(committed); content.Children.Add(Card(test));
        Grid.SetRow(status, 2); root.Children.Add(status);
        input.TextChanged += (_, _) => { if (engine == null) surface.SetView(Demo with { Preedit = input.Text ?? "" }); };
        input.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        surface.CandidateSelected += index => Guard(() =>
        {
            if (engine == null) committed.Text = Demo.Candidates[index].Text;
            else ShowResult(engine.SelectCandidate(index));
        });
        surface.ElementSelected += id => SetStatus(id == "preedit" ? "已选中拼音文字；拖动或使用方向键移动。" : "已选中布局元素；拖动或使用方向键移动。");
        surface.ElementMoved += (id, dx, dy) => Guard(() =>
        {
            var bounds = surface.Elements.Single(x => x.Id == id).Bounds;
            edit!.Move(id, dx, dy, new(bounds.X, bounds.Y));
        });
        timer.Tick += (_, _) => Guard(PollConfig);
        Closing += async (_, e) =>
        {
            if (discardClose || edit?.IsDirty != true) return;
            e.Cancel = true;
            if (OwnedWindows.Any()) return;
            var dialog = new Window
            {
                Title = "尚未保存的布局", Width = 380, Height = 190, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var choices = new StackPanel { Margin = new(24), Spacing = 18 };
            choices.Children.Add(new TextBlock { Text = "布局修改尚未保存，是否放弃并关闭？", TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var keep = new Button { Content = "继续编辑" }; keep.Click += (_, _) => dialog.Close(false);
            var discard = new Button { Content = "放弃并关闭" }; discard.Click += (_, _) => dialog.Close(true);
            buttons.Children.Add(keep); buttons.Children.Add(discard); choices.Children.Add(buttons); dialog.Content = choices;
            if (await dialog.ShowDialog<bool>(this)) { discardClose = true; Close(); }
        };
        Closed += (_, _) => { timer.Stop(); surface.Dispose(); edit?.Dispose(); engine?.Dispose(); };
        try { OpenTheme(library.ActiveDirectory); }
        catch (Exception e) { OpenTheme(library.DefaultDirectory); SetStatus("当前皮肤加载失败，已恢复默认：" + e.Message); }
        surface.SetView(engine?.View ?? Demo);
        if (engine != null) input.Text = "";
        timer.Start();
    }
    private static Border Card(Control child) => new() { Background = Brushes.White, CornerRadius = new(12), Padding = new(20), Child = child };
    private void Add(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new(0, 0, 8, 6), Padding = new(12, 7) };
        button.Click += (_, _) => Guard(action); panel.Children.Add(button);
    }
    private void AddAsync(Panel panel, string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) =>
        {
            if (edit?.IsDirty == true) { SetStatus("请先保存副本或放弃修改，再导入皮肤。"); return; }
            button.IsEnabled = false;
            try { await action(); } catch (Exception e) { SetStatus(e.Message); }
            finally { button.IsEnabled = true; }
        };
        panel.Children.Add(button);
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { SetStatus(e.Message); } }
    private void SetStatus(string message) => status.Text = message;
    private void ReloadList()
    {
        refreshing = true;
        try
        {
            var entries = library.List().Select(x => new ThemeEntry(x.Directory, x.Theme.Name)).ToArray();
            themes.ItemsSource = entries; themes.SelectedItem = entries.FirstOrDefault(x => x.Directory == selected);
        }
        finally { refreshing = false; }
    }
    public void OpenTheme(string entry)
    {
        var next = new ThemeEditSession(library, entry, new AvaloniaThemeImageTranscoder());
        try { surface.SetTheme(next.Current, next.Directory); }
        catch { next.Dispose(); throw; }
        edit?.Dispose(); edit = next; selected = entry;
        next.Changed += () => surface.SetTheme(next.Current, next.Directory);
        LoadReport(entry);
        copyName.Text = next.Current.Name + " · 自定义";
        monitor = new(library, entry); _ = monitor.Poll();
        ReloadList(); SetStatus("已加载 " + next.Current.Name);
    }
    private void LoadReport(string entry)
    {
        importReport.IsVisible = false;
        var path = ThemeFiles.AssetPath(entry, "import-report.json");
        if (!File.Exists(path)) return;
        importReport.IsVisible = true;
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("导入报告过大。");
            var report = JsonSerializer.Deserialize<ImportReport>(File.ReadAllText(path), ThemeFiles.Json)
                ?? throw new InvalidDataException("导入报告为空。");
            var compatibility = report.Compatibility switch { "complete" => "完全兼容", "partial" => "部分兼容", _ => "尚未确认" };
            importReport.Header = "导入兼容性：" + compatibility;
            importReport.Content = new TextBlock { Text = string.Join("\n", report.Diagnostics.Select(x => x.Message)), TextWrapping = TextWrapping.Wrap };
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or ArgumentException)
        {
            importReport.Header = "导入报告无法读取";
            importReport.Content = new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap };
        }
    }
    public string SaveCopy(string name)
    {
        var entry = edit!.SaveCopy(name);
        OpenTheme(entry); SetStatus("副本已保存；源皮肤保持原样。");
        return entry;
    }
    private void ChangeFont(int delta) => edit?.Update(t => t with { Text = t.Text with { FontSize = Math.Clamp(t.Text.FontSize + delta, 6, 96) } });
    private async Task<string?> Pick(string title, params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = title, AllowMultiple = false, FileTypeFilter = [new(title) { Patterns = patterns }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException("请选择本地文件。");
    }
    private async Task ImportSsf()
    {
        var path = await Pick("导入 SSF", "*.ssf");
        if (path == null) return;
        var result = await library.Import(path); OpenTheme(result.ThemeDirectory);
        SetStatus("导入完成：" + result.Report.Compatibility + "；" + string.Join("；", result.Report.Diagnostics.Select(x => x.Message)));
    }
    private async Task ImportNative()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择含 theme.json 的皮肤目录", AllowMultiple = false });
        if (folders.Count == 0) return;
        var source = folders[0].TryGetLocalPath() ?? throw new NotSupportedException("请选择本地目录。");
        OpenTheme((await library.Import(source)).ThemeDirectory);
    }
    private async Task LinkConfig()
    {
        var path = await Pick("关联配置", "*.json", "*.jsonc");
        if (path != null) OpenTheme(library.Link(path));
    }
    private void PollConfig()
    {
        if (edit?.IsDirty != false || monitor == null) return;
        var result = monitor.Poll();
        if (result.Error != null) SetStatus("重载失败，保留有效预览：" + result.Error);
        else if (result.Changed) Guard(() =>
        {
            var currentMonitor = monitor;
            OpenTheme(selected);
            monitor = currentMonitor;
        });
    }
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (engine == null || string.IsNullOrEmpty(e.Text) || e.Text.Any(x => x > 127)) return;
        Guard(() => { foreach (var key in e.Text) ShowResult(engine.ProcessKey(key)); });
        e.Handled = true;
    }
    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (engine == null || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
        var key = e.Key switch
        {
            Key.Back => 0xff08, Key.Escape => 0xff1b, Key.Space => 32, Key.Enter => 0xff0d,
            Key.PageUp => 0xff55, Key.PageDown => 0xff56, Key.Left => 0xff51, Key.Up => 0xff52,
            Key.Right => 0xff53, Key.Down => 0xff54, _ => 0
        };
        if (key == 0) return;
        Guard(() => ShowResult(engine.ProcessKey(key))); e.Handled = true;
    }
    private void ShowResult(KeyResult result)
    {
        surface.SetView(result.View); input.Text = result.View.Preedit;
        if (result.Commit.Length > 0) committed.Text = (committed.Text ?? "") + result.Commit;
    }
    public void SavePreview(string path) => surface.SavePng(path);
    private sealed record ThemeEntry(string Directory, string Name) { public override string ToString() => Name; }
}
