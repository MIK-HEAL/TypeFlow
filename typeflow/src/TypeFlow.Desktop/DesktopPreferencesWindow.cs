using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeFlow.Desktop;

internal sealed class DesktopPreferencesWindow : Window
{
    public DesktopPreferencesWindow(Func<DesktopPreferences> read,
        Action<Func<DesktopPreferences, DesktopPreferences>> save, Action resetPosition)
    {
        Title = "挂件操作与候选窗位置"; Width = 480; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new(24) }; Content = panel;
        var value = read();
        void Save(Func<DesktopPreferences, DesktopPreferences> update)
        {
            try { save(update); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new(0, 12, 0, 6), FontWeight = FontWeights.SemiBold });
        void Choice(string[] labels, int selected, Action<int> change)
        {
            var combo = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, selected), Padding = new(6) };
            combo.SelectionChanged += (_, _) => change(combo.SelectedIndex); panel.Children.Add(combo);
        }
        Label("点击挂件动画时");
        var actions = new[] { "voice", "script", "language", "settings", "none" };
        Choice(["打开语音输入", "切换简繁体", "切换中英文", "打开皮肤设置", "不执行操作"],
            Array.IndexOf(actions, value.CompanionClick), i => Save(p => p with { CompanionClick = actions[i] }));
        var locked = new CheckBox { Content = "锁定挂件位置（仍可点击操作）", IsChecked = value.CompanionLocked, Margin = new(0, 12, 0, 8) };
        locked.Click += (_, _) => Save(p => p with { CompanionLocked = locked.IsChecked == true }); panel.Children.Add(locked);
        var reset = new Button { Content = "恢复挂件到屏幕右下角", Padding = new(8), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { Save(p => p with { CompanionLeft = null, CompanionTop = null }); resetPosition(); }; panel.Children.Add(reset);
        Label("候选窗位置");
        var placements = new[] { "below", "right", "bottom" };
        Choice(["光标下方优先（空间不足时避让到侧边）", "光标右侧优先", "固定在当前屏幕底部"],
            Array.IndexOf(placements, value.CandidatePlacement), i => Save(p => p with { CandidatePlacement = placements[i] }));
        Label("与光标的间距");
        var gaps = new[] { 4.0, 10, 16, 24, 40 };
        Choice(["紧凑 · 4", "舒适 · 10（默认）", "宽松 · 16", "较远 · 24", "更远 · 40"],
            Array.IndexOf(gaps, value.CandidateGap), i => Save(p => p with { CandidateGap = gaps[i] }));
        Label("候选窗缩放（文字和装饰一起缩放）");
        var scales = new[] { .5, .75, 1, 1.25, 1.5 };
        Choice(["50%", "75%", "100%（默认）", "125%", "150%"], Array.IndexOf(scales, value.CandidateScale),
            i => Save(p => p with { CandidateScale = scales[i] }));
        panel.Children.Add(new TextBlock { Text = "设置自动保存。拖动动画可移动挂件，右键可隐藏或解锁。\n简繁 / 中英文按钮使用 TypeFlow 默认快捷键。\n语音使用 Windows 语音输入，请先点入输入框；首次使用按系统提示开启麦克风和语音服务。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new(0, 18, 0, 12) });
        var close = new Button { Content = "完成", IsCancel = true, Padding = new(20, 8, 20, 8), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(); panel.Children.Add(close);
    }
}
