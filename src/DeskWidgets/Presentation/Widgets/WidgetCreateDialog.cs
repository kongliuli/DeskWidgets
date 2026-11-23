using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskWidgets;
internal sealed class WidgetCreateDialog : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly ComboBox choices = new() { MinHeight = 32, Foreground = Brushes.Black, Margin = new Thickness(0, 8, 0, 16) };
    internal WidgetState? Selection { get; private set; }
    internal WidgetCreateDialog(WidgetDefinition definition)
    {
        string family = WidgetKinds.Family(definition.Directory); bool clock = family == "world-clock";
        Title = clock ? "新建世界时钟" : "新建天气"; Width = 430; Height = clock ? 230 : 340; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(25, 39, 56)); Foreground = Brushes.WhiteSmoke; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        NameScope.SetNameScope(this, new NameScope()); RegisterName("RegionChoice", choices);
        var root = new StackPanel { Margin = new Thickness(24) }; Content = root;
        root.Children.Add(new TextBlock { Text = clock ? "选择这张时钟的地区" : "选择这张天气卡的城市", FontSize = 20 });
        root.Children.Add(choices);
        if (clock) foreach (var zone in WidgetZones.Entries) choices.Items.Add(new ComboBoxItem { Content = zone.Name, Tag = zone.Zone });
        else
        {
            foreach (var city in new[] { new WidgetCity("南京",32.0603,118.7969), new("北京",39.9042,116.4074), new("上海",31.2304,121.4737), new("广州",23.1291,113.2644), new("深圳",22.5431,114.0579), new("纽约",40.7128,-74.006), new("洛杉矶",34.0522,-118.2437), new("伦敦",51.5074,-.1278), new("东京",35.6762,139.6503) }) choices.Items.Add(new ComboBoxItem { Content = city.Name, Tag = city });
            var row = new DockPanel(); var searchButton = new Button { Content = "搜索城市", Padding = new Thickness(8), Foreground = Brushes.Black }; DockPanel.SetDock(searchButton, Dock.Right); row.Children.Add(searchButton);
            var query = new TextBox { Padding = new Thickness(7), Foreground = Brushes.Black, ToolTip = "输入中文或英文城市名", Margin = new Thickness(0, 0, 7, 0) }; row.Children.Add(query); root.Children.Add(row);
            var status = new TextBlock { Text = "可直接选择常用城市，或搜索其他地区。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12), Foreground = Brushes.LightSteelBlue }; root.Children.Add(status);
            CancellationTokenSource? searching = null; Closed += (_, _) => searching?.Cancel();
            async void Search()
            {
                string name = query.Text.Trim(); if (name.Length is < 1 or > 100) { status.Text = "请输入 1–100 字的城市名。"; return; }
                searching?.Cancel(); var request = new CancellationTokenSource(); searching = request; searchButton.IsEnabled = false; status.Text = "正在搜索…";
                try
                {
                    string json = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) + "&count=5&language=zh&format=json", request.Token);
                    using var document = JsonDocument.Parse(json);
                    var results = ParseCities(document.RootElement);
                    if (searching != request) return;
                    if (results.Length == 0) { status.Text = "没有找到城市，可尝试英文名。"; return; }
                    choices.Items.Clear(); foreach (var result in results) choices.Items.Add(new ComboBoxItem { Content = result.Label, Tag = result.City }); choices.SelectedIndex = 0; choices.IsDropDownOpen = true; status.Text = "请选择搜索结果，再创建实例。";
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) { if (searching == request) status.Text = "搜索暂不可用，仍可选择已有城市。"; }
                finally { if (searching == request) { searching = null; searchButton.IsEnabled = true; } request.Dispose(); }
            }
            searchButton.Click += (_, _) => Search(); query.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Search(); } };
        }
        choices.SelectedIndex = 0;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.Black };
        var create = new Button { Content = "创建实例", Padding = new Thickness(16, 7, 16, 7), Foreground = Brushes.Black }; RegisterName("CreateInstance", create);
        create.Click += (_, _) =>
        {
            if (choices.SelectedItem is not ComboBoxItem selected) return;
            Selection = new WidgetState { WidgetName = definition.Directory, Width = definition.Width, Height = definition.Height, TimeZone = clock ? selected.Tag as string : null, City = !clock ? selected.Tag as WidgetCity : null };
            DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel); actions.Children.Add(create); root.Children.Add(actions);
    }
    internal static (WidgetCity City, string Label)[] ParseCities(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        return results.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Take(5).Select(value =>
        {
            string Text(string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
            var city = new WidgetCity(Text("name"), value.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number && lat.TryGetDouble(out double y) ? y : double.NaN, value.TryGetProperty("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number && lon.TryGetDouble(out double x) ? x : double.NaN);
            return (City: city, Label: string.Join(" · ", new[] { city.Name, Text("admin1"), Text("country") }.Where(s => !string.IsNullOrWhiteSpace(s))));
        }).Where(result => result.City.IsValid()).ToArray();
    }
}
