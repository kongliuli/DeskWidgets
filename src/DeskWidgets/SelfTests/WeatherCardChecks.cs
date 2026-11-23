using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;

internal static class WeatherCardChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        manager.Open(manager.Scan().Single(d => d.Directory == "weather-card"));
        var window = Application.Current.Windows.OfType<WidgetWindow>().Single(w => w.State.WidgetName == "weather-card");
        await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await window.Initialization;
        var browser = (WebView2CompositionControl)window.FindName("Browser");
        var core = browser.CoreWebView2;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), hour = now / 3600 * 3600;
        string fixture = JsonSerializer.Serialize(new
        {
            timezone = "Asia/Shanghai",
            current = new { time = now, temperature_2m = 18, relative_humidity_2m = 65, apparent_temperature = 17, is_day = 1, precipitation = 0, weather_code = 2, cloud_cover = 45, wind_speed_10m = 14, wind_direction_10m = 270 },
            hourly = new { time = Enumerable.Range(0, 7).Select(i => hour + i * 3600).ToArray(), temperature_2m = new[] { 18, 19, 20, 18, 17, 16, 15 }, weather_code = new[] { 2, 0, 3, 61, 75, 95, 0 }, is_day = new[] { 1, 1, 1, 1, 0, 0, 0 } }
        });
        int requests = 0;
        core.AddWebResourceRequestedFilter("https://*.open-meteo.com/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            bool search = args.Request.Uri.Contains("geocoding-api", StringComparison.Ordinal);
            if (!search) requests++;
            string json = search ? "{\"results\":[{\"name\":\"东京\",\"latitude\":35.68,\"longitude\":139.69,\"country\":\"日本\"}]}" : fixture;
            args.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), 200, "OK", "Content-Type: application/json\r\nAccess-Control-Allow-Origin: *");
        };
        async Task<string> Script(string script) => await core.ExecuteScriptAsync(script);
        async Task WaitFor(string expression, string reason)
        {
            for (int i = 0; i < 100; i++) { if (await Script(expression) == "true") return; await Task.Delay(50); }
            string diagnostic = await Script("JSON.stringify({enabled:window.__cardEngine?.enabled,running:window.__cardEngine?.running,alpha:document.querySelector('.weather-card')?.style.getPropertyValue('--surface-alpha'),hidden:document.querySelector('.weather-card')?.dataset.backgroundHidden,surfaceOpacity:getComputedStyle(document.querySelector('.widget-surface')).opacity,visibility:getComputedStyle(document.querySelector('.widget-surface')).visibility,input:document.querySelector('[name=transparency]')?.value,cancelDisabled:document.querySelector('#cancel-appearance')?.disabled,dialogOpen:document.querySelector('#appearance-dialog')?.open,saved:localStorage.getItem('appearance'),pageHidden:document.hidden})");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "weather-appearance-diagnostic.json"), diagnostic);
            throw new InvalidOperationException(reason + "; initializationCount=" + window.BrowserInitializationCount + "; reloadCount=" + window.BrowserReloadCount + "; " + diagnostic);
        }
        async Task Capture(string label)
        {
            using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "weather-card-" + label + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        await WaitFor("document.querySelector('.weather-card')?.dataset.ready === 'true'", "Weather Card must initialize");
        await Script("document.querySelector('#refresh').click(); import('./app.js').then(module => window.__cardEngine = module.engine)");
        await WaitFor("!!window.__cardEngine && document.querySelector('#temperature').textContent === '18' && document.querySelector('#status').textContent.startsWith('更新')", "Weather Card must render API fixture");
        Check(await Script("document.querySelectorAll('#hourly li time[datetime]').length === 5") == "true", "Five genuine future hours");
        string initialDocument = await Script("performance.timeOrigin");
        string folder = Path.Combine(manager.WidgetsRoot, "weather-card"); DateTime originalDirectoryTime = Directory.GetLastWriteTimeUtc(folder);
        Directory.SetLastWriteTimeUtc(folder, originalDirectoryTime.AddSeconds(5)); await Task.Delay(700);
        Check(window.BrowserReloadCount == 0 && await Script("performance.timeOrigin") == initialDocument, "Directory metadata changes must not reload widgets");
        Directory.SetLastWriteTimeUtc(folder, originalDirectoryTime);

        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); await Task.Delay(150);
        Check(window.BrowserInitializationCount == 1 && await Script("performance.timeOrigin") == initialDocument && await Script("!!window.__cardEngine") == "true", "Repeated WPF Loaded must not reinitialize or navigate the browser; init=" + window.BrowserInitializationCount + "; reload=" + window.BrowserReloadCount + "; navigation=" + window.BrowserNavigationCount + "; before=" + initialDocument + "; after=" + await Script("performance.timeOrigin") + "; engine=" + await Script("!!window.__cardEngine"));
        int dragMessages = 0;
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> observeDrag = (_, args) =>
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            if (message.RootElement.GetProperty("type").GetString() == "startDrag") dragMessages++;
        };
        core.WebMessageReceived += observeDrag;
        await Script("document.querySelector('.content').dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0}))");
        await Task.Delay(80);
        Check(dragMessages == 1, "Weather Card left-button drag bridge");
        window.State.Locked = true; window.ApplyLock(); await Task.Delay(50);
        await Script("document.querySelector('.content').dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0}));document.querySelector('#preview-toggle').dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0}))");
        await Task.Delay(50); Check(dragMessages == 1, "Locked card and interactive controls do not start drag");
        window.State.Locked = false; window.ApplyLock(); core.WebMessageReceived -= observeDrag;
        await WaitFor("!document.querySelector('#appearance-toggle').disabled", "Unlock restores weather controls");
        await Capture("default");
        await Script("document.querySelector('#appearance-toggle').click()");
        await Script("(() => {const f=document.querySelector('#appearance-form');f.elements.transparency.value='100';f.elements.glassBlur.value='0';f.elements.transparency.dispatchEvent(new Event('input'));f.elements.glassBlur.dispatchEvent(new Event('input'));})()");
        await WaitFor("!window.__cardEngine.enabled && !window.__cardEngine.running && getComputedStyle(document.querySelector('.content')).opacity==='1' && getComputedStyle(document.querySelector('.widget-surface')).visibility==='hidden'", "Invisible weather background suspends Canvas without fading text");
        await Script("document.querySelector('#cancel-appearance').click()");
        await WaitFor("window.__cardEngine.enabled && getComputedStyle(document.querySelector('.widget-surface')).opacity==='0.85'", "Cancel restores weather opacity and animation");
        await Script("(() => {document.querySelector('#appearance-toggle').click();const f=document.querySelector('#appearance-form');f.elements.transparency.value='45';f.elements.glassBlur.value='6';f.requestSubmit();})()"); await Task.Delay(50);
        Check(await Script("JSON.parse(localStorage.getItem('appearance')).transparency===45 && getComputedStyle(document.querySelector('.background-glass')).backdropFilter.includes('6px')") == "true", "Weather appearance shares persisted controls");
        await Capture("glass");
        await Script("document.querySelector('#preview-toggle').click()");
        Check(await Script("!document.querySelector('#simulator').hidden && document.querySelectorAll('#scene-options button').length === 7") == "true", "Live simulator controls");
        await Script("document.querySelector('#preview-toggle').click(); document.querySelector('.weather-card').style.transition='none'");
        int beforePreview = requests;
        foreach (string scene in new[] { "clear", "partly-cloudy", "overcast", "rain", "thunderstorm", "snow", "fog" })
        {
            foreach (bool day in new[] { true, false })
            {
                await Script($$"""
                    (() => {
                      document.querySelector('#scene-options [data-scene="{{scene}}"]').click();
                      if (window.__cardEngine.state.isDay !== {{(day ? "true" : "false")}}) document.querySelector('#day-mode').click();
                      window.__cardEngine.pause();
                      for(let i=0;i<120;i++) window.__cardEngine.render(1/30);
                    })()
                    """);
                Check(await Script($"window.__cardEngine.state.scene === '{scene}' && window.__cardEngine.state.isDay === {(day ? "true" : "false")}") == "true", "Simulator scene/day: " + scene);
                if (scene == "thunderstorm")
                {
                    await Script("window.__cardEngine.nextStrike = window.__cardEngine.time; window.__cardEngine.render(1/30)");
                    Check(await Script("window.__cardEngine.motion.matches ? window.__cardEngine.strike === null : window.__cardEngine.strike?.branches.length === 2") == "true", "Fractal lightning or reduced-motion suppression");
                }
                if (day || scene == "clear") await Capture(scene + (day ? "-day" : "-night"));
            }
        }
        await Script("(() => {const wind=document.querySelector('#wind-speed');wind.value='72';wind.dispatchEvent(new Event('input',{bubbles:true}));})()");
        Check(await Script("window.__cardEngine.state.windSpeed === 72") == "true", "Live simulator wind slider");
        Check(requests == beforePreview, "Simulator must not fetch API on scene/day/wind changes");
        await Script("document.querySelector('#restore-live').click(); window.__cardEngine.start()");
        Check(await Script("document.querySelector('#mode').textContent === '实时' && window.__cardEngine.state.scene === 'partly-cloudy'") == "true", "Restore actual API weather");
        foreach (var (width, height, label) in new[] { (260, 160, "small"), (340, 210, "medium"), (520, 340, "large") })
        {
            window.Width = width; window.Height = height; await Task.Delay(180);
            Check(await Script("(() => {const c=document.querySelector('#atmosphere'),r=c.getBoundingClientRect();return c.width===Math.round(r.width*devicePixelRatio)&&c.height===Math.round(r.height*devicePixelRatio);})()") == "true", "Canvas DPI backing size: " + label);
            Check(await Script("(() => {const c=document.querySelector('.content');return c.scrollWidth<=c.clientWidth && c.scrollHeight<=c.clientHeight;})()") == "true", "Responsive Weather Card fits: " + label);
            Check(await Script($"(getComputedStyle(document.querySelector('#hourly')).display !== 'none') === {(height >= 180 ? "true" : "false")}") == "true", "Forecast height threshold: " + label);
            await Capture(label);
        }
        await Script("Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});document.dispatchEvent(new Event('visibilitychange'));window.__hiddenFrames=window.__cardEngine.frameCount");
        await Task.Delay(150);
        Check(await Script("!window.__cardEngine.running && window.__cardEngine.rafId===0 && window.__cardEngine.frameCount===window.__hiddenFrames") == "true", "Hidden page fully suspends requestAnimationFrame");
        await Script("delete document.hidden;document.dispatchEvent(new Event('visibilitychange'))");
        Check(await Script("window.__cardEngine.running || matchMedia('(prefers-reduced-motion: reduce)').matches") == "true", "Visible page resumes");
        await Script("window.__nativeTimeout=window.setTimeout;window.setTimeout=(callback,delay,...args)=>{const id=window.__nativeTimeout(callback,delay,...args);if(delay>60000)window.__hourTimer={callback,id};return id;};document.querySelector('#city').click();const form=document.querySelector('#city-search');form.elements.query.value='东京';form.requestSubmit()");
        await WaitFor("document.querySelectorAll('#search-results button').length===1", "City search results");
        await Script("document.querySelector('#search-results button').click()");
        await WaitFor("JSON.parse(localStorage.getItem('weather-card.city')).name==='东京' && document.querySelector('#city').textContent.includes('东京') && document.querySelector('#status').textContent.startsWith('更新') && !!window.__hourTimer", "City selection persists and renders");
        await Script("window.__nativeNow=Date.now;Date.now=()=>window.__nativeNow()+3600001;clearTimeout(window.__hourTimer.id);window.__hourTimer.callback()");
        Check(await Script("new Date(document.querySelector('#hourly time').dateTime).getTime()>Date.now() && document.querySelectorAll('#hourly li time[datetime]').length===5") == "true", "Hour boundary retires past forecast slots without waiting fifteen minutes");
        await Script("Date.now=window.__nativeNow;window.setTimeout=window.__nativeTimeout");
        try { await Script("document.querySelector('#close-widget').click()"); }
        catch (Exception) when (!Application.Current.Windows.OfType<WidgetWindow>().Contains(window)) { }
        for (int i = 0; i < 40 && Application.Current.Windows.OfType<WidgetWindow>().Contains(window); i++) await Task.Delay(50);
        Check(!Application.Current.Windows.OfType<WidgetWindow>().Contains(window), "Close button host bridge");
        Check(window.State.City?.Name == "东京", "Weather city changes update grouped native context");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "weather-card-test.txt"), "PASS: manifest sizing, actual ES modules, API data and five hours, seven scenes/day/night, lightning, wind slider without extra requests, restore-live, three sizes, DPI canvas, hidden/resume and city persistence");
    }
}
