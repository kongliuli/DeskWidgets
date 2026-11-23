using System;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DeskWidgets;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly WidgetManager manager;
    private ComponentCenter? center;
    private void ShowCenter() { if (center == null) { center = new ComponentCenter(manager); center.Closed += (_, _) => center = null; } center.Show(); center.WindowState = System.Windows.WindowState.Normal; center.Activate(); }
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public TrayService(WidgetManager manager)
    {
        this.manager = manager;
        trayIcon = new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "deskwidgets.ico"), new System.Drawing.Size(32, 32));
        icon = new Forms.NotifyIcon { Text = "DeskWidgets · 双击打开组件中心", Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        menu.Opening += (_, _) => Populate();
        icon.DoubleClick += (_, _) => ShowCenter();
    }
    private static Forms.ToolStripMenuItem Item(string title, Action action, bool check = false)
    {
        var item = new Forms.ToolStripMenuItem(title) { Checked = check };
        item.Click += (_, _) => { try { action(); } catch (Exception error) { App.Report(error); } };
        return item;
    }
    private void Populate()
    {
        while (menu.Items.Count > 0) { var old = menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
        menu.Items.Add(Item("组件中心 / 快速启动", ShowCenter));
        var load = new Forms.ToolStripMenuItem("加载挂件");
        foreach (var definition in manager.Catalog())
        {
            var widget = new Forms.ToolStripMenuItem(definition.Name);
            widget.DropDownItems.Add(Item("打开新实例", () => manager.OpenFromCatalog(definition)));
            widget.DropDownItems.Add(Item("唤醒已有实例", () => manager.Wake(definition)));
            load.DropDownItems.Add(widget);
        }
        if (load.DropDownItems.Count == 0) load.DropDownItems.Add(new Forms.ToolStripMenuItem("未找到有效挂件") { Enabled = false });
        menu.Items.Add(load);
        menu.Items.Add(Item("锁定所有挂件位置", () => manager.LockAll(!manager.AllLocked), manager.AllLocked));
        menu.Items.Add(Item("所有挂件置顶 / 沉底", () => manager.PinAll(!manager.AllTopmost), manager.AllTopmost));
        menu.Items.Add(Item("从屏幕右侧排列（保留大小）", manager.ArrangeFromRight));
        menu.Items.Add(Item("全部缩到最小", manager.ShrinkAll));
        menu.Items.Add(Item("重载所有挂件", manager.ReloadAll));
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        bool startup = key?.GetValue("DeskWidgets") is string;
        menu.Items.Add(Item("开机自启", () =>
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (startup) run.DeleteValue("DeskWidgets", false);
            else run.SetValue("DeskWidgets", $"\"{Environment.ProcessPath}\"");
        }, startup));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("退出", App.ExitGracefully));
    }
    public void Dispose() { center?.Close(); icon.Visible = false; icon.Dispose(); trayIcon.Dispose(); menu.Dispose(); }
}
