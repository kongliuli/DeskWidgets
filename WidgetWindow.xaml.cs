using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;

namespace DeskWidgets;

public partial class WidgetWindow : Window
{
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);
    // shortcut: this SDK keeps its capture divider internal; remove this lookup when the SDK publishes the setter.
    internal static readonly DependencyProperty? CaptureDividerProperty = typeof(Microsoft.Web.WebView2.Wpf.WebView2CompositionControl)
        .GetField("FpsDividerProperty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(null) as DependencyProperty;
    private readonly WidgetManager manager;
    internal WidgetState State { get; }
    private nint handle;
    private HwndSource? source;
    private bool disposed;
    private bool interactive;
    internal bool IsSleeping { get; private set; }
    private bool changingSleep, reloadAfterWake;
    internal Task SleepTransition { get; private set; } = Task.CompletedTask;
    private bool closeRequested;
    private bool reloading;
    private Win32Interop.Point? pointerDown;
    private long pointerDownAt;
    private OutlineDragSession? dragSession;
    private bool dragRequested;
    private int inputTraceCount;
    internal Task Initialization { get; private set; } = Task.CompletedTask;
    internal Task Reloading { get; private set; } = Task.CompletedTask;
    private readonly string virtualHost;
    public bool IsPinnedTop { get => State.Topmost; set { State.Topmost = value; ApplyLayer(); PublishState(); manager.ScheduleSave(); } }
    internal WidgetWindow(WidgetManager manager, WidgetState state, WidgetDefinition definition)
    {
        InitializeComponent();
        if (CaptureDividerProperty != null) Browser.SetValue(CaptureDividerProperty, 3);
        this.manager = manager; State = state;
        virtualHost = $"w-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(state.Id))).ToLowerInvariant()[..32]}.widgets.local";
        Title = definition.Name;
        MinWidth = definition.MinWidth; MinHeight = definition.MinHeight;
        Width = Math.Max(MinWidth, state.Width); Height = Math.Max(MinHeight, state.Height);
        Left = state.Left; Top = state.Top;
        // All persisted coordinates are WPF DIPs; native screen pixels never enter the config.
        LocationChanged += (_, _) => Capture(); SizeChanged += (_, _) => Capture();
        PreviewMouseLeftButtonDown += (_, _) => { if (Win32Interop.GetCursorPos(out var cursor)) { pointerDown = cursor; pointerDownAt = Environment.TickCount64; } };
        DragHandle.MouseLeftButtonDown += (_, args) => { if (State.Locked) return; dragRequested = true; StartOutlineDrag(); args.Handled = true; };
        PreviewMouseMove += (_, _) => { if (dragSession != null && Win32Interop.GetCursorPos(out var cursor)) dragSession.Update(cursor, true); };
        PreviewMouseLeftButtonUp += (_, _) => FinishOutlineDrag(false);
        LostMouseCapture += (_, _) => { if (dragSession != null && Mouse.Captured != this && Mouse.Captured != Browser) FinishOutlineDrag(true); };
        Loaded += (_, _) => Initialization = InitializeBrowser();
        Closing += async (_, args) =>
        {
            if (Initialization.IsCompleted && Reloading.IsCompleted && SleepTransition.IsCompleted) { PrepareClose(); return; }
            args.Cancel = true;
            if (closeRequested) return;
            closeRequested = true;
            await Task.WhenAll(Initialization, Reloading, SleepTransition);
            Close();
        };
        Closed += (_, _) => manager.Remove(this);
        ApplyLock();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        handle = new WindowInteropHelper(this).Handle;
        Win32Interop.SetLong(handle, Win32Interop.ExStyle, Win32Interop.GetLong(handle, Win32Interop.ExStyle) | 0x08000080);
        source = HwndSource.FromHwnd(handle); source?.AddHook(WndProc);
        Win32Interop.EnsureOnScreen(handle);
        ApplyLayer();
    }
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0xA1 && !State.Locked && (long)wParam is >= 10 and <= 17 && Win32Interop.GetCursorPos(out var resizeOrigin)) { BeginOutlineResize(resizeOrigin, (int)wParam); handled = true; return 0; }
        if (msg == 0x21 && !interactive) { handled = true; return 3; } // MA_NOACTIVATE
        if (msg == 0x112 && ((long)wParam & 0xfff0) == 0xf020) handled = true;
        if (msg == 0x46 && !IsPinnedTop && !IsSleeping)
        {
            var pos = Marshal.PtrToStructure<Win32Interop.WindowPos>(lParam);
            // Preserve NOZORDER: forcing every notification to bottom makes sibling widgets reorder each other.
            if ((pos.Flags & 0x80) != 0) { pos.Flags &= ~0x80u; Marshal.StructureToPtr(pos, lParam, false); }
        }
        return 0;
    }
    internal void ApplyLayer()
    {
        if (handle == 0 || disposed) return;
        var owner = IsPinnedTop ? 0 : Win32Interop.DesktopOwner();
        if (Win32Interop.GetLong(handle, Win32Interop.Owner) == owner && ((Win32Interop.GetLong(handle, Win32Interop.ExStyle) & 8) != 0) == IsPinnedTop) return;
        // Use a desktop owner rather than SetParent: retain top-level WPF/DPI and native move semantics.
        Win32Interop.SetLong(handle, Win32Interop.Owner, owner);
        Topmost = IsPinnedTop;
        Win32Interop.SetWindowPos(handle, IsPinnedTop ? Win32Interop.Topmost : Win32Interop.NotTopmost, 0, 0, 0, 0, 0x13);
        if (!IsPinnedTop) Win32Interop.SetWindowPos(handle, Win32Interop.Bottom, 0, 0, 0, 0, 0x13);
        manager.Notes.RefreshLayer(true);
    }
    private async Task InitializeBrowser()
    {
        Capture();
        try
        {
            Browser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            var userData = App.IsSelfTest ? Path.Combine(AppContext.BaseDirectory, "test-profiles") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            if (disposed) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = virtualHost.Split('.')[0];
            await Browser.EnsureCoreWebView2Async(environment, options);
            if (disposed) return;
            var core = Browser.CoreWebView2;
            string contentDirectory = WidgetKinds.ContentDirectory(State.WidgetName);
            if (!Directory.Exists(Path.Combine(manager.WidgetsRoot, contentDirectory))) contentDirectory = State.WidgetName;
            core.SetVirtualHostNameToFolderMapping(virtualHost, Path.Combine(manager.WidgetsRoot, contentDirectory), CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping("assets.widgets.local", Path.Combine(manager.WidgetsRoot, "_shared"), CoreWebView2HostResourceAccessKind.Allow);
            if (State.WidgetName == "clipboard-cache")
            {
                core.AddWebResourceRequestedFilter("https://clipboard.images.local/__clipboard/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.Host != "clipboard.images.local") return;
                    var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    bool valid = args.Request.Method == "GET" && parts.Length == 3 && parts[0] == "__clipboard" && Guid.TryParseExact(parts[1], "N", out Guid _) && parts[2] is "thumb.png" or "original.png";
                    if (valid && manager.Clipboard.TryGetImage(parts[1], parts[2] == "original.png", out var bytes)) args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK", "Content-Type: image/png\r\nCache-Control: no-store");
                    else args.Response = environment.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", "Cache-Control: no-store");
                };
            }
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, args) => { if (!IsLocal(args.Uri)) args.Cancel = true; else if (interactive) SetInteractive(false); };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += MessageReceived;
            core.NavigationCompleted += (_, _) => { PublishState(); if (IsSleeping) RequestSleepSync(); };
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            if (disposed) return;
            var seeds = new System.Collections.Generic.Dictionary<string, object>();
            if (WidgetKinds.Family(State.WidgetName) == "world-clock") seeds["settings"] = new { timeZone = State.TimeZone ?? WidgetKinds.DefaultZone(State.WidgetName) };
            if (WidgetKinds.Family(State.WidgetName) == "weather-card" && State.City is { } city && city.IsValid())
            {
                if (State.WidgetName == "weather") seeds["location"] = new { name = city.Name, latitude = city.Lat, longitude = city.Lon };
                else seeds["weather-card.city"] = city;
            }
            if (seeds.Count > 0) await core.AddScriptToExecuteOnDocumentCreatedAsync("(()=>{try{const seeds=" + JsonSerializer.Serialize(seeds, MessageJson) + ";for(const [key,value] of Object.entries(seeds))if(localStorage.getItem(key)===null)localStorage.setItem(key,JSON.stringify(value));}catch{}})()");
            if (!disposed) core.Navigate($"https://{virtualHost}/index.html");
        }
        catch (Exception error) { if (!disposed) App.Report(error); }
    }
    private bool IsLocal(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == virtualHost;
    private void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (disposed || !IsLocal(e.Source)) return;
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            if (!json.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            if (IsSleeping && type.GetString() != "widgetContext") return;
            switch (type.GetString())
            {
                case "widgetContext":
                    if (WidgetKinds.Family(State.WidgetName) == "world-clock" && json.RootElement.TryGetProperty("timeZone", out var selectedZone) && selectedZone.ValueKind == JsonValueKind.String && WidgetKinds.ValidZone(selectedZone.GetString()) && State.TimeZone != selectedZone.GetString())
                    { State.TimeZone = selectedZone.GetString(); Title = "世界时钟 · " + WidgetKinds.Context(State); manager.ScheduleSave(); }
                    if (WidgetKinds.Family(State.WidgetName) == "weather-card" && json.RootElement.TryGetProperty("city", out var selectedCity) && selectedCity.ValueKind == JsonValueKind.Object)
                    { var city = JsonSerializer.Deserialize<WidgetCity>(selectedCity.GetRawText(), MessageJson); if (city != null && city.IsValid() && State.City != city) { State.City = city; Title = "天气 · " + city.Name; manager.ScheduleSave(); } }
                    break;
                case "subscribeClipboard" when State.WidgetName == "clipboard-cache":
                    manager.Clipboard.Subscribe(this, json.RootElement.TryGetProperty("enabled", out var clipboardEnabled) && clipboardEnabled.ValueKind == JsonValueKind.True); break;
                case "requestClipboard" when State.WidgetName == "clipboard-cache": manager.Clipboard.Request(this); break;
                case "copyClipboardEntry" when !State.Locked && State.WidgetName == "clipboard-cache":
                    if (json.RootElement.TryGetProperty("id", out var clipboardId) && clipboardId.ValueKind == JsonValueKind.String) manager.Clipboard.Copy(this, clipboardId.GetString() ?? ""); break;
                case "clearClipboardCache" when !State.Locked && State.WidgetName == "clipboard-cache": manager.Clipboard.Clear(this); break;
                case "requestNotes" or "saveNoteHistory" or "readNoteHistory" or "startNoteDrag" or "endNoteDrag" or "cancelNoteDrag" or "closeDesktopNote" or "deleteNoteHistory" when State.WidgetName == "notes":
                    manager.Notes.Handle(this, json.RootElement); break;
                case "subscribeMedia" when State.WidgetName == "media-player":
                    manager.Media.Subscribe(this, json.RootElement.TryGetProperty("enabled", out var mediaEnabled) && mediaEnabled.ValueKind == JsonValueKind.True); break;
                case "mediaCommand" when !State.Locked && State.WidgetName == "media-player":
                    if (json.RootElement.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String) _ = manager.Media.Command(this, command.GetString() ?? ""); break;
                case "openWorkspaceTarget" when !State.Locked && State.WidgetName == "workspace-launcher":
                    if (json.RootElement.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String) WorkspaceLauncher.Open(this, target.GetString() ?? ""); break;
                case "subscribeSystemMetrics" when State.WidgetName == "system-monitor":
                    MetricChannels channels = MetricChannels.None;
                    if (json.RootElement.TryGetProperty("channels", out var list) && list.ValueKind == JsonValueKind.Array)
                        foreach (var channel in list.EnumerateArray()) if (channel.ValueKind == JsonValueKind.String)
                            channels |= channel.GetString() switch { "cpu" => MetricChannels.Cpu, "gpu" => MetricChannels.Gpu, "memory" => MetricChannels.Memory, _ => MetricChannels.None };
                    manager.Telemetry.SubscribeMetrics(this, channels);
                    break;
                case "subscribeAudio" when State.WidgetName == "audio-control":
                    bool audioEnabled = json.RootElement.TryGetProperty("enabled", out var audioFlag) && audioFlag.ValueKind == JsonValueKind.True;
                    bool meterEnabled = json.RootElement.TryGetProperty("meter", out var meterFlag) && meterFlag.ValueKind == JsonValueKind.True;
                    manager.Telemetry.SubscribeAudio(this, audioEnabled, meterEnabled);
                    break;
                case "setAudioVolume" when !State.Locked && State.WidgetName == "audio-control":
                    if (json.RootElement.TryGetProperty("value", out var volumeValue) && volumeValue.ValueKind == JsonValueKind.Number && volumeValue.TryGetDouble(out var volume)) manager.Telemetry.SetVolume(this, volume);
                    break;
                case "setAudioMute" when !State.Locked && State.WidgetName == "audio-control":
                    if (json.RootElement.TryGetProperty("muted", out var muteValue) && muteValue.ValueKind is JsonValueKind.True or JsonValueKind.False) manager.Telemetry.SetMute(this, muteValue.GetBoolean());
                    break;
                case "startDrag" when !State.Locked:
                    TraceInput("web-start");
                    dragRequested = true;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, (Action)StartOutlineDrag);
                    break;
                case "dragMove" when !State.Locked:
                    if (dragSession != null && Win32Interop.GetCursorPos(out var point)) dragSession.Update(point, true);
                    break;
                case "endDrag": TraceInput("web-up"); FinishOutlineDrag(false); break;
                case "cancelDrag": TraceInput("web-cancel"); FinishOutlineDrag(true); break;
                case "closeWidget" when !State.Locked: Dispatcher.BeginInvoke(Close); break;
                case "toggleLock": State.Locked = !State.Locked; ApplyLock(); manager.ScheduleSave(); break;
                case "toggleTopmost" when !State.Locked: IsPinnedTop = !IsPinnedTop; break;
                case "setInteractive" when !State.Locked && json.RootElement.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    SetInteractive(enabled.GetBoolean());
                    break;
            }
        }
        catch (JsonException) { }
    }
    private void PrepareClose()
    {
        if (disposed) return;
        disposed = true;
        manager.Telemetry.Remove(this); manager.Media.Remove(this); manager.Notes.Remove(this); manager.Clipboard.Remove(this);
        dragSession?.Dispose();
        source?.RemoveHook(WndProc);
        Browser.Dispose();
        if (Browser.Parent is System.Windows.Controls.Panel panel) panel.Children.Remove(Browser);
        UnregisterName(nameof(Browser));
        Browser = null!;
        Initialization = Task.CompletedTask;
    }
    internal void PostTelemetry(object value)
    {
        if (!disposed && !IsSleeping && Browser.CoreWebView2 != null) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, MessageJson));
    }
    internal void SetSleeping(bool sleeping)
    {
        if (disposed || IsSleeping == sleeping) return;
        IsSleeping = sleeping;
        if (sleeping)
        {
            FinishOutlineDrag(true); manager.Notes.Remove(this); manager.Telemetry.Remove(this); manager.Media.Remove(this); manager.Clipboard.Remove(this);
            Browser.Visibility = Visibility.Hidden; Hide();
        }
        else
        {
            Browser.CoreWebView2?.Resume(); Browser.Visibility = Visibility.Visible; Show();
            Win32Interop.EnsureOnScreen(handle);
            Win32Interop.SetWindowPos(handle, IsPinnedTop ? Win32Interop.Topmost : Win32Interop.Bottom, 0, 0, 0, 0, 0x13);
        }
        RequestSleepSync();
        if (!sleeping && reloadAfterWake) { reloadAfterWake = false; Reload(); }
    }
    private void RequestSleepSync() { if (!changingSleep) SleepTransition = SyncSleepAsync(); }
    private async Task SyncSleepAsync()
    {
        if (changingSleep || disposed || Browser.CoreWebView2 == null) return;
        changingSleep = true;
        try
        {
            while (!disposed)
            {
                await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Background);
                if (disposed) return;
                bool wanted = IsSleeping; var core = Browser.CoreWebView2;
                try
                {
                    if (wanted)
                    {
                        await core.ExecuteScriptAsync("document.querySelectorAll('video,audio').forEach(v=>{v.__deskWasPlaying=!v.paused;v.pause()})");
                        if (wanted != IsSleeping) continue;
                        await core.TrySuspendAsync();
                    }
                    else
                    {
                        core.Resume();
                        await core.ExecuteScriptAsync("document.querySelectorAll('video,audio').forEach(v=>{if(v.__deskWasPlaying){v.play().catch(()=>{});delete v.__deskWasPlaying;}});document.dispatchEvent(new Event('visibilitychange'))");
                        PublishState();
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (wanted != IsSleeping) continue; System.Diagnostics.Trace.WriteLine(error); }
                if (wanted == IsSleeping) break;
            }
        }
        finally { changingSleep = false; }
    }
    private void ResizePointerDown(object sender, MouseButtonEventArgs args)
    {
        if (State.Locked || disposed || sender is not FrameworkElement element || !int.TryParse(element.Tag?.ToString(), out int edge) || !Win32Interop.GetCursorPos(out var cursor)) return;
        BeginOutlineResize(cursor, edge); args.Handled = true;
    }
    private void BeginOutlineResize(Win32Interop.Point origin, int edge)
    {
        dragSession?.Dispose(); dragRequested = false;
        dragSession = new OutlineDragSession(this, origin, () => { dragSession = null; if (Mouse.Captured == this || Mouse.Captured == Browser) Mouse.Capture(null); }, edge);
        if (!Mouse.Capture(Browser, CaptureMode.Element)) { dragSession.Dispose(); dragSession = null; }
    }
    private void StartOutlineDrag()
    {
        if (disposed || State.Locked || !dragRequested || dragSession != null || !Win32Interop.GetCursorPos(out var cursor)) return;
        var origin = pointerDown.HasValue && Environment.TickCount64 - pointerDownAt < 500 ? pointerDown.Value : cursor;
        dragSession = new OutlineDragSession(this, origin, () => { dragSession = null; dragRequested = false; if (Mouse.Captured == this || Mouse.Captured == Browser) Mouse.Capture(null); });
        bool captured = Mouse.Capture(Browser, CaptureMode.Element);
        TraceInput(captured ? "capture-ok" : "capture-failed");
        pointerDown = null;
    }
    private void FinishOutlineDrag(bool cancel)
    {
        dragRequested = false;
        if (dragSession == null) return;
        TraceInput(cancel ? "finish-cancel" : "finish-drop");
        if (!cancel && Win32Interop.GetCursorPos(out var cursor)) dragSession.Update(cursor, false);
        else dragSession.Dispose();
    }
    private void TraceInput(string kind)
    {
        if (App.IsSelfTest || ++inputTraceCount > 15) return;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "input-trace.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length > 65536) return;
            var data = new { time = DateTimeOffset.Now, widget = State.WidgetName, kind, pinned = IsPinnedTop,
                physical = Win32Interop.GetAsyncKeyState(System.Windows.Forms.SystemInformation.MouseButtonsSwapped ? 2 : 1),
                logical = Mouse.LeftButton.ToString(), captured = Mouse.Captured?.GetType().Name };
            File.AppendAllText(path, JsonSerializer.Serialize(data) + "\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void SetInteractive(bool enabled)
    {
        interactive = enabled;
        if (handle == 0 || disposed) return;
        var style = Win32Interop.GetLong(handle, Win32Interop.ExStyle);
        Win32Interop.SetLong(handle, Win32Interop.ExStyle, interactive ? style & ~0x08000000 : style | 0x08000000);
        if (interactive) { Win32Interop.SetForegroundWindow(handle); Browser.Focus(); }
        else if (!IsPinnedTop) Win32Interop.SetWindowPos(handle, Win32Interop.Bottom, 0, 0, 0, 0, 0x13);
        manager.Notes.RefreshLayer(true);
    }
    internal void ApplyLock()
    {
        if (State.Locked) dragSession?.Dispose();
        if (State.Locked) { dragRequested = false; manager.Notes.Remove(this); SetInteractive(false); }
        DragHandle.IsEnabled = !State.Locked;
        DragHandle.Visibility = State.Locked ? Visibility.Collapsed : Visibility.Visible;
        DragHandle.Opacity = State.Locked ? 0.2 : 1;
        ResizeMode = State.Locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(0);
        ResizeHandles.Visibility = State.Locked ? Visibility.Collapsed : Visibility.Visible;
        PublishState();
    }
    private void PublishState()
    {
        if (!disposed && !IsSleeping) Browser.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "hostState", locked = State.Locked, topmost = State.Topmost }));
    }
    internal void Reload()
    {
        if (disposed || closeRequested || App.IsExiting || reloading || Browser.CoreWebView2 == null) return;
        if (IsSleeping) { reloadAfterWake = true; return; }
        Reloading = ReloadCore();
    }
    private async Task ReloadCore()
    {
        reloading = true;
        try
        {
            var core = Browser.CoreWebView2;
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            if (!disposed) core.Reload();
        }
        catch (Exception error) { if (!disposed) App.Report(error); }
        finally { reloading = false; }
    }
    private void Capture()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        State.Left = Left; State.Top = Top; State.Width = ActualWidth; State.Height = ActualHeight;
        manager.ScheduleSave();
    }
}
