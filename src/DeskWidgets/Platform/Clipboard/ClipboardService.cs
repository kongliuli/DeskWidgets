using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeskWidgets;
internal sealed record ClipboardEntry(string Id, string Text, DateTimeOffset CreatedAt, string Kind = "text", int Width = 0, int Height = 0, string? Thumbnail = null, [property: JsonIgnore] ClipboardImage? Image = null);
internal sealed class ClipboardService : IDisposable
{
    private readonly Dispatcher ui = System.Windows.Application.Current.Dispatcher;
    private readonly HashSet<WidgetWindow> clients = [];
    private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ClipboardEntry> entries = [];
    private readonly ConcurrentDictionary<string, ClipboardImage> images = new();
    private Thread? thread;
    private HwndSource? source;
    private DispatcherTimer? retry;
    private volatile bool listening;
    internal int SubscriberCount => clients.Count;
    internal bool NativeListening => listening;
    private volatile bool disposed, collectRequested;
    private int attempts;
    private uint ownSequence, readSequence;
    private string status = "文本与图片 · 本次运行内保留";
    private const int MaximumBytes = 1024 * 1024, MaximumTotalBytes = 64 * 1024 * 1024;
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetBitmapObject(nint bitmap, int size, out NativeBitmap info);
    internal void Subscribe(WidgetWindow window, bool enabled)
    {
        if (disposed) return;
        if (enabled) clients.Add(window); else clients.Remove(window);
        collectRequested = clients.Count > 0;
        Queue(() => { SetListening(collectRequested); SendState(window); });
    }
    internal void Remove(WidgetWindow window) { if (!clients.Remove(window) || disposed) return; collectRequested = clients.Count > 0; Queue(() => SetListening(collectRequested)); }
    internal void Request(WidgetWindow window) => Queue(() => SendState(window));
    internal void Clear(WidgetWindow window) => Queue(() => { entries.Clear(); images.Clear(); Broadcast(); SendState(window); });
    internal void Copy(WidgetWindow window, string id) => Queue(() =>
    {
        var entry = entries.Find(item => item.Id == id);
        if (entry == null) { Post(window, new { type = "clipboardResult", success = false, message = "记录已过期，请刷新列表。" }); return; }
        try { if (!App.IsSelfTest) { if (entry.Image is { } image) { System.Windows.Clipboard.SetDataObject(ClipboardImages.CreateDataObject(image), true); }
                else System.Windows.Clipboard.SetText(entry.Text, System.Windows.TextDataFormat.UnicodeText); ownSequence = GetClipboardSequenceNumber(); } }
        catch (ExternalException) { Post(window, new { type = "clipboardResult", success = false, message = "剪贴板被其他程序占用，请稍后重试" }); return; }
        Post(window, new { type = "clipboardResult", success = true, message = App.IsSelfTest ? "已验证完整复制（测试未写系统剪贴板）" : "已复制，可在目标应用手动粘贴", length = entry.Text.Length, kind = entry.Kind, width = entry.Width, height = entry.Height });
    });
    internal Task InjectForTest(string text)
    {
        if (!App.IsSelfTest) throw new InvalidOperationException("Only isolated tests may inject clipboard entries");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(() => { CaptureText(text); completion.SetResult(); }); return completion.Task;
    }
    internal Task InjectImageForTest(byte[] data)
    {
        if (!App.IsSelfTest) throw new InvalidOperationException("Only isolated tests may inject clipboard images");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(() => { try { CaptureImage(ClipboardImages.FromPng(data)); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }); return completion.Task;
    }
    internal bool TryGetImage(string id, bool original, out byte[] bytes)
    {
        if (images.TryGetValue(id, out var image)) { bytes = original ? image.Original : image.Thumbnail; return true; }
        bytes = []; return false;
    }
    private static bool BitmapIsSafe()
    {
        uint format = IsClipboardFormatAvailable(17) ? 17u : 8u;
        if (IsClipboardFormatAvailable(format))
        {
            nint memory = GetClipboardData(format); nuint bytes = GlobalSize(memory);
            if (memory == 0 || bytes < 12 || bytes > 68 * 1024 * 1024) return false;
            nint pointer = GlobalLock(memory); if (pointer == 0) return false;
            try { int header = Marshal.ReadInt32(pointer); return header == 12 ? ClipboardImages.ValidDimensions((ushort)Marshal.ReadInt16(pointer, 4), (ushort)Marshal.ReadInt16(pointer, 6)) : header >= 40 && (nuint)header <= bytes && ClipboardImages.ValidDimensions(Marshal.ReadInt32(pointer, 4), Math.Abs((long)Marshal.ReadInt32(pointer, 8))); }
            finally { GlobalUnlock(memory); }
        }
        return IsClipboardFormatAvailable(2) && GetBitmapObject(GetClipboardData(2), Marshal.SizeOf<NativeBitmap>(), out var bitmap) != 0 && ClipboardImages.ValidDimensions(bitmap.Width, bitmap.Height);
    }
    private static byte[]? ReadPng()
    {
        uint format = RegisterClipboardFormat("PNG"); if (!IsClipboardFormatAvailable(format)) return null;
        nint memory = GetClipboardData(format); nuint size = GlobalSize(memory);
        if (memory == 0 || size == 0 || size > ClipboardImages.MaximumEncodedBytes) throw new ArgumentException("PNG 超过 16 MiB 或格式无效。");
        nint pointer = GlobalLock(memory); if (pointer == 0) return null;
        try { byte[] data = new byte[(int)size]; Marshal.Copy(pointer, data, 0, data.Length); return data; } finally { GlobalUnlock(memory); }
    }
    private void Queue(Action action)
    {
        if (disposed) return;
        if (thread == null)
        {
            thread = new Thread(() =>
            {
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    source = new HwndSource(new HwndSourceParameters("DeskWidgets Clipboard") { ParentWindow = -3, WindowStyle = 0, Width = 0, Height = 0 });
                    source.AddHook(Hook);
                    retry = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
                    retry.Tick += (_, _) => CaptureCurrent();
                    ready.SetResult(dispatcher); if (disposed) dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); Dispatcher.Run();
                }
                catch (Exception error) when (error is not OutOfMemoryException) { ready.TrySetException(error); }
                finally { retry?.Stop(); if (source != null) { if (listening) RemoveClipboardFormatListener(source.Handle); source.Dispose(); } }
            }) { IsBackground = true, Name = "DeskWidgets Clipboard STA" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        _ = Dispatch();
        async Task Dispatch()
        {
            try
            {
                var dispatcher = await ready.Task.ConfigureAwait(false);
                if (!disposed) _ = dispatcher.BeginInvoke(() =>
                {
                    if (disposed) return;
                    try { action(); }
                    catch (Exception error) when (error is not OutOfMemoryException) { status = "剪贴板暂不可用：" + error.Message; Broadcast(); }
                });
            }
            catch (Exception error) when (error is not OutOfMemoryException) { _ = ui.BeginInvoke(() => { if (!disposed) foreach (var window in clients.ToArray()) window.PostTelemetry(new { type = "clipboardError", message = error.Message }); }); }
        }
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x31D && listening) { attempts = 0; CaptureCurrent(); }
        return 0;
    }
    private void SetListening(bool enabled)
    {
        if (enabled == listening || source == null) return;
        if (enabled)
        {
            if (!AddClipboardFormatListener(source.Handle)) throw new InvalidOperationException("无法注册剪贴板变更通知。");
            listening = true; attempts = 0; CaptureCurrent();
        }
        else { RemoveClipboardFormatListener(source.Handle); listening = false; retry?.Stop(); }
    }
    private void CaptureCurrent()
    {
        try { CaptureCurrentCore(); }
        catch (Exception error) when (error is not OutOfMemoryException) { status = "剪贴板内容未缓存：" + error.Message; Broadcast(); }
    }
    private void CaptureCurrentCore()
    {
        retry?.Stop();
        if (App.IsSelfTest || disposed || !collectRequested || !listening || source == null) return;
        uint sequence = GetClipboardSequenceNumber();
        if (sequence == ownSequence || sequence == readSequence) return;
        if (!OpenClipboard(source.Handle))
        {
            if (++attempts <= 5) retry?.Start(); else { status = "剪贴板正被其他应用占用，等待下一次复制"; Broadcast(); }
            return;
        }
        byte[]? png = null; bool bitmapAvailable = false;
        try
        {
            readSequence = sequence;
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) { status = "来源要求不缓存此内容"; Broadcast(); return; }
            uint historyFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (IsClipboardFormatAvailable(historyFormat))
            {
                nint privacy = GetClipboardData(historyFormat);
                if (privacy != 0 && GlobalSize(privacy) >= 4)
                {
                    nint flag = GlobalLock(privacy);
                    if (flag != 0) { bool excluded; try { excluded = Marshal.ReadInt32(flag) == 0; } finally { GlobalUnlock(privacy); } if (excluded) { status = "来源要求不加入剪贴板历史"; Broadcast(); return; } }
                }
            }
            png = ReadPng();
            if (png == null && (IsClipboardFormatAvailable(2) || IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17)))
            {
                if (!BitmapIsSafe()) throw new ArgumentException("位图超过尺寸/容量上限或格式无效。");
                bitmapAvailable = true;
            }
            if (png == null && !bitmapAvailable)
            {
            if (!IsClipboardFormatAvailable(13)) return;
            nint memory = GetClipboardData(13); if (memory == 0) return;
            nuint bytes = GlobalSize(memory);
            if (bytes > MaximumBytes) { status = "内容超过单条 1 MiB 安全容量，未缓存；没有截断保存"; Broadcast(); return; }
            nint pointer = GlobalLock(memory); if (pointer == 0) return;
            string text;
            try { text = Marshal.PtrToStringUni(pointer, (int)bytes / 2) ?? ""; int end = text.IndexOf('\0'); if (end >= 0) text = text[..end]; }
            finally { GlobalUnlock(memory); }
            if (collectRequested && !disposed) CaptureText(text);
            }
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or IOException or NotSupportedException or FileFormatException) { status = "未缓存：" + error.Message; Broadcast(); }
        finally { CloseClipboard(); }
        if (!collectRequested || disposed) return;
        try
        {
            if (png != null) CaptureImage(ClipboardImages.FromPng(png));
            else if (bitmapAvailable && GetClipboardSequenceNumber() == sequence && System.Windows.Clipboard.GetImage() is { } bitmap) CaptureImage(ClipboardImages.FromBitmap(bitmap));
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or IOException or NotSupportedException) { status = "图片未缓存：" + error.Message; Broadcast(); }
    }
    private void CaptureText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length * 2 > MaximumBytes) { status = "内容超过单条 1 MiB 安全容量，未缓存"; Broadcast(); return; }
        foreach (var duplicate in entries.Where(item => item.Kind == "text" && item.Text == text).ToArray()) RemoveEntry(duplicate);
        entries.Insert(0, new ClipboardEntry(Guid.NewGuid().ToString("N"), text, DateTimeOffset.Now));
        Trim(); status = "文本与图片 · 本次运行内保留"; Broadcast();
    }
    private void RemoveEntry(ClipboardEntry entry) { entries.Remove(entry); images.TryRemove(entry.Id, out _); }
    private void Trim()
    {
        long Size(ClipboardEntry item) => item.Image is { } image ? image.Original.Length + image.Thumbnail.Length : item.Text.Length * 2L;
        long bytes = entries.Sum(Size);
        while (entries.Count > 100 || bytes > MaximumTotalBytes) { var entry = entries[^1]; bytes -= Size(entry); RemoveEntry(entry); }
    }
    private void CaptureImage(ClipboardImage image)
    {
        foreach (var duplicate in entries.Where(item => item.Image?.Hash == image.Hash).ToArray()) RemoveEntry(duplicate);
        string id = Guid.NewGuid().ToString("N"); images[id] = image;
        entries.Insert(0, new ClipboardEntry(id, $"图片 {image.Width} × {image.Height}", DateTimeOffset.Now, "image", image.Width, image.Height, $"https://clipboard.images.local/__clipboard/{id}/thumb.png", image));
        Trim(); status = "文本与图片 · 本次运行内保留"; Broadcast();
    }
    private object Snapshot() => new { type = "clipboardState", entries = entries.ToArray(), status };
    private void SendState(WidgetWindow window) => Post(window, Snapshot());
    private void Broadcast() { object snapshot = Snapshot(); ui.BeginInvoke(() => { if (!disposed) foreach (var window in clients.ToArray()) window.PostTelemetry(snapshot); }); }
    private void Post(WidgetWindow window, object value) => ui.BeginInvoke(() => { if (!disposed) window.PostTelemetry(value); });
    public void Dispose() { disposed = true; clients.Clear(); if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send); thread?.Join(200); }
}
