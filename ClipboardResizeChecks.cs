using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;
internal static class ClipboardResizeChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        static Win32Interop.Rect Bounds(Window window) { Check(Win32Interop.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds), "Native bounds"); return bounds; }
        var target = manager.OpenWindows.First(w => !w.State.Locked);
        target.Width = 400; target.Height = 300; await Task.Delay(100);
        var hwnd = new WindowInteropHelper(target).Handle;
        foreach (bool pinned in new[] { false, true })
        {
            target.IsPinnedTop = pinned;
            foreach (int edge in Enumerable.Range(10, 8))
            {
                var original = Bounds(target); int resizes = 0;
                nint Count(nint h, int message, nint wp, nint lp, ref bool handled) { if (message == 5) resizes++; return 0; }
                var source = HwndSource.FromHwnd(hwnd); source.AddHook(Count);
                using (var session = new OutlineDragSession(target, new Win32Interop.Point { X = 500, Y = 500 }, () => { }, edge))
                {
                    session.Update(new Win32Interop.Point { X = 540, Y = 535 }, true);
                    Check(Bounds(target).Equals(original) && resizes == 0, "Resize only updates outline before release, edge " + edge);
                    Check(!session.PreviewBounds.Equals(original), "Outline changes for each resize direction");
                    session.Update(new Win32Interop.Point { X = 540, Y = 535 }, false);
                    Check(resizes == 1 && Bounds(target).Equals(session.PreviewBounds), "One native resize on release, pinned=" + pinned + ", edge=" + edge);
                }
                source.RemoveHook(Count);
                Win32Interop.SetWindowPos(hwnd, 0, original.Left, original.Top, original.Right - original.Left, original.Bottom - original.Top, 0x14);
            }
        }
        var before = Bounds(target);
        using (var cancelled = new OutlineDragSession(target, new Win32Interop.Point(), () => { }, 17)) { cancelled.Update(new Win32Interop.Point { X = -10000, Y = -10000 }, true); Check(cancelled.PreviewBounds.Right - cancelled.PreviewBounds.Left >= target.MinWidth && cancelled.PreviewBounds.Bottom - cancelled.PreviewBounds.Top >= target.MinHeight, "Outline clamps plugin minimum"); cancelled.Update(new Win32Interop.Point(), true, true); }
        Check(Bounds(target).Equals(before), "Cancelled resize leaves target unchanged");
        var handles = (System.Windows.Controls.Grid)target.FindName("ResizeHandles");
        Check(handles.Children.Count == 8 && handles.Children.OfType<FrameworkElement>().Single(c => c.Tag?.ToString() == "17").Width == 30, "Eight resize targets with 30 DIP bottom corner");
        var corner = handles.Children.OfType<UIElement>().Single(c => ((FrameworkElement)c).Tag?.ToString() == "17");
        Check(corner.InputHitTest(new Point(20, 20)) != null && corner.InputHitTest(new Point(5, 5)) == null, "Large diagonal hit target leaves inner footer controls unobstructed");
        target.State.Locked = true; target.ApplyLock(); Check(handles.Visibility == Visibility.Collapsed, "Lock hides resize overlay"); target.State.Locked = false; target.ApplyLock(); target.IsPinnedTop = false;
        manager.Open(manager.Scan().Single(d => d.Directory == "clipboard-cache"));
        var window = manager.OpenWindows.Last(w => w.State.WidgetName == "clipboard-cache");
        await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await window.Initialization;
        var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
        for (int i = 0; i < 100; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget')?.dataset.ready==='true'") == "true") break; await Task.Delay(50); }
        Check(window.IsPinnedTop, "New clipboard card defaults to pinned top");
        await core.ExecuteScriptAsync("Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'))"); await Task.Delay(50);
        Check(manager.Clipboard.SubscriberCount == 1, "Covered card continues caching without rendering");
        await core.ExecuteScriptAsync("delete document.hidden;document.dispatchEvent(new Event('visibilitychange'))");
        for (int i = 0; i < 7; i++) await manager.Clipboard.InjectForTest("缓存 " + i).WaitAsync(TimeSpan.FromSeconds(5));
        string longText = string.Concat(Enumerable.Repeat("长段落与链接 https://example.com/ ", 100)) + "尾部标记";
        await manager.Clipboard.InjectForTest(longText); await Task.Delay(100);
        Check(manager.Clipboard.NativeListening && manager.Clipboard.SubscriberCount == 1, "Native listener is registered on dedicated STA without reading actual clipboard in tests");
        Check(await core.ExecuteScriptAsync("document.querySelectorAll('#recent button').length===8 && Number(document.querySelector('.widget').dataset.visibleCount)>=3 && Number(document.querySelector('.widget').dataset.visibleCount)<=5") == "true", "Resident text count adapts to available space");
        Check(await core.ExecuteScriptAsync("document.querySelector('#recent button').textContent===" + JsonSerializer.Serialize(longText)) == "true", "Resident entry keeps full text and wraps by width");
        await core.ExecuteScriptAsync("globalThis.__copied=null;chrome.webview.addEventListener('message',({data})=>{if(data.type==='clipboardResult')globalThis.__copied=data;});document.querySelector('#recent button').click()"); await Task.Delay(100);
        Check(await core.ExecuteScriptAsync("globalThis.__copied?.success && globalThis.__copied.length===" + longText.Length) == "true", "Click selects complete content; test never writes system clipboard or sends input");
        await core.ExecuteScriptAsync("document.querySelector('[data-action=settings]').click();const form=document.querySelector('.widget-settings form');form.elements.count.value='3';form.requestSubmit()"); await Task.Delay(80);
        Check(await core.ExecuteScriptAsync("document.querySelectorAll('#recent button').length===8 && Number(document.querySelector('.widget').dataset.visibleCount)===3 && getComputedStyle(document.querySelector('#recent button')).whiteSpace==='nowrap' && getComputedStyle(document.querySelector('#recent button')).textOverflow==='ellipsis'") == "true", "Three single-line entries with visual ellipsis and full underlying content");
        await core.ExecuteScriptAsync("document.querySelector('#history-open').click();document.querySelector('#search').value='尾部标记';document.querySelector('#search').dispatchEvent(new Event('input'))"); await Task.Delay(180);
        Check(await core.ExecuteScriptAsync("document.querySelectorAll('#results button').length===1 && document.querySelector('#results button').textContent.endsWith('尾部标记')") == "true", "Search reaches end of long content");
        await core.ExecuteScriptAsync("document.querySelector('#history-close').click();document.querySelector('#pause').click()"); await Task.Delay(100);
        Check(manager.Clipboard.SubscriberCount == 0 && !manager.Clipboard.NativeListening, "Pause unregisters native clipboard listener");
        await core.ExecuteScriptAsync("document.querySelector('#pause').click()"); await Task.Delay(100);
        Check(manager.Clipboard.SubscriberCount == 1 && manager.Clipboard.NativeListening, "Resume registers one native listener");
        await manager.Clipboard.InjectForTest(longText); await Task.Delay(60);
        Check(await core.ExecuteScriptAsync("document.querySelector('#count').textContent==='8'") == "true", "Repeated text is deduplicated");
        await manager.Clipboard.InjectForTest(new string('x', 600000)); await Task.Delay(60);
        Check(await core.ExecuteScriptAsync("document.querySelector('#count').textContent==='8'") == "true", "Oversized content rejected whole, without truncating or erasing history");
        using (var png = File.Create(Path.Combine(AppContext.BaseDirectory, "clipboard-cache.png"))) await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, png);
        await core.ExecuteScriptAsync("document.querySelector('#clear').click()"); await Task.Delay(70);
        Check(await core.ExecuteScriptAsync("document.querySelector('#count').textContent==='0'") == "true", "Clear only clears host cache"); window.Close(); await Task.Delay(60);
        Check(manager.Clipboard.SubscriberCount == 0 && !manager.Clipboard.NativeListening, "Last card close releases native listener");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "clipboard-resize-test.txt"), "PASS: eight-direction outline resize pinned/unpinned, no target resize before release, one final resize, minimum/cancel/lock, native clipboard STA listener registration/pause/cleanup, five/three rows, full-text width wrapping/search/selection, deduplication and capacity rejection; no real clipboard read/write or keyboard injection");
    }
}
