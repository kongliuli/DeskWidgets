using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;

namespace DeskWidgets;
internal static class NotesLayoutChecks
{
    internal static async Task Run(WidgetManager manager)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static Win32Interop.Rect Bounds(Window window) { Check(Win32Interop.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect), "Native window bounds"); return rect; }
        var work = new Win32Interop.Rect { Left = -1920, Top = -100, Right = 0, Bottom = 800 };
        var sizes = new[] { new Win32Interop.Rect { Right = 300, Bottom = 500 }, new Win32Interop.Rect { Right = 240, Bottom = 500 } };
        var plan = DesktopLayout.Plan(work, sizes);
        Check(plan[0].Right == -12 && plan[0].Top == -88 && plan[1].Right == -324 && plan[1].Top == -88, "Right-to-left packing supports negative physical monitor coordinates and wraps columns");
        bool rejected = false; try { DesktopLayout.Plan(work, [new Win32Interop.Rect { Right = 5000, Bottom = 50 }]); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Oversized arrangement rejected before any position mutation");
        async Task<(WidgetWindow Window, Microsoft.Web.WebView2.Core.CoreWebView2 Core)> Open()
        {
            manager.Open(manager.Scan().Single(d => d.Directory == "notes")); var window = manager.OpenWindows.Last(w => w.State.WidgetName == "notes");
            await Application.Current.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await window.Initialization;
            var core = ((WebView2CompositionControl)window.FindName("Browser")).CoreWebView2;
            for (int i = 0; i < 100; i++) { if (await core.ExecuteScriptAsync("document.querySelector('.widget')?.dataset.ready==='true' && !!document.querySelector('#save-history')") == "true") return (window, core); await Task.Delay(50); }
            throw new InvalidOperationException("Notes history UI did not initialize");
        }
        var (note, core) = await Open();
        await core.ExecuteScriptAsync("document.querySelector('#note').value='<script>只读桌面便签</script>\\n第二行';document.querySelector('#save-history').click()"); await Task.Delay(130);
        Check(await core.ExecuteScriptAsync("document.querySelector('#history-count').textContent==='1'") == "true", "History saves via host and broadcasts to notes UI");
        await core.ExecuteScriptAsync("chrome.webview.postMessage({type:'saveNoteHistory',text:'x'.repeat(50001)})"); await Task.Delay(80);
        Check(await core.ExecuteScriptAsync("document.querySelector('#history-count').textContent==='1' && document.querySelector('#status').textContent.includes('50000')") == "true", "Host rejects oversized history without deleting saved notes");
        await core.ExecuteScriptAsync("document.querySelector('#open-history').click();document.querySelector('.drag-note').dispatchEvent(new PointerEvent('pointerdown',{button:0,pointerId:16,bubbles:true}));document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))"); await Task.Delay(80);
        Check(manager.Notes.Windows.Count == 0 && !Application.Current.Windows.OfType<DesktopNoteWindow>().Any(), "Escape cancels drag preview without persisting a desktop note");
        await core.ExecuteScriptAsync("document.querySelector('.drag-note').dispatchEvent(new PointerEvent('pointerdown',{button:0,pointerId:15,bubbles:true}))"); await Task.Delay(50);
        note.State.Locked = true; note.ApplyLock(); await Task.Delay(50);
        Check(!Application.Current.Windows.OfType<DesktopNoteWindow>().Any(), "Locking source note cancels native desktop preview");
        note.State.Locked = false; note.ApplyLock(); await Task.Delay(50);
        await core.ExecuteScriptAsync("document.querySelector('#open-history').click();document.querySelector('.drag-note').dispatchEvent(new PointerEvent('pointerdown',{button:0,pointerId:17,bubbles:true}));document.querySelector('.drag-note').dispatchEvent(new PointerEvent('pointerup',{button:0,pointerId:17,bubbles:true}))"); await Task.Delay(120);
        Check(manager.Notes.Windows.Count == 1, "History drag bridge creates a desktop note");
        var desktop = manager.Notes.Windows.Single(); nint handle = new WindowInteropHelper(desktop).Handle;
        Check((Win32Interop.GetLong(handle, Win32Interop.ExStyle) & 0x080000A0) == 0x080000A0 && Win32Interop.SendMessage(handle, 0x84, 0, 0) == -1, "Desktop note is transparent to hit testing, tool window and nonactivating");
        Check(Win32Interop.GetLong(handle, Win32Interop.Owner) == Win32Interop.DesktopOwner() && !desktop.Topmost, "Desktop note has desktop owner and bottom layer");
        string historyPath = Path.Combine(AppContext.BaseDirectory, "notes-history.json");
        var saved = JsonSerializer.Deserialize<SavedNote[]>(File.ReadAllText(historyPath))!;
        Check(saved.Length == 1 && saved[0].Pinned && saved[0].Text.Contains("<script>"), "Pinned content persists as plain text, not HTML");
        using (var recovered = new NotesService(manager, App.CreateNoteHistoryRepository())) { recovered.Restore(); Check(recovered.Windows.Count == 1, "Desktop note restores from persisted history"); }
        var original = manager.OpenWindows.Select(w => (Window: w, Rect: Bounds(w))).ToArray();
        manager.ShrinkAll(); await Task.Delay(100);
        foreach (var item in original)
        {
            var rect = Bounds(item.Window);
            Check(item.Window.Width == item.Window.MinWidth && item.Window.Height == item.Window.MinHeight && rect.Left == item.Rect.Left && rect.Top == item.Rect.Top, "Shrink all uses plugin minimum and preserves positions");
        }
        var arrangedWindows = manager.OpenWindows.Cast<Window>().Concat(manager.Notes.Windows).ToArray(); var before = arrangedWindows.Select(Bounds).ToArray();
        manager.ArrangeFromRight(); await Task.Delay(100);
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        for (int i = 0; i < arrangedWindows.Length; i++)
        {
            var after = Bounds(arrangedWindows[i]); Check(after.Right - after.Left == before[i].Right - before[i].Left && after.Bottom - after.Top == before[i].Bottom - before[i].Top, "Right arrangement preserves every native size");
            Check(after.Left >= screen.Left && after.Right <= screen.Right && after.Top >= screen.Top && after.Bottom <= screen.Bottom, "Arrangement stays in active screen work area");
        }
        note.Close(); Check(manager.Notes.Windows.Count == 1, "Closing writer keeps the desktop note");
        var (reopened, newCore) = await Open(); await newCore.ExecuteScriptAsync("document.querySelector('#open-history').click()"); await Task.Delay(100);
        Check(await newCore.ExecuteScriptAsync("document.querySelector('.drag-note').textContent==='关闭桌面便签'") == "true", "New notes instance can manage existing desktop notes");
        await newCore.ExecuteScriptAsync("document.querySelector('.drag-note').click()"); await Task.Delay(80);
        Check(manager.Notes.Windows.Count == 0 && JsonSerializer.Deserialize<SavedNote[]>(File.ReadAllText(historyPath))![0].Pinned == false, "Only notes action closes the desktop note while retaining history"); reopened.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "notes-layout-test.txt"), "PASS: history persistence/plain text, drag-out bridge, native click-through/tool/noactivate/desktop owner, restore, cross-instance management, close retains history, physical right packing/overflow/negative monitor coordinates, minimum sizes and position/size invariants");
    }
}
