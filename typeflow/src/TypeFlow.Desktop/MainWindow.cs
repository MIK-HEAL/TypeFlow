using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;
using TypeFlow.Engine.Rime;

namespace TypeFlow.Desktop;

public sealed class MainWindow : Window
{
    private readonly ThemeLibrary library;
    private readonly CandidateSurface surface = new();
    private readonly ListBox themes = new();
    private readonly TextBox report = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox editor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 18, MinHeight = 110, Padding = new(12) };
    private readonly TextBlock engineStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 10) };
    private IInputEngine? engine;
    private bool chinese = true;
    private string selected = "";
    private ThemeConfigMonitor? configMonitor;
    private readonly DispatcherTimer configTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string reportBase = "";
    private bool reloading;
    private bool configFailed;
    private readonly Action<bool>? systemMode;
    private readonly Func<bool>? systemModeEnabled;
    public MainWindow(ThemeLibrary library, string productRoot, Action<bool>? systemMode = null, Func<bool>? systemModeEnabled = null)
    {
        this.library = library;
        this.systemMode = systemMode; this.systemModeEnabled = systemModeEnabled;
        Title = systemMode == null ? "TypeFlow · 皮肤与输入实验室" : "TypeFlow · 皮肤设置";
        Width = Math.Min(1120, SystemParameters.WorkArea.Width - 32); Height = Math.Min(780, SystemParameters.WorkArea.Height - 32);
        MinWidth = 800; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14; Background = new SolidColorBrush(Color.FromRgb(244, 246, 250));
        var layout = new DockPanel { Margin = new(24) }; Content = layout;
        var heading = new TextBlock { Text = "TypeFlow", FontSize = 30, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var subtitle = new TextBlock { Text = systemMode == null ? "导入皮肤，预览外观，再试一段输入。" : "导入或选择皮肤，点击应用后即可用于系统输入。", Foreground = Brushes.DimGray, Margin = new(0, 4, 0, 20) };
        DockPanel.SetDock(subtitle, Dock.Top); layout.Children.Add(subtitle);
        var sidebar = new DockPanel { Width = 230, Margin = new(0, 0, 22, 0) }; DockPanel.SetDock(sidebar, Dock.Left); layout.Children.Add(sidebar);
        var actions = new StackPanel();
        var actionScroll = new ScrollViewer { Content = actions, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(actionScroll, Dock.Bottom); sidebar.Children.Add(actionScroll);
        AddButton(actions, "导入 SSF…", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "搜狗皮肤 (*.ssf)|*.ssf|所有文件|*.*" };
            if (dialog.ShowDialog(this) == true) await Import(dialog.FileName);
        });
        AddButton(actions, "导入原生皮肤目录…", async () =>
        {
            var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) await Import(dialog.FolderName);
        });
        AddButton(actions, "关联配置文件…", () => Guard(() =>
        {
            var dialog = new OpenFileDialog { Filter = "原生皮肤配置 (*.json;*.jsonc)|*.json;*.jsonc" };
            if (dialog.ShowDialog(this) == true) { var path = library.Link(dialog.FileName); Reload(); Load(path); }
        }));
        var configButton = AddButton(actions, "编辑配置文件", () => Guard(OpenConfiguration));
        configButton.ContextMenu = new ContextMenu();
        var locate = new MenuItem { Header = "打开配置所在目录" };
        locate.Click += (_, _) => Guard(() =>
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select," + library.ConfigFile(selected)); Process.Start(start);
        });
        configButton.ContextMenu.Items.Add(locate);
        AddButton(actions, "立即重载配置", () => { PollConfiguration(true); if (systemMode != null && !configFailed) Guard(() => ApplyTheme(selected)); });
        AddButton(actions, systemMode == null ? "应用到候选窗" : "应用到系统输入法", () => Guard(() => ApplyTheme(selected)));
        AddButton(actions, "高级布局自定义…", () => Guard(() =>
        {
            var window = new LayoutEditorWindow(library, selected, engine?.View ?? Program.Demo) { Owner = this };
            window.Saved += (path, apply) => { Reload(); Load(path); if (apply) ApplyTheme(path); };
            window.ShowDialog();
        }));
        AddButton(actions, "恢复默认皮肤", () => Guard(() => { Load(library.DefaultDirectory); ApplyTheme(library.DefaultDirectory); }));
        if (systemMode != null) AddButton(actions, "使用系统原生候选窗", () => Guard(() => { systemMode(false); UpdateStatus(); report.Text += "\n已切换为原生候选窗，导入的皮肤仍保留。"; }));
        AddButton(actions, "删除导入的皮肤", () => Guard(() => { library.Delete(selected); Reload(); Load(library.ActiveDirectory); }));
        sidebar.Children.Add(themes); themes.SelectionChanged += (_, _) => { if (!reloading && themes.SelectedItem is ThemeItem item) Guard(() => Load(item.Directory)); };
        var main = new Grid(); layout.Children.Add(main);
        main.RowDefinitions.Add(new() { Height = new GridLength(260) }); main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var preview = new Border { Background = Brushes.White, CornerRadius = new(12), Padding = new(18), Margin = new(0, 0, 0, 16) };
        preview.Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Child = surface, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center } };
        main.Children.Add(preview);
        var inputPanel = new StackPanel(); Grid.SetRow(inputPanel, 1); main.Children.Add(inputPanel);
        inputPanel.Children.Add(new TextBlock { Text = "输入测试区", FontSize = 17, FontWeight = FontWeights.SemiBold });
        inputPanel.Children.Add(engineStatus); inputPanel.Children.Add(editor);
        var helpers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 10, 0, 10) }; Grid.SetRow(helpers, 2); main.Children.Add(helpers);
        if (systemMode == null) AddButton(helpers, "中 / 英", () => { chinese = !chinese; engine?.Clear(); surface.SetView(engine?.View ?? Program.Demo); UpdateStatus(); });
        else helpers.Children.Add(new TextBlock { Text = "Shift 切换中英文 · 空格/数字选词", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        AddButton(helpers, "重置示例文字", () => surface.SetView(Program.Demo));
        helpers.Children.Add(new TextBlock { Text = "预览缩放", VerticalAlignment = VerticalAlignment.Center });
        var zoom = new ComboBox { ItemsSource = new[] { "100%", "150%", "200%" }, SelectedIndex = 0, Margin = new(12, 4, 0, 4), MinWidth = 90 };
        zoom.SelectionChanged += (_, _) => { var s = new[] { 1.0, 1.5, 2.0 }[zoom.SelectedIndex]; surface.LayoutTransform = new ScaleTransform(s, s); }; helpers.Children.Add(zoom);
        Grid.SetRow(report, 3); main.Children.Add(report);
        Reload();
        try { Load(library.ActiveDirectory); }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or NotSupportedException or ArgumentException or JsonException)
        { Load(library.DefaultDirectory); report.Text += "\n上次皮肤无法加载，已恢复默认：" + e.Message; }
        surface.SetView(Program.Demo);
        var dll = Path.Combine(productRoot, "runtime/rime.dll");
        if (File.Exists(dll)) Guard(() => engine = new RimeEngine(dll, Path.Combine(productRoot, "rime-data"), Path.Combine(library.Root, "rime-user"), "luna_pinyin_simp"));
        UpdateStatus();
        InputMethod.SetIsInputMethodEnabled(editor, systemMode != null);
        if (systemMode == null) editor.PreviewKeyDown += KeyDown;
        surface.CandidateSelected += i => { if (engine != null && i < engine.View.Candidates.Count) Guard(() => Apply(engine.SelectCandidate(i))); editor.Focus(); };
        configTimer.Tick += (_, _) => PollConfiguration(); configTimer.Start();
        Closed += (_, _) => { configTimer.Stop(); engine?.Dispose(); };
        AllowDrop = true; DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) await Import(files[0]); };
    }
    private void UpdateStatus() => engineStatus.Text = systemMode != null
        ? $"{(systemModeEnabled?.Invoke() == true ? "正在使用自定义皮肤" : "正在使用原生候选窗")} · 下方可直接试打，上方为皮肤示例。"
        : engine == null ? "引擎尚未加载；当前展示皮肤预览。运行时准备方式见 README。" : $"Rime 真实输入 · {(chinese ? "中文" : "英文")} · 空格选词 / 数字选词 / Esc 取消 / PageUp、PageDown 翻页";
    private void ApplyTheme(string directory)
    {
        library.Validator.Validate(ThemeFiles.Load(Path.Combine(directory, "theme.json")), directory);
        library.Activate(directory); systemMode?.Invoke(true); UpdateStatus();
        report.Text += systemMode == null ? "\n已应用。输入会话保持不变。" : "\n已应用到系统输入法。关闭设置窗口后皮肤仍生效。";
    }
    private static Button AddButton(Panel parent, string label, Action action)
    {
        var button = new Button { Content = label, Padding = new(10, 7, 10, 7), Margin = new(0, 4, 6, 4) };
        button.Click += (_, _) => action(); parent.Children.Add(button); return button;
    }
    private async Task Import(string source)
    {
        try { var result = await Task.Run(() => library.Import(source)); Reload(); Load(result.ThemeDirectory); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    private void Reload()
    {
        reloading = true;
        try
        {
            themes.ItemsSource = library.List().Select(x => new ThemeItem(x.Directory, x.Theme.Name + (File.Exists(Path.Combine(x.Directory, "link.json")) ? " [关联]" : ""))).ToArray();
            themes.SelectedItem = themes.Items.Cast<ThemeItem>().FirstOrDefault(x => x.Directory == selected);
        }
        finally { reloading = false; }
    }
    private void Load(string directory, bool monitor = true)
    {
        var theme = ThemeFiles.Load(Path.Combine(directory, "theme.json"));
        surface.SetTheme(theme, directory); selected = directory;
        var file = Path.Combine(directory, "import-report.json");
        report.Text = $"{theme.Name} · {theme.Author}\n{theme.License}\n";
        if (File.Exists(file))
        {
            var r = JsonSerializer.Deserialize<ImportReport>(File.ReadAllText(file), ThemeFiles.Json)!;
            report.Text += $"兼容程度：{r.Compatibility}\n" + string.Join("\n", r.Diagnostics.Select(x => "• " + x.Message));
        }
        report.Text += "\n配置文件：" + library.ConfigFile(directory) + "\n保存后约 1 秒自动重载；原生配置支持 JSON/JSONC。";
        reportBase = report.Text;
        configFailed = false;
        if (monitor) configMonitor = new ThemeConfigMonitor(library, directory);
    }
    private void PollConfiguration(bool force = false)
    {
        if (configMonitor == null) return;
        var result = configMonitor.Poll(force);
        if (result.Error != null) { configFailed = true; report.Text = reportBase + "\n重载失败，继续显示上次有效皮肤：" + result.Error; return; }
        if (result.Changed) Guard(() => { Load(selected, false); Reload(); report.Text = reportBase + "\n配置已重载，输入会话保持不变。"; });
        else if (force || configFailed) { configFailed = false; report.Text = reportBase + "\n配置有效，已继续使用当前皮肤。"; }
    }
    private void OpenConfiguration()
    {
        if (selected == library.DefaultDirectory)
        {
            using var draft = new ThemeEditSession(library, selected, new WpfThemeImageTranscoder());
            var path = draft.SaveCopy("默认 · 我的配置"); Reload(); Load(path);
        }
        Process.Start(new ProcessStartInfo(library.ConfigFile(selected)) { UseShellExecute = true });
    }
    private new void KeyDown(object sender, KeyEventArgs e)
    {
        if (engine == null || !chinese || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
        var key = e.Key;
        int code = key switch
        {
            >= Key.A and <= Key.Z => 'a' + key - Key.A,
            >= Key.D0 and <= Key.D9 => '0' + key - Key.D0,
            Key.Space => 32, Key.Back => 0xff08, Key.Escape => 0xff1b, Key.Return => 0xff0d,
            Key.Left => 0xff51, Key.Right => 0xff53, Key.Up => 0xff52, Key.Down => 0xff54,
            Key.PageUp => 0xff55, Key.PageDown => 0xff56, Key.OemQuotes => 39,
            _ => 0
        };
        if (code == 0) return;
        if (key is >= Key.A and <= Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) code = char.ToUpperInvariant((char)code);
        Guard(() => { var result = engine.ProcessKey(code); Apply(result); e.Handled = result.Handled; });
    }
    private void Apply(KeyResult result)
    {
        if (result.Commit.Length != 0) { editor.SelectedText = result.Commit; editor.CaretIndex = editor.SelectionStart + result.Commit.Length; editor.SelectionLength = 0; }
        surface.SetView(result.View);
    }
    private sealed record ThemeItem(string Directory, string Name) { public override string ToString() => Name; }
}
