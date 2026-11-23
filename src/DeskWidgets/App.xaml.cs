using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using DeskWidgets.Infrastructure;

namespace DeskWidgets;

public partial class App : Application
{
    internal static bool IsSelfTest { get; private set; }
    internal static bool IsExiting => Current is App app && app.exiting;
    private Mutex? mutex;
    private WidgetManager? manager;
    private TrayService? tray;
    private bool exiting;
    private Windows.System.DispatcherQueueController? compositionQueue;
    internal static async Task ShutdownCompositionAsync()
    {
        var app = (App)Current;
        var queue = app.compositionQueue;
        app.compositionQueue = null;
        // Release closed WinRT composition graphs before their STA dispatcher stops pumping.
        await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); });
        await app.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (queue != null) await queue.ShutdownQueueAsync();
    }
    internal static async void ExitGracefully()
    {
        var app = (App)Current;
        if (app.exiting) return;
        app.exiting = true;
        if (app.manager != null) await app.manager.WaitForWindowsAsync();
        app.manager?.Dispose();
        await app.Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await ShutdownCompositionAsync();
        app.Shutdown();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IsSelfTest = Array.Exists(e.Args, arg => arg == "--self-test");
        if (IsSelfTest && (!Array.Exists(e.Args, arg => arg == "--isolated-test") || File.Exists(Path.Combine(AppContext.BaseDirectory, "config.json")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "notes-history.json")))) { Shutdown(2); return; }
        string mutexName = IsSelfTest ? @"Local\DeskWidgets.Test." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..16] : @"Local\DeskWidgets.Host";
        mutex = new Mutex(true, mutexName, out bool first);
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { Report(args.Exception); args.Handled = true; };
        try
        {
            // The UI-thread queue outlives every WebView; individual widgets must not own it.
            compositionQueue = Win32Interop.CreateCompositionQueue();
            if (IsSelfTest) { HostChecks.Run(); return; }
            manager = CreateWidgetManager();
            tray = new TrayService(manager);
            manager.Restore();
        }
        catch (Exception error) { Report(error); Shutdown(1); }
    }
    internal static WidgetManager CreateWidgetManager()
    {
        string root = AppContext.BaseDirectory;
        string widgetsRoot = Path.Combine(root, "widgets");
        return new WidgetManager(
            widgetsRoot,
            new FileWidgetCatalog(widgetsRoot),
            new JsonWidgetLayoutRepository(Path.Combine(root, "config.json")),
            CreateNoteHistoryRepository());
    }

    internal static JsonNoteHistoryRepository CreateNoteHistoryRepository() =>
        new(Path.Combine(AppContext.BaseDirectory, "notes-history.json"));

    internal static void Report(Exception error)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "DeskWidgets.log"), $"{DateTimeOffset.Now:O} {error}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        MessageBox.Show(error.Message, "DeskWidgets", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        manager?.Dispose();
        tray?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
