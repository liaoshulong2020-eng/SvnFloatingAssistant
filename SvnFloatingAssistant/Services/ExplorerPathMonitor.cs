using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SvnFloatingAssistant.Services;

public sealed class ExplorerPathMonitor
{
    /// <summary>
    /// 获取当前 Explorer 窗口的路径。
    /// 如果窗口中选中了一个子文件夹，返回该子文件夹路径（预览效果）。
    /// 否则返回当前浏览目录路径。
    /// </summary>
    public string? GetCurrentExplorerPath()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(foreground, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                return GetTitlePath(foreground);
        }
        catch { return null; }

        // 通过 Shell COM 获取 Explorer 信息
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;

        try
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var window in shell.Windows())
            {
                try
                {
                    if ((IntPtr)(int)window.HWND != foreground) continue;

                    string currentPath = "";
                    try
                    {
                        var url = (string)window.LocationURL;
                        currentPath = ConvertFileUrlToPath(url) ?? "";
                    }
                    catch { }

                    // 尝试获取选中文件夹（预览功能）
                    try
                    {
                        dynamic? document = window.Document;
                        if (document is not null)
                        {
                            dynamic? selItems = null;
                            try { selItems = document.SelectedItems(); } catch { }

                            if (selItems is not null && selItems.Count == 1)
                            {
                                dynamic? item = selItems.Item(0);
                                if (item is not null)
                                {
                                    bool isFolder = false;
                                    try { isFolder = item.IsFolder; } catch { }
                                    if (isFolder)
                                    {
                                        string? selPath = null;
                                        try { selPath = (string)item.Path; } catch { }
                                        if (!string.IsNullOrWhiteSpace(selPath))
                                        {
                                            if (selPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                                                selPath = new Uri(selPath).LocalPath;
                                            if (Directory.Exists(selPath))
                                                return selPath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // 没有选中文件夹，返回当前目录
                    if (!string.IsNullOrWhiteSpace(currentPath))
                        return currentPath;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 兜底：通过窗口标题获取路径（适用于第三方文件管理器）。
    /// </summary>
    private static string? GetTitlePath(IntPtr hwnd)
    {
        var title = GetWindowTitle(hwnd);
        if (Directory.Exists(title)) return title;

        // 窗口标题可能是 "文件夹名 - 文件资源管理器" 格式
        var parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && Directory.Exists(parts[0]))
            return parts[0];

        return null;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string? ConvertFileUrlToPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return null;
        try { return new Uri(url).LocalPath; } catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
