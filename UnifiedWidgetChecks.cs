using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;
internal static class UnifiedWidgetChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        var catalog = manager.Catalog();
        Check(catalog.Count == 11 && catalog.Count(d => WidgetKinds.Family(d.Directory) == "world-clock") == 1 && catalog.Count(d => WidgetKinds.Family(d.Directory) == "weather-card") == 1, "Clock aliases and legacy weather become two catalog families");
        Check(WidgetKinds.Context(new WidgetState { WidgetName = "clock-beijing" }) == "北京" && WidgetKinds.Context(new WidgetState { WidgetName = "clock-new-york" }) == "纽约 · 美东", "Legacy region fallback preserves old config without migration");
        await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await manager.WaitForWindowsAsync();
        foreach (var legacy in manager.OpenWindows.Where(w => w.State.WidgetName is "clock-beijing" or "clock-new-york" or "clock-los-angeles"))
        {
            var legacyCore = ((WebView2CompositionControl)legacy.FindName("Browser")).CoreWebView2;
            string expected = JsonSerializer.Serialize(WidgetKinds.DefaultZone(legacy.State.WidgetName));
            for (int i = 0; i < 100; i++) { if (await legacyCore.ExecuteScriptAsync("document.querySelector('.zone-picker')?.value===" + expected) == "true") break; await Task.Delay(50); }
            Check(await legacyCore.ExecuteScriptAsync("document.querySelector('.zone-picker')?.value===" + expected) == "true", "Legacy profile retains region while loading unified clock source: " + legacy.State.WidgetName);
        }
        using (var json = JsonDocument.Parse("{\"results\":[{\"name\":\"京都\",\"latitude\":35,\"longitude\":135,\"country\":\"日本\"},{\"name\":\"bad\",\"latitude\":999,\"longitude\":0},{\"name\":\"bad\",\"latitude\":\"x\",\"longitude\":0},7]}")) Check(WidgetCreateDialog.ParseCities(json.RootElement).Length == 1, "Geocoding parser validates type and coordinate ranges");
        WidgetState Choose(string directory, int index)
        {
            var dialog = new WidgetCreateDialog(catalog.Single(d => d.Directory == directory));
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                ((ComboBox)dialog.FindName("RegionChoice")).SelectedIndex = index;
                ((Button)dialog.FindName("CreateInstance")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
            Check(dialog.ShowDialog() == true && dialog.Selection != null, "Native create dialog accepts selected region"); return dialog.Selection!;
        }
        async Task<(WidgetWindow Window, Microsoft.Web.WebView2.Core.CoreWebView2 Core)> Open(WidgetState state)
        {
            manager.Open(catalog.Single(d => d.Directory == state.WidgetName), state); var window = manager.OpenWindows.Single(w => w.State.Id == state.Id);
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await window.Initialization;
            var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
            for (int i = 0; i < 160; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget,.weather-card')?.dataset.ready==='true'") == "true") return (window, core); await Task.Delay(50); }
            throw new InvalidOperationException("Selected region instance failed to load");
        }
        var clockState = Choose("world-clock", 3); Check(clockState.TimeZone == "America/Los_Angeles", "New clock region selected before opening");
        var (clock, cc) = await Open(clockState);
        Check(await cc.ExecuteScriptAsync("document.querySelector('.zone-picker').value==='America/Los_Angeles' && JSON.parse(localStorage.getItem('settings')).timeZone==='America/Los_Angeles'") == "true", "Selected timezone seeds only the new profile");
        await cc.ExecuteScriptAsync("document.querySelector('.zone-picker').value='Asia/Shanghai';document.querySelector('.zone-picker').dispatchEvent(new Event('change'))"); await Task.Delay(100);
        Check(clock.State.TimeZone == "Asia/Shanghai" && await cc.ExecuteScriptAsync("document.querySelector('.scene').classList.contains('beijing')") == "true", "Header dropdown updates time zone, background and native context");
        clock.State.Locked = true; clock.ApplyLock(); await Task.Delay(60);
        Check(await cc.ExecuteScriptAsync("document.querySelector('.zone-picker').disabled && getComputedStyle(document.querySelector('.zone-picker')).display==='none' && getComputedStyle(document.querySelector('.city-label')).display!=='none'") == "true", "Locked clock keeps readonly region label"); clock.State.Locked = false; clock.ApplyLock();
        string beforeReload = await cc.ExecuteScriptAsync("performance.timeOrigin");
        clock.Reload(); await clock.Reloading;
        for (int i = 0; i < 80; i++) { if (await cc.ExecuteScriptAsync("performance.timeOrigin") != beforeReload && await cc.ExecuteScriptAsync("document.querySelector('.zone-picker')?.value==='Asia/Shanghai'") == "true") break; await Task.Delay(50); }
        Check(await cc.ExecuteScriptAsync("document.querySelector('.zone-picker')?.value==='Asia/Shanghai'") == "true", "Saved selection wins over original launch seed on reload");
        var weatherState = Choose("weather-card", 8); Check(weatherState.City?.Name == "东京", "New weather instance selects city before opening");
        var (weather, wc) = await Open(weatherState);
        Check(await wc.ExecuteScriptAsync("JSON.parse(localStorage.getItem('weather-card.city')).name==='东京' && document.querySelector('#city').textContent.includes('东京')") == "true", "Weather city seeds isolated profile");
        var nanjing = new WidgetState { WidgetName = "weather-card", Width = 340, Height = 210, City = new WidgetCity("南京",32.0603,118.7969) };
        var (weather2, wc2) = await Open(nanjing);
        Check(await wc2.ExecuteScriptAsync("JSON.parse(localStorage.getItem('weather-card.city')).name==='南京'") == "true" && await wc.ExecuteScriptAsync("JSON.parse(localStorage.getItem('weather-card.city')).name==='东京'") == "true", "Weather instances retain independent cities");
        weather.Close(); weather2.Close(); clock.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "unified-widget-test.txt"), "PASS: 11 catalog families, legacy timezone fallback, native timezone/city creation dialogs, geocoding validation, isolated bootstrap preferences, header timezone/background/native context, lock label, reload persistence and weather multi-instance city isolation");
    }
}
