using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;

internal static class HostChecks
{
    internal static async void Run()
    {
        WidgetManager? manager = null;
        try
        {
            void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
            static WebView2CompositionControl Browser(WidgetWindow window) => (WebView2CompositionControl)window.FindName("Browser");
            static async Task<string> Script(WidgetWindow window, string script) => await Browser(window).CoreWebView2.ExecuteScriptAsync(script);
            static async Task Capture(WidgetWindow window, string name)
            {
                using var image = File.Create(Path.Combine(AppContext.BaseDirectory, name + ".png"));
                await Browser(window).CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            }
            var state = new WidgetState();
            Check(state.IsValid(), "Default state");
            state.Left = double.NaN; Check(!state.IsValid(), "Reject NaN position");
            state.Left = -1920; Check(state.IsValid(), "Support negative monitor position");
            state.Width = -1; Check(!state.IsValid(), "Reject invalid size");
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "widgets", "world-clock", "background-test.gif"), Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
            manager = new WidgetManager(); manager.Restore();
            if (Environment.GetCommandLineArgs().Contains("--image-sleep-only") || Environment.GetCommandLineArgs().Contains("--unified-only") || Environment.GetCommandLineArgs().Contains("--clipboard-only"))
            {
                if (Environment.GetCommandLineArgs().Contains("--unified-only")) await UnifiedWidgetChecks.Run(manager); else if (Environment.GetCommandLineArgs().Contains("--clipboard-only")) await ClipboardResizeChecks.Run(manager); else await ImageSleepChecks.Run(manager); await manager.WaitForWindowsAsync(); manager.Dispose();
                await App.ShutdownCompositionAsync(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "PASS: targeted widget checks"); Application.Current.Shutdown(0); return;
            }
            var definitions = manager.Scan();
            Check(definitions.Count == 15, "All fifteen plugins must be discoverable");
            var weatherCardDefinition = definitions.Single(d => d.Directory == "weather-card");
            Check(weatherCardDefinition.Width == 340 && weatherCardDefinition.Height == 210 && weatherCardDefinition.MinWidth == 260 && weatherCardDefinition.MinHeight == 160, "defaultWidth/defaultHeight manifest compatibility");
            Check(Application.Current.Windows.OfType<WidgetWindow>().Count() == 5, "Five separate default widgets");
            manager.Open(definitions.Single(d => d.Directory == "world-clock"));
            var windows = Application.Current.Windows.OfType<WidgetWindow>().ToArray();
            for (int attempt = 0; attempt < 250; attempt++)
            {
                await Task.Delay(100);
                bool ready = true;
                foreach (var item in windows)
                {
                    if (Browser(item).CoreWebView2 == null || await Script(item, "document.querySelector('.widget')?.dataset.ready === 'true'") != "true") { ready = false; break; }
                }
                if (ready) break;
            }
            foreach (var item in windows)
                Check(Browser(item).CoreWebView2 != null && await Script(item, "document.querySelector('.widget')?.dataset.ready === 'true'") == "true", "Plugin must render: " + item.State.WidgetName);
            foreach (var item in windows)
            {
                Check(WidgetWindow.CaptureDividerProperty != null && (int)Browser(item).GetValue(WidgetWindow.CaptureDividerProperty) == 3, "WebView capture frame divider");
                Check(((System.Windows.Controls.Border)item.FindName("DragHandle")).IsEnabled, "Shared host drag handle: " + item.State.WidgetName);
                nint itemHandle = new System.Windows.Interop.WindowInteropHelper(item).Handle;
                Check(Win32Interop.GetWindowRect(itemHandle, out var original), "Read original bounds");
                int actualMoves = 0;
                var itemSource = System.Windows.Interop.HwndSource.FromHwnd(itemHandle);
                nint CountMoves(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
                { if (message == 3) actualMoves++; return 0; }
                itemSource.AddHook(CountMoves);
                using (var session = new OutlineDragSession(item, new Win32Interop.Point { X = -500, Y = 100 }, () => { }))
                {
                    session.Update(new Win32Interop.Point { X = -460, Y = 130 }, true);
                    Check(Win32Interop.GetWindowRect(itemHandle, out var during) && during.Left == original.Left && during.Top == original.Top && actualMoves == 0, "Only outline moves during drag: " + item.State.WidgetName);
                    Check(session.PreviewBounds.Left == original.Left + 40 && session.PreviewBounds.Top == original.Top + 30, "Outline tracks native coordinates");
                    session.Update(new Win32Interop.Point { X = -460, Y = 130 }, false);
                    Check(Win32Interop.GetWindowRect(itemHandle, out var dropped) && dropped.Left == original.Left + 40 && dropped.Top == original.Top + 30 && actualMoves == 1, "Single drop move: " + item.State.WidgetName);
                }
                itemSource.RemoveHook(CountMoves);
                using (var cancelled = new OutlineDragSession(item, new Win32Interop.Point { X = 0, Y = 0 }, () => { }))
                {
                    cancelled.Update(new Win32Interop.Point { X = 70, Y = -20 }, true);
                    cancelled.Update(new Win32Interop.Point { X = 70, Y = -20 }, true, true);
                    Check(Win32Interop.GetWindowRect(itemHandle, out var afterCancel) && afterCancel.Left == original.Left + 40 && afterCancel.Top == original.Top + 30, "Cancel preserves position");
                }
                Win32Interop.SetWindowPos(itemHandle, 0, original.Left, original.Top, 0, 0, 0x15);
                int dragMessages = 0;
                EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs> observeDrag = (_, args) =>
                {
                    using var message = JsonDocument.Parse(args.WebMessageAsJson);
                    if (message.RootElement.GetProperty("type").GetString() == "startDrag") dragMessages++;
                };
                Browser(item).CoreWebView2.WebMessageReceived += observeDrag;
                await Script(item, "document.querySelector('.content').dispatchEvent(new PointerEvent('pointerdown', {bubbles:true,button:0,pointerType:'mouse'}))");
                await Task.Delay(80);
                Browser(item).CoreWebView2.WebMessageReceived -= observeDrag;
                Check(dragMessages == 1, "Every plugin routes body drag to host: " + item.State.WidgetName);
                item.State.Locked = true; item.ApplyLock();
                Check(!((System.Windows.Controls.Border)item.FindName("DragHandle")).IsEnabled, "Lock disables shared handle");
                item.State.Locked = false; item.ApplyLock();
                Win32Interop.SetWindowPos(itemHandle, 0, original.Left, original.Top, 0, 0, 0x15);
            }
            Check(windows.Select(w => new Uri(Browser(w).CoreWebView2.Source).Host).Distinct().Count() == 6, "Each instance has its own origin");
            Check(windows.Select(w => Browser(w).CoreWebView2.Profile.ProfileName).Distinct().Count() == 6, "Each instance has its own profile");
            var window = windows.First(w => w.State.WidgetName == "world-clock");
            var duplicate = windows.Last(w => w.State.WidgetName == "world-clock");
            nint positionMemory = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<Win32Interop.WindowPos>());
            try
            {
                var originalPosition = new Win32Interop.WindowPos { Hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle, InsertAfter = (nint)0x1234, Flags = 0x17 };
                System.Runtime.InteropServices.Marshal.StructureToPtr(originalPosition, positionMemory, false);
                Win32Interop.SendMessage(originalPosition.Hwnd, 0x46, 0, positionMemory);
                var constrained = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32Interop.WindowPos>(positionMemory);
                Check((constrained.Flags & 4) != 0 && constrained.InsertAfter == originalPosition.InsertAfter, "NOZORDER notifications must not trigger a new bottom reorder");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(positionMemory); }
            var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            int quietPositionChanges = 0;
            nint ObservePosition(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
            { if (message == 0x46) quietPositionChanges++; return 0; }
            source.AddHook(ObservePosition);
            window.ApplyLayer();
            source.RemoveHook(ObservePosition);
            await Script(window, "window.__clockNode = document.querySelector('.clock-time').firstChild");
            await Task.Delay(1300);
            bool stableClock = await Script(window, "window.__clockNode === document.querySelector('.clock-time').firstChild") == "true";
            Check(quietPositionChanges == 0 && stableClock, $"Unchanged layer refresh: {quietPositionChanges} native reposition messages, stable clock node = {stableClock}");
            await Script(window, "localStorage.setItem('isolation-check', 'private')");
            Check(await Script(duplicate, "localStorage.getItem('isolation-check')") == "null", "Duplicate clock storage is isolated");
            foreach (var item in windows.DistinctBy(w => w.State.WidgetName)) await Capture(item, item.State.WidgetName);
            var weather = windows.Single(w => w.State.WidgetName == "weather");
            foreach (var (width, height, label) in new[] { (240, 180, "small"), (320, 260, "medium"), (480, 360, "large") })
            {
                weather.Width = width; weather.Height = height; await Task.Delay(100);
                Check(await Script(weather, "document.querySelector('.content').scrollHeight <= document.querySelector('.content').clientHeight") == "true", "Weather layout fits: " + label);
                await Capture(weather, "weather-" + label);
            }
            weather.Width = 320; weather.Height = 260;
            Check(await Script(weather, "document.querySelectorAll('.cloud-layer').length === 3") == "true", "Weather has three independent cloud layers");
            string cloudTransform = await Script(weather, "getComputedStyle(document.querySelector('.cloud-layer.near')).transform");
            await Task.Delay(400);
            Check(await Script(weather, "matchMedia('(prefers-reduced-motion: reduce)').matches") == "true" || await Script(weather, "getComputedStyle(document.querySelector('.cloud-layer.near')).transform") != cloudTransform, "Cloud animation advances without page reload");
            await Script(weather, """
                (() => {
                  const originalFetch = window.fetch;
                  window.fetch = (url, options) => {
                    if (String(url).includes('geocoding-api.open-meteo.com')) return Promise.resolve(new Response(JSON.stringify({results:[{name:'上海',latitude:31.23,longitude:121.47,country:'中国'}]})));
                    if (String(url).includes('api.open-meteo.com')) return Promise.resolve(new Response(JSON.stringify({current:{time:'2026-11-17T12:00',temperature_2m:18,apparent_temperature:17,relative_humidity_2m:60,wind_speed_10m:8,weather_code:2,is_day:1},daily:{temperature_2m_max:[20],temperature_2m_min:[12]}})));
                    return originalFetch(url, options);
                  };
                  document.querySelector('#city').click();
                  const form=document.querySelector('#city-search'); form.elements.query.value='上海'; form.requestSubmit();
                })()
                """);
            await Task.Delay(250);
            Check(await Script(weather, "document.querySelectorAll('#city-results button').length === 1") == "true", "City search renders provider results");
            await Script(weather, "document.querySelector('#city-results button').click()"); await Task.Delay(200);
            Check(await Script(weather, "JSON.parse(localStorage.getItem('location')).name === '上海' && document.querySelector('#temperature').textContent === '18'") == "true", "City selection persists and updates its weather");
            string beforeReload = await Script(window, "performance.timeOrigin");
            var unrelated = windows.Single(w => w.State.WidgetName == "almanac");
            string unrelatedOrigin = await Script(unrelated, "performance.timeOrigin");
            string duplicateOrigin = await Script(duplicate, "performance.timeOrigin");
            string marker = Path.Combine(manager.WidgetsRoot, "world-clock", "app.js");
            string originalScript = File.ReadAllText(marker);
            File.AppendAllText(marker, "\nglobalThis.__hotReloadRevision = 'changed-source';\n");
            bool reloaded = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(100);
                var origin = await Script(window, "performance.timeOrigin");
                if (origin != beforeReload && origin != "null" && await Script(window, "globalThis.__hotReloadRevision === 'changed-source'") == "true" && await Script(duplicate, "performance.timeOrigin") != duplicateOrigin && await Script(duplicate, "globalThis.__hotReloadRevision === 'changed-source'") == "true") { reloaded = true; break; }
            }
            Check(reloaded, "File watcher reloads actual documents");
            Check(await Script(window, "globalThis.__hotReloadRevision") == "\"changed-source\"", "Reload must execute changed module bytes, not just restart the cached page");
            Check(await Script(unrelated, "performance.timeOrigin") == unrelatedOrigin, "Plugin reload leaves unrelated widgets running");
            Check(await Script(duplicate, "performance.timeOrigin") != duplicateOrigin, "Plugin reload reaches its duplicate instances");
            File.WriteAllText(marker, originalScript);
            await Task.Delay(800);
            window.Width = 240; window.Height = 140;
            await Task.Delay(200);
            Check(await Script(window, "document.querySelector('.clock-time').scrollWidth <= document.querySelector('.content').clientWidth") == "true", "Compact clock fits");
            await Capture(window, "clock-compact");
            var almanac = windows.Single(w => w.State.WidgetName == "almanac");
            almanac.Width = 240; almanac.Height = 160; await Task.Delay(150);
            Check(await Script(almanac, "getComputedStyle(document.querySelector('.regular')).display === 'none' && getComputedStyle(document.querySelector('.expanded')).display === 'none'") == "true", "Compact almanac reduces information");
            await Capture(almanac, "almanac-compact");
            almanac.Width = 460; almanac.Height = 600; await Task.Delay(150);
            Check(await Script(almanac, "getComputedStyle(document.querySelector('.expanded')).display !== 'none' && document.querySelector('#chong').textContent.length > 0") == "true", "Large almanac reveals details");
            await Capture(almanac, "almanac-large");
            Check(await Script(almanac, "(() => { const c=document.querySelector('.content'); return c.scrollHeight <= c.clientHeight; })()") == "true", "Expanded almanac must fit without clipping");
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Check((Win32Interop.GetLong(handle, Win32Interop.ExStyle) & 0x08000080) == 0x08000080, "Native tool/no-activate styles");
            Check(Win32Interop.GetLong(handle, Win32Interop.Owner) != 0, "Desktop owner");
            await Script(window, "window.chrome.webview.postMessage({type:'toggleLock'})"); await Task.Delay(100);
            Check(window.State.Locked && !duplicate.State.Locked && window.ResizeMode == ResizeMode.NoResize, "Web message only locks its own instance");
            Check(window.FindName("DragHandle") is System.Windows.Controls.Border { Visibility: Visibility.Collapsed }, "Locked host grip is hidden");
            Check(await Script(window, "[...document.querySelectorAll('.tools button')].filter(button=>getComputedStyle(button).display!=='none').length===1 && document.querySelector('[data-action=toggleLock]').getAttribute('aria-label')==='解锁组件'") == "true", "Locked widget exposes only unlock");
            await Script(window, "document.querySelector('[data-action=toggleLock]').click()"); await Task.Delay(100);
            Check(!window.State.Locked, "The single unlock button remains usable");
            window.IsPinnedTop = true;
            Check(window.Topmost && Win32Interop.GetLong(handle, Win32Interop.Owner) == 0, "Pin detaches desktop owner");
            window.IsPinnedTop = false;
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(200);
            Check((Win32Interop.GetLong(handle, Win32Interop.ExStyle) & 0x08000000) == 0, "Settings allow keyboard input");
            await Script(window, "(() => {const f=document.querySelector('dialog form');f.elements.transparency.value='35';f.elements.glassBlur.value='12';f.elements.transparency.dispatchEvent(new Event('input'));f.elements.glassBlur.dispatchEvent(new Event('input'));})()");
            Check(await Script(window, "getComputedStyle(document.querySelector('.widget-surface')).opacity==='0.65' && getComputedStyle(document.querySelector('.content')).opacity==='1' && getComputedStyle(document.querySelector('.background-glass')).backdropFilter.includes('12px')") == "true", "Live background-only alpha and real backdrop blur");
            await Capture(window, "appearance-preview");
            await Script(window, "document.querySelector('[data-cancel]').click()"); await Task.Delay(60);
            Check(await Script(window, "getComputedStyle(document.querySelector('.widget-surface')).opacity==='0.85'") == "true", "Cancel rolls appearance back");
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(60);
            await Script(window, "(() => {const f=document.querySelector('dialog form');f.elements.transparency.value='35';f.elements.glassBlur.value='12';f.requestSubmit();})()"); await Task.Delay(60);
            Check(await Script(window, "JSON.parse(localStorage.getItem('appearance')).transparency===35 && JSON.parse(localStorage.getItem('appearance')).blur===12") == "true", "Appearance persists per instance");
            Check(await Script(duplicate, "localStorage.getItem('appearance')") == "null", "Appearance does not leak to another clock instance");
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(60);
            await Script(window, "(() => {const f=document.querySelector('dialog form');f.elements.transparency.value='100';f.elements.glassBlur.value='0';f.elements.transparency.dispatchEvent(new Event('input'));f.elements.glassBlur.dispatchEvent(new Event('input'));})()");
            Check(await Script(window, "getComputedStyle(document.querySelector('.widget-surface')).visibility==='hidden' && getComputedStyle(document.querySelector('.background-glass')).display==='none' && getComputedStyle(document.querySelector('.content')).opacity==='1' && getComputedStyle(document.querySelector('.widget')).backgroundColor==='rgba(12, 20, 30, 0.004)'") == "true", "Fully transparent background preserves readable foreground and hit-test layer");
            await Script(window, "document.querySelector('dialog form').requestSubmit()"); await Task.Delay(60);
            await Capture(window, "appearance-transparent");
            using (var transparentPreview = new System.Drawing.Bitmap(Path.Combine(AppContext.BaseDirectory, "appearance-transparent.png")))
                Check(transparentPreview.GetPixel(transparentPreview.Width - 8, transparentPreview.Height / 2).A is > 0 and < 5, "Transparent background retains only a minimal native hit-test alpha away from text shadows");
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(60);
            await Script(window, "(() => {const f=document.querySelector('dialog form');f.elements.transparency.value='35';f.elements.glassBlur.value='12';f.requestSubmit();})()"); await Task.Delay(60);
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(60);
            await Script(window, "(() => { const f=document.querySelector('dialog form'); f.elements.backgroundType.value='image'; f.elements.backgroundPath.value='background-test.gif'; f.requestSubmit(); })()");
            await Task.Delay(300);
            Check(await Script(window, "document.querySelector('.background img')?.naturalWidth === 1") == "true", "Local GIF background loads");
            Check((Win32Interop.GetLong(handle, Win32Interop.ExStyle) & 0x08000000) != 0, "Settings restore no-activate mode");
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(100);
            await Script(window, "(() => { const f=document.querySelector('dialog form'); f.elements.backgroundType.value='video'; f.elements.backgroundPath.value='background-test.webm'; f.requestSubmit(); })()");
            await Task.Delay(700);
            Check(await Script(window, "(() => { const v=document.querySelector('.background video'); return !!v && v.loop && v.muted && v.currentTime > 0; })()") == "true", "Looping WebM background plays");
            await Script(window, "document.querySelector('[data-action=settings]').click()"); await Task.Delay(60);
            await Script(window, "(() => {const f=document.querySelector('dialog form');f.elements.transparency.value='100';f.elements.transparency.dispatchEvent(new Event('input'));})()"); await Task.Delay(60);
            Check(await Script(window, "document.querySelector('.background video').paused") == "true", "Fully transparent background pauses video decoding");
            await Script(window, "document.querySelector('[data-cancel]').click()"); await Task.Delay(120);
            Check(await Script(window, "!document.querySelector('.background video').paused") == "true", "Cancel resumes visible video");
            window.State.Locked = true; window.ApplyLock();
            window.Width = 400; window.Height = 220;
            bool persisted = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")));
                var saved = config.RootElement.EnumerateArray().Single(s => s.GetProperty("id").GetString() == window.State.Id);
                if (saved.GetProperty("locked").GetBoolean() && saved.GetProperty("width").GetDouble() == 400) { persisted = true; break; }
            }
            Check(persisted, "Debounced state persistence");
            string id = window.State.Id;
            await manager.WaitForWindowsAsync();
            manager.Dispose(); manager = new WidgetManager(); manager.Restore();
            var restored = Application.Current.Windows.OfType<WidgetWindow>().Single(w => w.State.Id == id);
            Check(restored.State.Locked && restored.Width == 400, "Restore individual instance state");
            await manager.WaitForWindowsAsync();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(100);
                if (await Script(restored, "document.querySelector('.background video')?.currentTime > 0") == "true") break;
            }
            Check(await Script(restored, "document.querySelector('.background video')?.currentTime > 0") == "true", "Independent video preference survives restore");
            Check(await Script(restored, "getComputedStyle(document.querySelector('.widget-surface')).opacity==='0.65' && document.querySelector('.widget').dataset.glass==='on'") == "true", "Background appearance survives host restart");
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                uint beforeHandles = Win32Interop.GetGuiResources(process.Handle, 1);
                for (int cycle = 0; cycle < 15; cycle++)
                {
                    using var first = new OutlineDragSession(restored, new Win32Interop.Point(), () => { });
                    bool previousCancelled = false;
                    using var previous = new OutlineDragSession(restored, new Win32Interop.Point(), () => previousCancelled = true);
                    using var replacement = new OutlineDragSession(restored, new Win32Interop.Point(), () => { });
                    Check(previousCancelled && OutlineDragSession.IsDragging, "Only one drag session can survive a new gesture");
                }
                await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check(!OutlineDragSession.IsDragging, "No drag timer remains after repeated sessions");
                uint afterHandles = Win32Interop.GetGuiResources(process.Handle, 1);
                Check(afterHandles <= beforeHandles + 2, $"Drag USER handles must remain bounded: {beforeHandles} -> {afterHandles}");
            }
            await WeatherCardChecks.Run(manager);
            await TelemetryChecks.Run(manager);
            await UtilityChecks.Run(manager);
            await NotesLayoutChecks.Run(manager);
            await ClipboardResizeChecks.Run(manager);
            await ImageSleepChecks.Run(manager);
            await UnifiedWidgetChecks.Run(manager);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "PASS: fifteen plugins, independent profiles, event-driven drag bridge/handle and bounded USER handles, single drop/cancel, unlock-only controls, NOZORDER, capture divider, changed module bytes after reload, Weather Card, four system chart layouts, native metrics/Core Audio, safe audio command preview, five utility plugins/component center/media read attempt/workspace validation, four-chart overview, history/desktop readonly notes, right arrangement/minimum-size invariants, eight-way outline resize, clipboard native listener/full-copy/width-wrap/pause, PNG images/adaptive layout, real renderer fullscreen sleep/wake, unified clock/weather catalog and region creation, appearance, config restore and safe exit");
            await manager.WaitForWindowsAsync();
            manager.Dispose();
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await App.ShutdownCompositionAsync();
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), error.ToString());
            manager?.Dispose();
            await App.ShutdownCompositionAsync();
            Application.Current.Shutdown(1);
        }
    }
}
