using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Markup;

namespace DeskWidgets;
public sealed class ComponentCenter : Window
{
    private readonly WidgetManager manager;
    private readonly TextBox search = new() { Name = "ComponentSearch", Width = 270, Height = 36, Padding = new Thickness(12, 8, 12, 8), ToolTip = "搜索组件名称或目录", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly WrapPanel flow = new() { Name = "ComponentFlow", Margin = new Thickness(-6, 8, -6, 0) };
    private readonly TextBlock summary = new();
    private readonly StackPanel filters = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroll;
    private string filter = "全部";
    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static readonly Brush Muted = Brush("#9EAFBF"), Blue = Brush("#A8DDF8");
    public ComponentCenter(WidgetManager manager)
    {
        this.manager = manager;
        NameScope.SetNameScope(this, new NameScope());
        RegisterName("ComponentSearch", search); RegisterName("ComponentFlow", flow); RegisterName("ComponentFilters", filters);
        Title = "DeskWidgets · 组件中心"; Width = 1060; Height = 790; MinWidth = 660; MinHeight = 460;
        FontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei UI"); FontSize = 12;
        Background = new LinearGradientBrush(Color.FromRgb(30, 43, 61), Color.FromRgb(14, 24, 38), 70);
        Foreground = Brush("#EDF4FB");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/deskwidgets.ico"));
        Resources.Add(typeof(Button), XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Setter Property="Foreground" Value="#E5F2FC"/><Setter Property="Background" Value="#283E55"/>
              <Setter Property="BorderBrush" Value="#446078"/><Setter Property="BorderThickness" Value="1"/>
              <Setter Property="Padding" Value="10,7"/><Setter Property="Cursor" Value="Hand"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="Frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
                <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Background" Value="#3C5872"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#A8DDF8"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.7"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """));
        search.Background = Brush("#28394D"); search.Foreground = Foreground; search.BorderBrush = Brush("#52677F");
        var root = new DockPanel { Margin = new Thickness(26, 22, 26, 16), Background = Background }; Content = root;
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 20) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var brand = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/assets/deskwidgets-mark.png")), Width = 43, Height = 43, Margin = new Thickness(0, 0, 14, 0) }; heading.Children.Add(brand);
        var titles = new StackPanel(); titles.Children.Add(Text("组件中心", 26)); titles.Children.Add(Text("让桌面各就其位", 12, Muted)); heading.Children.Add(titles);
        var global = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
        global.Children.Add(Button("从右侧排列", manager.ArrangeFromRight)); global.Children.Add(Button("全部缩到最小", manager.ShrinkAll, new Thickness(8, 0, 0, 0)));
        DockPanel.SetDock(global, Dock.Top); root.Children.Add(global);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(filters, Dock.Right); toolbar.Children.Add(filters);
        foreach (string label in new[] { "全部", "已开启", "未开启" }) filters.Children.Add(Button(label, () => { filter = label; Refresh(); }, margin: new Thickness(5, 0, 0, 0)));
        var searchBox = new StackPanel { Orientation = Orientation.Horizontal }; searchBox.Children.Add(search); searchBox.Children.Add(new TextBlock { Text = "搜索组件", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }); toolbar.Children.Add(searchBox);
        DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary); summary.Foreground = Muted;
        var footer = Text("每张卡片独立保存  ·  新建实例可多开  ·  关闭中心后继续驻留托盘", 11, Muted); footer.Margin = new Thickness(0, 14, 0, 0); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        scroll = new ScrollViewer { Content = flow, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
        scroll.SizeChanged += (_, _) => LayoutCards();
        scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0) LayoutCards(); };
        search.TextChanged += (_, _) => Refresh();
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter && flow.Children.OfType<Border>().FirstOrDefault()?.Tag is WidgetDefinition definition) manager.Wake(definition); };
        Loaded += (_, _) => search.Focus(); manager.Changed += Refresh;
        Closed += (_, _) => manager.Changed -= Refresh;
        Refresh();
    }
    private static TextBlock Text(string value, double size = 12, Brush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? Brush("#EDF4FB"), TextWrapping = TextWrapping.Wrap };
    private Button Button(string label, Action action, Thickness? margin = null)
    {
        var button = new Button { Content = label, Margin = margin ?? new Thickness(0) };
        button.Click += (_, _) => { try { action(); } catch (Exception error) { App.Report(error); } }; return button;
    }
    private Button IconButton(string glyph, string label, Action action)
    {
        var button = Button(glyph, action, new Thickness(3, 0, 0, 0)); button.FontFamily = new FontFamily("Segoe MDL2 Assets"); button.Padding = new Thickness(7); button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label); return button;
    }
    private static (string Name, string Description, string Detail, string Glyph, string Color) Presentation(WidgetDefinition d) => d.Directory switch
    {
        "world-clock" => ("世界时钟", "每张卡片独立选择地区", "UTC / 北京 / 纽约 / 洛杉矶\n下拉切换，多实例独立保存", "\uE917", "#9BCFF4"),
        "clock-beijing" => ("北京时间", "与国内的日常保持同步", "Asia / Shanghai\nUTC+8 中国标准时间", "\uE917", "#E7C397"),
        "clock-new-york" => ("纽约 · 美东", "自动适应美国夏令时", "America / New York\nEST / EDT 自动切换", "\uE917", "#B9BAEC"),
        "clock-los-angeles" => ("洛杉矶 · 美西", "太平洋时区的独立时钟", "America / Los Angeles\nPST / PDT 自动切换", "\uE917", "#D7ADCA"),
        "weather-card" => ("天气", "每张卡片独立选择城市", "新建时选择地区，可搜索全球城市\n实时预报与程序化动态背景", "\uE753", "#9BCEEC"),
        "weather" => ("简约天气", "轻量天气与城市背景", "当前温度与天气\n支持城市切换", "\uE706", "#9BCEEC"),
        "almanac" => ("黄历", "公历、农历与传统日历", "日期与节气\n随卡片尺寸调整信息量", "\uE787", "#D8CC9F"),
        "system-monitor" => ("系统监控", "CPU、GPU 与内存使用情况", "进度条 / 雷达 / 圆环 / 折线\n按需启用指标", "\uE9D9", "#9BD2C4"),
        "audio-control" => ("音量与电平", "系统音量与输出峰值", "音量调节与静音\n数字信号电平 dBFS", "\uE767", "#B5BCE8"),
        "clipboard-cache" => ("剪贴板缓存", "点选复制，回到应用手动粘贴", "文本与图片 · 随尺寸排列\n点击复制，手动粘贴", "\uE70B", "#9FDAD0"),
        "notes" => ("轻量便签", "把临时想法留在桌面", "纯文本自动保存\n每个实例独立记录", "\uE70B", "#E7CCA1"),
        "countdown" => ("日程倒计时", "给重要时刻留一个位置", "本地日程与倒计时\n多开可记录不同事件", "\uE787", "#A6D8CF"),
        "media-player" => ("媒体播放", "系统公开的当前播放会话", "标题、作者与播放状态\n上一首 / 播放暂停 / 下一首", "\uE8D6", "#C0B5E8"),
        "focus-timer" => ("专注与短待办", "留一段时间给重要的事", "专注目标与计时\n支持暂停、继续和恢复", "\uE916", "#DDBBA9"),
        "workspace-launcher" => ("工作区入口", "常用目录与网址一步打开", "文件夹 / 快捷方式 / 网页\n自定义你的工作入口", "\uE8B7", "#9FC5ED"),
        _ => (d.Name, "本地扩展组件", $"默认尺寸 {d.Width:0} × {d.Height:0}\n支持独立实例", "\uE80A", "#B6CDE1")
    };
    private static FrameworkElement CategoryIcon(string glyph, string color)
    {
        string path = glyph switch
        {
            "\uE917" => "M12,2 A10,10 0 1 1 11.99,2 M12,6 V12 L16,14",
            "\uE753" => "M6,18 C0,18 1,10 6,10 C6,2 18,2 19,10 C25,10 25,18 19,18 Z",
            "\uE706" => "M12,6 A6,6 0 1 1 11.99,6 M12,1 V3 M12,21 V23 M1,12 H3 M21,12 H23 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
            "\uE787" => "M3,5 H21 V22 H3 Z M3,10 H21 M7,2 V7 M17,2 V7 M7,14 H10 M14,14 H17 M7,18 H10",
            "\uE9D9" => "M1,13 H5 L8,5 L12,20 L16,9 L19,13 H23",
            "\uE767" => "M3,9 H7 L12,4 V20 L7,15 H3 Z M16,8 Q21,12 16,16 M19,4 Q27,12 19,20",
            "\uE70B" => "M5,2 H19 V22 H5 Z M8,7 H16 M8,11 H16 M8,15 H16 M8,19 H12",
            "\uE8D6" => "M8,17 V5 L21,2 V14 M8,5 L21,2 M8,17 C8,22 1,22 1,18 C1,15 8,14 8,17 M21,14 C21,19 14,19 14,15 C14,12 21,11 21,14",
            "\uE916" => "M9,2 H15 M12,2 V5 M12,5 A8,8 0 1 1 11.99,5 M12,8 V13 L15,15 M18,5 L20,3",
            "\uE8B7" => "M2,6 H10 L13,9 H22 V21 H2 Z M2,6 V3 H10 L13,6 H22 V9",
            _ => "M2,2 H10 V10 H2 Z M14,2 H22 V10 H14 Z M2,14 H10 V22 H2 Z M14,14 H22 V22 H14 Z"
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = Brush(color), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return new Viewbox { Width = 24, Height = 24, Child = canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
    private void LayoutCards()
    {
        double available = Math.Max(280, scroll.ViewportWidth - 2);
        int columns = Math.Max(1, (int)(available / 300)); double width = Math.Floor(available / columns) - 12;
        foreach (var card in flow.Children.OfType<Border>()) card.Width = width;
    }
    private void Refresh()
    {
        double offset = scroll.VerticalOffset;
        flow.Children.Clear();
        var definitions = manager.Catalog(); var instances = manager.OpenWindows;
        foreach (var choice in filters.Children.OfType<Button>()) { bool active = (string)choice.Content == filter; choice.Background = active ? Blue : Brush("#25364B"); choice.Foreground = active ? Brush("#122333") : Foreground; }
        foreach (var definition in definitions.OrderByDescending(d => instances.Any(w => WidgetKinds.Family(w.State.WidgetName) == WidgetKinds.Family(d.Directory))))
        {
            var p = Presentation(definition); var opened = instances.Where(w => WidgetKinds.Family(w.State.WidgetName) == WidgetKinds.Family(definition.Directory)).ToArray();
            if (!(p.Name + " " + definition.Name + " " + definition.Directory + " " + p.Detail + " " + string.Join(" ", opened.Select(w => WidgetKinds.Context(w.State)))).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) || filter == "已开启" && opened.Length == 0 || filter == "未开启" && opened.Length > 0) continue;
            var content = new StackPanel();
            var card = new Border { Tag = definition, Margin = new Thickness(6), Padding = new Thickness(16), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), BorderBrush = Brush(opened.Length > 0 ? "#46657A" : "#34465B"), Background = new LinearGradientBrush(Color.FromRgb(40, 56, 74), Color.FromRgb(28, 41, 58), 90), Child = content };
            var top = new DockPanel(); content.Children.Add(top);
            var badge = new Border { Background = Brush(opened.Length > 0 ? "#9DE3CC" : "#3D4C60"), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Top, Child = Text(opened.Length > 0 ? $"已开启 {opened.Length}" : "未开启", 10, opened.Length > 0 ? Brush("#123C32") : Brush("#D3DEEB")) }; DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Border { Width = 37, Height = 37, CornerRadius = new CornerRadius(9), Background = Brush("#3B5068"), Margin = new Thickness(0, 0, 9, 0), Child = CategoryIcon(p.Glyph, p.Color) });
            var titleText = Text(p.Name, 16); titleText.FontWeight = FontWeights.SemiBold; titleText.VerticalAlignment = VerticalAlignment.Center; title.Children.Add(titleText); top.Children.Add(title);
            var description = Text(p.Description, 11, Muted); description.Margin = new Thickness(0, 11, 0, 9); content.Children.Add(description);
            var detail = Text(p.Detail, 12, Brush(p.Color)); detail.LineHeight = 22;
            content.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8), Background = Brush("#182A3C"), Child = detail });
            for (int index = 0; index < opened.Length; index++)
            {
                var window = opened[index]; var state = window.State;
                var row = new DockPanel();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
                actions.Children.Add(IconButton("\uE8A7", "置顶显示此实例", () => { window.IsPinnedTop = true; window.Show(); WindowState = WindowState.Minimized; }));
                actions.Children.Add(IconButton(state.Locked ? "\uE72E" : "\uE785", state.Locked ? "解锁" : "锁定", () => { state.Locked = !state.Locked; window.ApplyLock(); manager.ScheduleSave(); }));
                actions.Children.Add(IconButton("\uE718", state.Topmost ? "沉到底层" : "置顶", () => window.IsPinnedTop = !window.IsPinnedTop));
                actions.Children.Add(IconButton("\uE72C", "重载此实例", window.Reload));
                actions.Children.Add(IconButton("\uE711", "关闭此实例", window.Close));
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(Text($"{(WidgetKinds.Context(state) is { Length: > 0 } label ? label : $"实例 {index + 1}")}  ·  {state.Id[..Math.Min(4, state.Id.Length)]}", 11));
                info.Children.Add(Text($"{state.Width:0} × {state.Height:0} · {(state.Topmost ? "置顶" : "桌面层")}{(state.Locked ? " · 已锁定" : "")}", 10, Muted)); row.Children.Add(info);
                content.Children.Add(new Border { Padding = new Thickness(8, 7, 8, 7), Background = Brush("#23374B"), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
            var add = Button(opened.Length > 0 ? "＋ 新建实例" : "＋ 打开组件", () => manager.OpenFromCatalog(definition)); add.Margin = new Thickness(0, 3, 0, 0); if (opened.Length == 0) add.BorderBrush = Brush("#77AEC8"); content.Children.Add(add);
            flow.Children.Add(card);
        }
        summary.Text = $"{definitions.Count} 种组件，{instances.Count} 个实例正在运行   ·   当前显示 {flow.Children.Count} 种";
        if (flow.Children.Count == 0) flow.Children.Add(new Border { Padding = new Thickness(16), Child = Text("没有匹配的组件。试试其他关键词或切换筛选。", 13, Muted) });
        LayoutCards(); scroll.ScrollToVerticalOffset(offset);
    }
}
