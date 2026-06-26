using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using WpfPoint = System.Windows.Point;

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
        var explorerWindow = GetExplorerWindowUnderCursor();
        var fallbackWindow = GetForegroundWindow();
        if (explorerWindow == IntPtr.Zero && IsExplorerWindow(fallbackWindow))
        {
            explorerWindow = fallbackWindow;
        }

        if (explorerWindow == IntPtr.Zero)
        {
            return fallbackWindow == IntPtr.Zero ? null : GetTitlePath(fallbackWindow);
        }

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
                    if (new IntPtr(Convert.ToInt64(window.HWND)) != explorerWindow) continue;

                    string currentPath = "";
                    try
                    {
                        var url = (string)window.LocationURL;
                        currentPath = ConvertFileUrlToPath(url) ?? "";
                    }
                    catch { }

                    var hoveredPath = GetHoveredItemPath(currentPath, explorerWindow);
                    if (hoveredPath is not null)
                    {
                        return hoveredPath;
                    }

                    // 优先获取单个选中项，用选中项自己的 SVN 信息做预览。
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
                                    string? selPath = null;
                                    try { selPath = (string)item.Path; } catch { }
                                    selPath = NormalizeShellPath(selPath);
                                    if (IsExistingFileSystemPath(selPath))
                                    {
                                        return selPath;
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

    private static IntPtr GetExplorerWindowUnderCursor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return IntPtr.Zero;
        }

        var window = WindowFromPoint(cursor);
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = GetAncestor(window, 2);
        if (root == IntPtr.Zero)
        {
            root = window;
        }

        return IsExplorerWindow(root) ? root : IntPtr.Zero;
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    private static string? GetHoveredItemPath(string currentDirectory, IntPtr foreground)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory) || !Directory.Exists(currentDirectory))
        {
            return null;
        }

        if (!GetCursorPos(out var cursor) || !GetWindowRect(foreground, out var rect))
        {
            return null;
        }

        if (cursor.X < rect.Left || cursor.X > rect.Right || cursor.Y < rect.Top || cursor.Y > rect.Bottom)
        {
            return null;
        }

        try
        {
            var element = AutomationElement.FromPoint(new WpfPoint(cursor.X, cursor.Y));
            for (var depth = 0; element is not null && depth < 8; depth++)
            {
                var controlType = element.Current.ControlType;
                if (controlType == ControlType.ListItem ||
                    controlType == ControlType.DataItem ||
                    controlType == ControlType.TreeItem)
                {
                    var candidate = ResolveChildPath(currentDirectory, element.Current.Name);
                    if (candidate is not null)
                    {
                        return candidate;
                    }
                }

                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ResolveChildPath(string directory, string? childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        var normalized = childName.Trim();
        if (CanBeFileName(normalized))
        {
            var exact = Path.Combine(directory, normalized);
            if (IsExistingFileSystemPath(exact))
            {
                return exact;
            }
        }

        try
        {
            return Directory
                .EnumerateFileSystemEntries(directory)
                .Select(path => new { Path = path, Name = Path.GetFileName(path) })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                                normalized.Contains(entry.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.Name.Length)
                .Select(entry => entry.Path)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool CanBeFileName(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string? NormalizeShellPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(path).LocalPath; } catch { return null; }
        }

        return path;
    }

    private static bool IsExistingFileSystemPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || File.Exists(path));

    private static string? ConvertFileUrlToPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return null;
        try { return new Uri(url).LocalPath; } catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
