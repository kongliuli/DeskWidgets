using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;
internal static class ImageSleepChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        var monitor = new Win32Interop.Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
        Check(FullscreenMonitor.CoversMonitor(monitor, monitor, false, false), "Borderless fullscreen geometry includes negative monitor coordinates");
        Check(!FullscreenMonitor.CoversMonitor(monitor, monitor, true, true), "Normal maximized captioned window is not fullscreen");
        Check(!FullscreenMonitor.CoversMonitor(new Win32Interop.Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1040 }, monitor, false, false), "Work-area maximization is not monitor fullscreen");
        byte[] pixels = new byte[800 * 600 * 4];
        for (int y = 0; y < 600; y++) for (int x = 0; x < 800; x++) { int p = (y * 800 + x) * 4; pixels[p] = (byte)(x % 256); pixels[p + 1] = (byte)(y % 256); pixels[p + 2] = 180; pixels[p + 3] = x < 10 ? (byte)0 : (byte)255; }
        var bitmap = BitmapSource.Create(800, 600, 96, 96, PixelFormats.Bgra32, null, pixels, 800 * 4);
        var image = ClipboardImages.FromBitmap(bitmap); var decoded = ClipboardImages.FromPng(image.Original);
        Check(decoded.Width == 800 && decoded.Height == 600 && ClipboardImages.DecodeOriginal(decoded).PixelWidth == 800, "PNG cache preserves original dimensions");
        using (var stream = new MemoryStream(image.Thumbnail)) { var thumb = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Check(thumb.PixelWidth <= 640 && thumb.PixelHeight <= 400, "Thumbnail bounded independently of original"); }
        var original = new FormatConvertedBitmap(ClipboardImages.DecodeOriginal(image), PixelFormats.Bgra32, null, 0); byte[] first = new byte[4]; original.CopyPixels(new Int32Rect(0, 0, 1, 1), first, 4, 0); Check(first[3] == 0, "PNG retains transparency");
        var data = ClipboardImages.CreateDataObject(image); Check(data.GetImage().PixelHeight == 600 && data.GetData("PNG") is MemoryStream png && png.Length == image.Original.Length, "Paste payload provides bitmap plus full-resolution PNG without touching system clipboard");
        Check(!ClipboardImages.ValidDimensions(100000, 100000), "Image decompression dimensions are bounded before decode");
        async Task<(WidgetWindow Window, CoreWebView2 Core)> Open(string directory)
        {
            manager.Open(manager.Scan().Single(d => d.Directory == directory)); var window = manager.OpenWindows.Last(w => w.State.WidgetName == directory);
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await window.Initialization;
            var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
            for (int i = 0; i < 120; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget')?.dataset.ready==='true'") == "true") return (window, core); await Task.Delay(50); }
            throw new InvalidOperationException(directory + " did not initialize");
        }
        var (clipboard, cc) = await Open("clipboard-cache");
        var responses = new System.Collections.Generic.List<string>();
        cc.WebResourceResponseReceived += (_, args) => { if (args.Request.Uri.Contains("/__clipboard/", StringComparison.Ordinal)) responses.Add(args.Request.Uri + " " + args.Response.StatusCode); };
        await manager.Clipboard.InjectImageForTest(image.Original).WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 80; i++) { if (await cc.ExecuteScriptAsync("document.querySelector('#recent img')?.naturalWidth>0") == "true") break; await Task.Delay(50); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "image-dom.json"), await cc.ExecuteScriptAsync("JSON.stringify({html:document.querySelector('#recent').innerHTML,status:document.querySelector('#status').textContent,complete:document.querySelector('#recent img')?.complete,width:document.querySelector('#recent img')?.naturalWidth})"));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "image-responses.txt"), responses);
        Check(await cc.ExecuteScriptAsync("document.querySelector('#recent img')?.naturalWidth>0 && document.querySelector('#recent .image-entry span').textContent.includes('800 × 600')") == "true", "PNG thumbnail served through per-widget virtual host");
        await cc.ExecuteScriptAsync("globalThis.__imageCopy=null;chrome.webview.addEventListener('message',({data})=>{if(data.type==='clipboardResult')globalThis.__imageCopy=data;});document.querySelector('#recent button').click()"); await Task.Delay(80);
        Check(await cc.ExecuteScriptAsync("globalThis.__imageCopy?.kind==='image' && globalThis.__imageCopy.width===800 && globalThis.__imageCopy.height===600") == "true", "Click copies original image payload rather than thumbnail");
        for (int i = 0; i < 15; i++) await manager.Clipboard.InjectForTest("自适应条目 " + i);
        await cc.ExecuteScriptAsync("document.querySelector('[data-action=settings]').click();const f=document.querySelector('.widget-settings form');f.elements.count.value='auto';f.requestSubmit()");
        clipboard.Width = 300; clipboard.Height = 260; await Task.Delay(80); int compact = int.Parse(await cc.ExecuteScriptAsync("Number(document.querySelector('.widget').dataset.visibleCount)"));
        clipboard.Width = 820; clipboard.Height = 620; await Task.Delay(100); int expanded = int.Parse(await cc.ExecuteScriptAsync("Number(document.querySelector('.widget').dataset.visibleCount)"));
        Check(await cc.ExecuteScriptAsync("document.querySelectorAll('#recent button').length===16 && document.querySelector('#recent').scrollHeight>document.querySelector('#recent').clientHeight") == "true", "All cached rows exist and remain scrollable beyond viewport");
        Check(expanded > compact && expanded <= 100 && await cc.ExecuteScriptAsync("getComputedStyle(document.querySelector('#recent')).gridTemplateColumns.split(' ').length===1") == "true", "Resident rows grow with height while remaining one column");
        await manager.Clipboard.InjectImageForTest(image.Original); await Task.Delay(80);
        using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "clipboard-images.png"))) await cc.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
        var (system, _) = await Open("system-monitor"); var (audio, _) = await Open("audio-control"); var (media, _) = await Open("media-player"); var (writer, _) = await Open("notes");
        manager.Notes.Add("全屏休眠测试"); string noteId = JsonSerializer.Deserialize<SavedNote[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "notes-history.json")))!.Last().Id;
        manager.Notes.BeginDrag(writer, noteId); manager.Notes.EndDrag(writer, false);
        int heartbeats = 0;
        cc.WebMessageReceived += (_, args) => { if (args.WebMessageAsJson.Contains("sleepHeartbeat", StringComparison.Ordinal)) heartbeats++; };
        await cc.ExecuteScriptAsync("globalThis.__sleepTimer=setInterval(()=>chrome.webview.postMessage({type:'sleepHeartbeat'}),50)"); await Task.Delay(150);
        Check(manager.Telemetry.SubscriberCount > 0 && manager.Clipboard.SubscriberCount > 0, "Native samplers active before sleep");
        var states = manager.OpenWindows.Select(w => (Window: w, w.Left, w.Top, w.Width, w.Height, w.State.Locked, w.State.Topmost)).ToArray();
        manager.SetSleeping(true); await manager.WaitForSleepAsync().WaitAsync(TimeSpan.FromSeconds(15)); await Task.Delay(150);
        foreach (var state in states)
        {
            var control = (WebView2CompositionControl)state.Window.FindName("Browser");
            Check(state.Window.IsSleeping && !state.Window.IsVisible && control.CoreWebView2.IsSuspended, "All widget renderers suspended: " + state.Window.State.WidgetName);
        }
        Check(manager.Telemetry.SubscriberCount == 0 && manager.Clipboard.SubscriberCount == 0 && !manager.Clipboard.NativeListening && manager.Notes.Windows.All(w => !w.IsVisible), "Sleep stops native sampling/listening and hides desktop notes");
        int paused = heartbeats; await Task.Delay(250); Check(heartbeats == paused, "Sleeping page script timer stops");
        manager.SetSleeping(false); await manager.WaitForSleepAsync().WaitAsync(TimeSpan.FromSeconds(15)); await Task.Delay(250);
        foreach (var state in states) Check(state.Window.IsVisible && !state.Window.IsSleeping && state.Window.Left == state.Left && state.Window.Top == state.Top && state.Window.Width == state.Width && state.Window.Height == state.Height && state.Window.State.Locked == state.Locked && state.Window.State.Topmost == state.Topmost, "Wake preserves layout and lock/pin state");
        Check(manager.Telemetry.SubscriberCount >= 2 && manager.Clipboard.SubscriberCount == 1 && manager.Notes.Windows.All(w => w.IsVisible) && heartbeats > paused, "Wake resumes native services, notes and page timers");
        for (int i = 0; i < 3; i++) { manager.SetSleeping(true); await manager.WaitForSleepAsync(); manager.SetSleeping(false); await manager.WaitForSleepAsync(); }
        await cc.ExecuteScriptAsync("clearInterval(globalThis.__sleepTimer)"); manager.Notes.CloseDesktop(noteId);
        foreach (var window in new[] { clipboard, system, audio, media, writer }) window.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "image-sleep-test.txt"), "PASS: original PNG/bitmap payload, alpha/dimensions/bounded thumbnail, virtual-host image rendering, adaptive count/columns, fullscreen geometry, real WebView suspend/heartbeat stop, native sampler/listener pause, desktop notes hidden, layout/lock/pin preserved and repeated wake");
    }
}
