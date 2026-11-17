using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DeskWidgets;

public sealed record WidgetDefinition(string Directory, string Name, double Width, double Height, double MinWidth, double MinHeight, bool DefaultOpen = false);
public sealed class WidgetState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WidgetName { get; set; } = "world-clock";
    public double Left { get; set; } = 64;
    public double Top { get; set; } = 64;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 190;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TimeZone { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WidgetCity? City { get; set; }
    public bool Locked { get; set; }
    public bool Topmost { get; set; }
    internal bool IsValid() => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(WidgetName) && new[] { Left, Top, Width, Height }.All(double.IsFinite) && Width is >= 1 and <= 32768 && Height is >= 1 and <= 32768 && (TimeZone == null || WidgetKinds.ValidZone(TimeZone)) && (City == null || City.IsValid());
}
public sealed class WidgetManager : IDisposable
{
    internal ClipboardService Clipboard { get; } = new();
    internal NotesService Notes { get; }
    internal MediaService Media { get; } = new();
    internal TelemetryService Telemetry { get; } = new(System.Windows.Application.Current.Dispatcher);
    public string WidgetsRoot { get; } = Path.Combine(AppContext.BaseDirectory, "widgets");
    private readonly string config = Path.Combine(AppContext.BaseDirectory, "config.json");
    private readonly List<WidgetWindow> windows = [];
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer desktopTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly FullscreenMonitor fullscreen;
    internal bool IsSleeping { get; private set; }
    private FileSystemWatcher? watcher;
    private bool disposing;
    private bool writableConfig = true;
    private readonly HashSet<string> changedWidgets = new(StringComparer.OrdinalIgnoreCase);
    private bool reloadShared;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public WidgetManager()
    {
        Notes = new NotesService(this);
        Directory.CreateDirectory(WidgetsRoot);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); Changed?.Invoke(); };
        reloadTimer.Tick += (_, _) =>
        {
            reloadTimer.Stop();
            if (!disposing) foreach (var window in windows.ToArray()) if (reloadShared || changedWidgets.Contains(window.State.WidgetName) || changedWidgets.Contains(WidgetKinds.ContentDirectory(window.State.WidgetName))) window.Reload();
            changedWidgets.Clear(); reloadShared = false; Changed?.Invoke();
        };
        watcher = new FileSystemWatcher(WidgetsRoot) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName };
        FileSystemEventHandler changed = (_, args) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (disposing) return;
            if (args.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(args.FullPath)) return;
            string relative = Path.GetRelativePath(WidgetsRoot, args.FullPath);
            string directory = relative.Split(Path.DirectorySeparatorChar)[0];
            if (directory == "_shared") reloadShared = true;
            else changedWidgets.Add(directory);
            reloadTimer.Stop(); reloadTimer.Start();
        });
        watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed; watcher.Renamed += (sender, e) => changed(sender, e);
        watcher.Error += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ReloadAll());
        watcher.EnableRaisingEvents = true;
        // Explorer can restart; refresh the owner without creating another shell process.
        desktopTimer.Tick += (_, _) => { if (IsSleeping) return; foreach (var window in windows.ToArray()) if (!window.IsPinnedTop) window.ApplyLayer(); Notes.RefreshLayer(); };
        desktopTimer.Start();
        fullscreen = new FullscreenMonitor(SetSleeping, !App.IsSelfTest);
    }
    public List<WidgetDefinition> Scan()
    {
        List<WidgetDefinition> definitions = [];
        foreach (var folder in Directory.EnumerateDirectories(WidgetsRoot))
        {
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(folder, "index.html"))) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "widget.json")));
                var root = json.RootElement;
                double Number(string name, double fallback) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) && double.IsFinite(n) && n is >= 1 and <= 32768 ? n : fallback;
                definitions.Add(new(Path.GetFileName(folder), root.GetProperty("name").GetString() ?? Path.GetFileName(folder), Number("defaultWidth", Number("width", 320)), Number("defaultHeight", Number("height", 190)), Number("minWidth", 240), Number("minHeight", 140), root.TryGetProperty("defaultOpen", out var open) && open.ValueKind == JsonValueKind.True));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { System.Diagnostics.Trace.WriteLine(error); }
        }
        return definitions;
    }
    public List<WidgetDefinition> Catalog() => Scan().GroupBy(d => WidgetKinds.Family(d.Directory)).Select(group => group.FirstOrDefault(d => d.Directory == group.Key) ?? group.First()).ToList();
    public void Restore()
    {
        Notes.Restore();
        var definitions = Scan();
        if (!File.Exists(config))
        {
            foreach (var definition in definitions.Where(d => d.DefaultOpen)) Open(definition);
            return;
        }
        try
        {
            var states = JsonSerializer.Deserialize<List<WidgetState>>(File.ReadAllText(config), Json) ?? throw new JsonException("配置必须是数组。");
            if (states.Any(s => s == null || !s.IsValid()) || states.Select(s => s.Id).Distinct().Count() != states.Count) throw new JsonException("挂件配置包含无效尺寸、位置或重复 ID。");
            foreach (var state in states)
            {
                var definition = definitions.Find(d => d.Directory == state.WidgetName);
                if (definition == null) throw new JsonException($"缺少挂件 {state.WidgetName}，保留原配置，请恢复该目录后重启。");
            }
            foreach (var state in states) Open(definitions.First(d => d.Directory == state.WidgetName), state);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { writableConfig = false; App.Report(new InvalidOperationException("无法恢复配置；本次运行不会覆盖原 config.json。" , error)); }
    }
    public void Open(WidgetDefinition definition, WidgetState? state = null)
    {
        if (disposing || App.IsExiting) return;
        state ??= new WidgetState { WidgetName = definition.Directory, Width = definition.Width, Height = definition.Height, Topmost = definition.Directory == "clipboard-cache", Left = 48 + windows.Count % 3 * 340, Top = 48 + windows.Count / 3 * 260 };
        var window = new WidgetWindow(this, state, definition);
        windows.Add(window); window.Show(); if (IsSleeping) window.SetSleeping(true); ScheduleSave();
    }
    public void OpenFromCatalog(WidgetDefinition definition)
    {
        if (WidgetKinds.Family(definition.Directory) is not ("world-clock" or "weather-card")) { Open(definition); return; }
        var dialog = new WidgetCreateDialog(definition);
        var owner = System.Windows.Application.Current.Windows.OfType<ComponentCenter>().FirstOrDefault(w => w.IsVisible);
        if (owner != null) dialog.Owner = owner; else dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        if (dialog.ShowDialog() == true && dialog.Selection is { } state) { state.Left = 48 + windows.Count % 3 * 340; state.Top = 48 + windows.Count / 3 * 260; Open(definition, state); }
    }
    public void Wake(WidgetDefinition definition)
    {
        var existing = windows.FirstOrDefault(w => WidgetKinds.Family(w.State.WidgetName) == WidgetKinds.Family(definition.Directory));
        if (existing == null) OpenFromCatalog(definition); else { if (!IsSleeping) existing.Show(); existing.WindowState = System.Windows.WindowState.Normal; }
    }
    public IReadOnlyList<WidgetWindow> OpenWindows => windows.ToArray();
    public event Action? Changed;
    internal void SetSleeping(bool sleeping)
    {
        if (disposing || IsSleeping == sleeping) return;
        IsSleeping = sleeping;
        foreach (var window in windows.ToArray()) window.SetSleeping(sleeping);
        Notes.SetSleeping(sleeping);
    }
    internal Task WaitForSleepAsync() => Task.WhenAll(windows.ToArray().Select(w => w.SleepTransition));
    public bool AllLocked => windows.Count > 0 && windows.All(w => w.State.Locked);
    public bool AllTopmost => windows.Count > 0 && windows.All(w => w.IsPinnedTop);
    public void LockAll(bool locked) { foreach (var w in windows) { w.State.Locked = locked; w.ApplyLock(); } ScheduleSave(); }
    public void PinAll(bool pinned) { foreach (var w in windows) w.IsPinnedTop = pinned; }
    public void ArrangeFromRight() { DesktopLayout.Arrange(windows.Cast<System.Windows.Window>().Concat(Notes.Windows).ToArray()); Notes.SavePositions(); ScheduleSave(); }
    public void ShrinkAll() { foreach (var window in windows.Cast<System.Windows.Window>().Concat(Notes.Windows)) { window.Width = window.MinWidth; window.Height = window.MinHeight; } Notes.SavePositions(); ScheduleSave(); }
    public void ReloadAll() { if (disposing) return; foreach (var window in windows.ToArray()) window.Reload(); }
    internal void Remove(WidgetWindow window) { Telemetry.Remove(window); Media.Remove(window); Notes.Remove(window); Clipboard.Remove(window); windows.Remove(window); if (!disposing) ScheduleSave(); }
    internal void ScheduleSave() { if (disposing) return; saveTimer.Stop(); saveTimer.Start(); }
    internal Task WaitForWindowsAsync() => Task.WhenAll(windows.ToArray().SelectMany(window => new[] { window.Initialization, window.Reloading, window.SleepTransition }));
    private void Save()
    {
        if (!writableConfig) return;
        try
        {
            string temporary = config + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(windows.Select(w => w.State), Json));
            File.Move(temporary, config, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { App.Report(error); }
    }
    public void Dispose()
    {
        if (disposing) return;
        disposing = true; fullscreen.Dispose(); watcher?.Dispose(); watcher = null;
        Telemetry.Dispose(); Media.Dispose(); Notes.Dispose(); Clipboard.Dispose();
        saveTimer.Stop(); reloadTimer.Stop(); desktopTimer.Stop(); Save();
        foreach (var window in windows.ToArray()) window.Close();
    }
}
