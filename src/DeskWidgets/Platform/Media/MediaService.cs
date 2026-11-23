using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DeskWidgets;
internal sealed class MediaService : IDisposable
{
    private readonly HashSet<WidgetWindow> clients = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private bool busy, commandBusy, disposed;
    internal MediaService() => timer.Tick += async (_, _) => await Update();
    internal void Subscribe(WidgetWindow window, bool enabled)
    {
        if (disposed) return;
        if (enabled) clients.Add(window); else clients.Remove(window);
        if (clients.Count == 0) { timer.Stop(); manager = null; } else { timer.Start(); _ = Update(); }
    }
    internal void Remove(WidgetWindow window) => Subscribe(window, false);
    private async Task Update()
    {
        if (disposed || busy || clients.Count == 0) return;
        busy = true;
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) { Publish(new { type = "mediaState", available = false, status = "没有公开的系统媒体会话", title = "", artist = "", playing = false }); return; }
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            Publish(new { type = "mediaState", available = true, title = properties.Title, artist = properties.Artist, source = session.SourceAppUserModelId, playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, status = "系统媒体会话 · 每秒更新" });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Publish(new { type = "mediaState", available = false, title = "", artist = "", playing = false, status = "媒体会话不可用：" + error.Message }); }
        finally { busy = false; }
    }
    internal async Task Command(WidgetWindow window, string command)
    {
        if (disposed || commandBusy || !clients.Contains(window) || window.State.Locked || command is not ("toggle" or "previous" or "next")) return;
        if (App.IsSelfTest) { window.PostTelemetry(new { type = "mediaCommandPreview", command }); return; }
        commandBusy = true;
        try
        {
            var session = manager?.GetCurrentSession();
            if (session == null) return;
            bool success = command switch { "toggle" => await session.TryTogglePlayPauseAsync(), "previous" => await session.TrySkipPreviousAsync(), _ => await session.TrySkipNextAsync() };
            if (!success) window.PostTelemetry(new { type = "mediaState", available = true, status = "当前播放器不支持此操作" });
            await Update();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { window.PostTelemetry(new { type = "mediaState", available = false, status = error.Message }); }
        finally { commandBusy = false; }
    }
    private void Publish(object value) { if (!disposed) foreach (var window in new List<WidgetWindow>(clients)) window.PostTelemetry(value); }
    public void Dispose() { disposed = true; timer.Stop(); clients.Clear(); manager = null; }
}
