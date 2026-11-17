using System;
using System.Diagnostics;
using System.IO;

namespace DeskWidgets;
internal static class WorkspaceLauncher
{
    internal static string Validate(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 2048 || target.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("入口路径无效");
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        { if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("网址无效"); return uri.AbsoluteUri; }
        if (!Path.IsPathFullyQualified(target) || target.StartsWith(@"\\", StringComparison.Ordinal) || target.StartsWith(@"\\?\", StringComparison.Ordinal)) throw new ArgumentException("请使用本机完整文件夹路径、.lnk 或 HTTP(S) 网址");
        string path = Path.GetFullPath(target);
        if (!Directory.Exists(path) && !(File.Exists(path) && Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("文件夹或 .lnk 快捷方式不存在");
        return path;
    }
    internal static void Open(WidgetWindow window, string target)
    {
        try
        {
            string validated = Validate(target);
            if (!App.IsSelfTest) Process.Start(new ProcessStartInfo(validated) { UseShellExecute = true });
            window.PostTelemetry(new { type = "workspaceResult", message = App.IsSelfTest ? "已验证入口（测试未打开）" : "已打开入口" });
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { window.PostTelemetry(new { type = "workspaceResult", message = error.Message }); }
    }
}
