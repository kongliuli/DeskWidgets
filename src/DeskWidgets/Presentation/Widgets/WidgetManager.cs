using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeskWidgets.Core;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DeskWidgets;

public sealed class WidgetManager : IDisposable
{
    internal ClipboardService Clipboard { get; } = new();
    internal NotesService Notes { get; }
    internal MediaService Media { get; } = new();
    internal TelemetryService Telemetry { get; } = new(System.Windows.Application.Current.Dispatcher);
    public string WidgetsRoot { get; }
    private readonly IWidgetLayoutRepository layoutRepository;
    private readonly IWidgetCatalog widgetCatalog;
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
    internal WidgetManager(string widgetsRoot, IWidgetCatalog widgetCatalog, IWidgetLayoutRepository layoutRepository, INoteHistoryRepository noteHistoryRepository)
    {
        WidgetsRoot = widgetsRoot;
        this.widgetCatalog = widgetCatalog;
        this.layoutRepository = layoutRepository;
        Notes = new NotesService(this, noteHistoryRepository);
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
    public List<WidgetDefinition> Scan() => widgetCatalog.Scan();
    public List<WidgetDefinition> Catalog() => Scan().GroupBy(d => WidgetKinds.Family(d.Directory)).Select(group => group.FirstOrDefault(d => d.Directory == group.Key) ?? group.First()).ToList();
    public void Restore()
    {
        Notes.Restore();
        var definitions = Scan();
        try
        {
            var states = layoutRepository.Load();
            if (states == null)
            {
                foreach (var definition in definitions.Where(d => d.DefaultOpen)) Open(definition);
                return;
            }
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
            layoutRepository.Save(windows.Select(w => w.State));
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
