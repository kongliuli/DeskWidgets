using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;

internal static class TelemetryChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        Check(SystemMetricsSampler.CpuPercent(100, 200, 100, 150, 300, 200) == 75, "CPU kernel includes idle; total is already across processors");
        Check(SystemMetricsSampler.AggregateGpu(new[] { new KeyValuePair<string, double>("pid_1_luid_a_phys_0_eng_0_engtype_3D", 40), new("pid_2_luid_a_phys_0_eng_0_engtype_3D", 35), new("pid_3_luid_a_phys_0_eng_1_engtype_Copy", 60) }) == 75, "GPU sums processes within the same engine and takes the busiest engine");
        Check(SystemMetricsSampler.AggregateGpu(new[] { new KeyValuePair<string, double>("pid_1_luid_a_phys_0_eng_0_engtype_3D", 0) }) == 0, "Valid idle GPU is zero");
        Check(SystemMetricsSampler.AggregateGpu(Array.Empty<KeyValuePair<string, double>>()) == null, "Unavailable GPU is not fake zero");
        Check(AudioEndpoint.ToDbfs(0) == null && Math.Abs(AudioEndpoint.ToDbfs(.5)!.Value + 6.0205999) < .001, "Peak amplitude to dBFS");
        var native = await Task.Run(async () =>
        {
            using var metrics = new SystemMetricsSampler(); metrics.Read(MetricChannels.Cpu | MetricChannels.Gpu | MetricChannels.Memory);
            await Task.Delay(1100);
            var sample = metrics.Read(MetricChannels.Cpu | MetricChannels.Gpu | MetricChannels.Memory);
            using var audio = new AudioEndpoint(); var audioSample = audio.Read(true);
            return new { metrics = sample, audio = audioSample };
        });
        Check(native.metrics.CpuPercent is >= 0 and <= 100 && native.metrics.TotalMemoryBytes > 0 && native.metrics.UsedMemoryBytes <= native.metrics.TotalMemoryBytes, "Live native CPU/RAM sampling");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "telemetry-native.json"), JsonSerializer.Serialize(native, new JsonSerializerOptions { WriteIndented = true }));
        async Task<(WidgetWindow, CoreWebView2)> Open(string directory)
        {
            manager.Open(manager.Scan().Single(d => d.Directory == directory));
            var window = Application.Current.Windows.OfType<WidgetWindow>().Single(w => w.State.WidgetName == directory);
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await window.Initialization;
            var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
            for (int i = 0; i < 100; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget')?.dataset.ready === 'true'") == "true") return (window, core); await Task.Delay(50); }
            throw new InvalidOperationException(directory + " did not initialize");
        }
        var (monitor, monitorCore) = await Open("system-monitor");
        async Task<string> Script(string code) => await monitorCore.ExecuteScriptAsync(code);
        for (int i = 0; i < 140; i++) { if (await Script("document.querySelector('#bars .bar-label span:last-child')?.textContent.endsWith('%')") == "true") break; await Task.Delay(50); }
        Check(await Script("document.querySelector('#bars .bar-label span:last-child')?.textContent.endsWith('%')") == "true", "Live CPU values reach the monitor UI after baseline sampling");
        Check(manager.Telemetry.SubscriberCount == 1, "Monitor subscribes on demand");
        foreach (string layout in new[] { "bars", "radar", "rings", "lines" })
        {
            await Script($"document.querySelector('[data-layout={layout}]').click()"); await Task.Delay(80);
            Check(await Script($"document.querySelector('.widget').dataset.layout==='{layout}'") == "true", "System chart layout: " + layout);
            using var image = File.Create(Path.Combine(AppContext.BaseDirectory, "system-monitor-" + layout + ".png"));
            await monitorCore.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        }
        await Script("document.querySelector('[data-action=settings]').click()"); await Task.Delay(80);
        await Script("(() => {const f=document.querySelector('dialog form');f.elements.gpu.checked=false;f.elements.layout.value='radar';f.requestSubmit();})()"); await Task.Delay(80);
        Check(await Script("document.querySelector('.widget').dataset.layout==='bars' && document.querySelectorAll('#bars progress').length===2") == "true", "Disabled metric and two-axis radar fallback");
        await Script("document.querySelector('#pause').click()"); await Task.Delay(80);
        Check(manager.Telemetry.SubscriberCount == 0, "Pause releases monitor subscription");
        await Script("document.querySelector('#pause').click()"); await Task.Delay(80);
        Check(manager.Telemetry.SubscriberCount == 1, "Resume monitor subscription");
        await Script("document.querySelector('[data-action=settings]').click()"); await Task.Delay(50);
        await Script("(() => {const f=document.querySelector('dialog form');f.elements.gpu.checked=true;f.requestSubmit();})()");
        monitor.Width = 760; monitor.Height = 520; await Task.Delay(180);
        Check(await Script("document.querySelector('.widget').dataset.dashboard==='true' && !document.querySelector('#dashboard').hidden && [...document.querySelectorAll('#dashboard canvas')].every(c=>!c.hidden && c.width>0 && c.height>0) && document.querySelectorAll('#dashboard-bars progress').length===3") == "true", "Large resource card shows four charts from the same subscription");
        Check(manager.Telemetry.SubscriberCount == 1, "Four-chart overview does not add samplers");
        for (int attempt = 0; attempt < 80; attempt++) { if (await Script("document.querySelector('#dashboard-bars .bar-label span:last-child')?.textContent.endsWith('%')") == "true") break; await Task.Delay(50); }
        Check(await Script("document.querySelector('#dashboard-bars .bar-label span:last-child')?.textContent.endsWith('%')") == "true", "Live CPU sample reaches the four-chart overview");
        for (int attempt = 0; attempt < 40; attempt++) { if (await Script("document.querySelectorAll('#dashboard-bars .bar-label span:last-child')[1]?.textContent.endsWith('%')") == "true") break; await Task.Delay(100); }
        using (var overview = File.Create(Path.Combine(AppContext.BaseDirectory, "system-monitor-overview.png"))) await monitorCore.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, overview);
        monitor.Width = 260; monitor.Height = 180; await Task.Delay(100);
        Check(await Script("document.querySelector('.content').scrollHeight<=document.querySelector('.content').clientHeight") == "true", "Compact system chart layout");
        monitor.Close();
        var (audioWindow, audioCore) = await Open("audio-control");
        for (int i = 0; i < 100; i++) { if (await audioCore.ExecuteScriptAsync("!document.querySelector('#audio-status').textContent.includes('预热')") == "true") break; await Task.Delay(50); }
        Check(manager.Telemetry.SubscriberCount == 1, "Audio shares one on-demand service");
        await audioCore.ExecuteScriptAsync("window.__audioCommand=null;window.chrome.webview.addEventListener('message',({data})=>{if(data.type==='audioCommandPreview')window.__audioCommand=data;});document.querySelector('#volume-slider').value='42';document.querySelector('#volume-slider').dispatchEvent(new Event('change'))");
        await Task.Delay(150);
        Check(await audioCore.ExecuteScriptAsync("window.__audioCommand?.volume===42") == "true", "Volume bridge validates commands without touching system volume in test mode");
        await audioCore.ExecuteScriptAsync("document.querySelector('#mute').click()"); await Task.Delay(80);
        Check(await audioCore.ExecuteScriptAsync("typeof window.__audioCommand?.muted==='boolean'") == "true", "Mute bridge command preview");
        using (var image = File.Create(Path.Combine(AppContext.BaseDirectory, "audio-control.png"))) await audioCore.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        audioWindow.Width = 260; audioWindow.Height = 160; await Task.Delay(100);
        Check(await audioCore.ExecuteScriptAsync("document.querySelector('.content').scrollHeight<=document.querySelector('.content').clientHeight") == "true", "Compact audio layout");
        audioWindow.State.Locked = true; audioWindow.ApplyLock(); await Task.Delay(80);
        Check(await audioCore.ExecuteScriptAsync("getComputedStyle(document.querySelector('.volume-controls')).display==='none' && [...document.querySelectorAll('.tools button')].filter(button=>getComputedStyle(button).display!=='none').length===1") == "true", "Locked audio hides controls and leaves only unlock");
        audioWindow.Close();
        Check(manager.Telemetry.SubscriberCount == 0, "Closing telemetry widgets releases all subscriptions");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "telemetry-test.txt"), "PASS: CPU/GPU math, live CPU/RAM/GPU attempt, readonly Core Audio, four chart layouts, metric switch, radar fallback, pause/resume, compact layout, safe volume-command preview, lock and subscription cleanup");
    }
}
