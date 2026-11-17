using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;
internal static class UtilityChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        foreach (string invalid in new[] { "cmd.exe", "javascript:alert(1)", "file:///C:/Windows", @"C:\Windows\System32\cmd.exe", "https://user:password@example.com", @"\\server\share" })
        {
            bool rejected = false; try { WorkspaceLauncher.Validate(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Reject unsafe workspace target: " + invalid);
        }
        Check(WorkspaceLauncher.Validate("https://example.com") == "https://example.com/", "Allow explicit HTTPS URL");
        Check(Directory.Exists(WorkspaceLauncher.Validate(AppContext.BaseDirectory)), "Allow existing local folder");
        async Task<(WidgetWindow Window, CoreWebView2 Core)> Open(string directory)
        {
            manager.Open(manager.Scan().Single(d => d.Directory == directory));
            var window = manager.OpenWindows.Last(w => w.State.WidgetName == directory);
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await window.Initialization;
            var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
            for (int i = 0; i < 100; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget')?.dataset.ready==='true'") == "true") return (window, core); await Task.Delay(50); }
            throw new InvalidOperationException(directory + " module initialization failed");
        }
        var (note, nc) = await Open("notes");
        await nc.ExecuteScriptAsync("document.querySelector('#note').value='<script>plain text</script>';document.querySelector('#note').dispatchEvent(new Event('input'))"); await Task.Delay(450);
        Check(await nc.ExecuteScriptAsync("JSON.parse(localStorage.getItem('note'))==='<script>plain text</script>'") == "true", "Note autosaves plain text");
        var (note2, nc2) = await Open("notes");
        Check(await nc2.ExecuteScriptAsync("document.querySelector('#note').value===''") == "true", "Notes isolated per instance"); note2.Close();
        note.State.Locked = true; note.ApplyLock(); await Task.Delay(70);
        Check(await nc.ExecuteScriptAsync("document.querySelector('#note').readOnly && document.querySelector('#note').disabled") == "true", "Note locked readonly");
        note.Close();
        var (countdown, cc) = await Open("countdown");
        await cc.ExecuteScriptAsync("document.querySelector('#edit').click();const f=document.querySelector('dialog.editor form');f.elements.title.value='发布';f.elements.date.value='2030-01-02T12:00';f.requestSubmit()"); await Task.Delay(80);
        Check(await cc.ExecuteScriptAsync("document.querySelector('#event-title').textContent==='发布' && document.querySelector('#remaining').textContent.includes('天')") == "true", "Local countdown saved and rendered");
        using (var image = File.Create(Path.Combine(AppContext.BaseDirectory, "countdown.png"))) await cc.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        countdown.Close();
        var (focus, fc) = await Open("focus-timer");
        await fc.ExecuteScriptAsync("document.querySelector('#toggle').click()"); await Task.Delay(100);
        Check(await fc.ExecuteScriptAsync("JSON.parse(localStorage.getItem('focus')).end>Date.now()") == "true", "Focus uses persisted wall clock deadline");
        await fc.ExecuteScriptAsync("document.querySelector('#toggle').click()");
        Check(await fc.ExecuteScriptAsync("JSON.parse(localStorage.getItem('focus')).end===null") == "true", "Focus pause persists remaining time"); focus.Close();
        var (launcher, lc) = await Open("workspace-launcher");
        await lc.ExecuteScriptAsync("document.querySelector('#add').click();const f=document.querySelector('dialog.editor form');f.elements.name.value='示例';f.elements.path.value='https://example.com';f.requestSubmit()"); await Task.Delay(60);
        await lc.ExecuteScriptAsync("document.querySelector('#targets button').click()"); await Task.Delay(80);
        Check(await lc.ExecuteScriptAsync("document.querySelector('#status').textContent.includes('测试未打开')") == "true", "Workspace bridge validates without launching external apps in test"); launcher.Close();
        var (media, mc) = await Open("media-player");
        for (int i = 0; i < 100; i++) { if (await mc.ExecuteScriptAsync("!document.querySelector('#status').textContent.includes('等待')") == "true") break; await Task.Delay(50); }
        string status = await mc.ExecuteScriptAsync("document.querySelector('#status').textContent");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "media-native.json"), status);
        Check(!status.Contains("等待"), "Real media API completes or displays explicit unavailable reason");
        await mc.ExecuteScriptAsync("globalThis.__media=null;chrome.webview.addEventListener('message',({data})=>{if(data.type==='mediaCommandPreview')globalThis.__media=data;});chrome.webview.postMessage({type:'mediaCommand',command:'toggle'})"); await Task.Delay(70);
        Check(await mc.ExecuteScriptAsync("globalThis.__media?.command==='toggle'") == "true", "Safe media command preview"); media.Close();
        var center = new ComponentCenter(manager); center.Show(); await Task.Delay(100);
        Check(center.IsVisible && center.Title.Contains("组件中心"), "Native component center flow/search/filter/multiple-instance launch/three-column layout");
        var cards = (System.Windows.Controls.WrapPanel)center.FindName("ComponentFlow");
        var search = (System.Windows.Controls.TextBox)center.FindName("ComponentSearch");
        Check(cards.Children.Count == manager.Catalog().Count, "Unified flow includes running and unopened plugins");
        var filters = (System.Windows.Controls.StackPanel)center.FindName("ComponentFilters");
        filters.Children.OfType<System.Windows.Controls.Button>().Single(b => (string)b.Content == "未开启").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(cards.Children.Count == manager.Catalog().Count - manager.OpenWindows.Select(w => WidgetKinds.Family(w.State.WidgetName)).Distinct().Count(), "Unopened filter uses actual open instances");
        filters.Children.OfType<System.Windows.Controls.Button>().Single(b => (string)b.Content == "全部").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        search.Text = "轻量便签";
        Check(cards.Children.Count == 1, "Chinese display-name search");
        for (int count = 1; count <= 2; count++)
        {
            var card = (System.Windows.Controls.Border)cards.Children[0];
            var button = ((System.Windows.Controls.StackPanel)card.Child).Children.OfType<System.Windows.Controls.Button>().Last();
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await manager.WaitForWindowsAsync(); await Task.Delay(500);
            Check(manager.OpenWindows.Count(w => w.State.WidgetName == "notes") == count, "Flow card starts independent instance " + count);
        }
        search.Text = ""; center.UpdateLayout(); await Task.Delay(100);
        var firstCard = (System.Windows.Controls.Border)cards.Children[0];
        var thirdCard = (System.Windows.Controls.Border)cards.Children[2];
        Check(Math.Abs(firstCard.TranslatePoint(new Point(), cards).Y - thirdCard.TranslatePoint(new Point(), cards).Y) < 1, "Three columns fit with scrollbar at default window width");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)((FrameworkElement)center.Content).ActualWidth, (int)((FrameworkElement)center.Content).ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new System.Windows.Media.VisualBrush((System.Windows.Media.Visual)center.Content), null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var image = File.Create(Path.Combine(AppContext.BaseDirectory, "component-center.png"))) encoder.Save(image);
        foreach (var openNote in manager.OpenWindows.Where(w => w.State.WidgetName == "notes").ToArray()) openNote.Close();
        center.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "utility-test.txt"), "PASS: 5 utility plugins initialize, isolated plain notes, lock, countdown, focus deadline/pause, workspace validation + safe bridge, real media API attempt + safe command, component center flow/search/filter/multiple-instance launch/three-column layout");
    }
}
