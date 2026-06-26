using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace SvnFloatingAssistant.Services;

public sealed class ExplorerPathMonitor
{
    private static DateTimeOffset _directoryOpusCacheUpdatedAt = DateTimeOffset.MinValue;
    private static string? _directoryOpusCachedActivePath;
    private static bool _directoryOpusCacheRefreshing;
    private static readonly object DirectoryOpusCacheLock = new();

    /// <summary>
    /// 获取当前文件管理器窗口所在目录的路径（仅当前目录，不做悬停/选中项检测）。
    /// </summary>
    public string? GetCurrentExplorerPath()
    {
        // 优先取光标下方的文件管理器窗口
        var cursorPos = GetCursorPosition();
        IntPtr explorerWindow = IntPtr.Zero;
        if (cursorPos.HasValue)
        {
            var pt = cursorPos.Value;
            var hwnd = WindowFromPoint(pt.X, pt.Y);
            if (hwnd != IntPtr.Zero)
            {
                var root = GetAncestor(hwnd, 2);
                if (root == IntPtr.Zero) root = hwnd;
                if (IsSupportedFileManagerWindow(root))
                    explorerWindow = root;
            }
        }

        // 光标下方没有，退而取前台窗口
        var foreground = GetForegroundWindow();
        if (explorerWindow == IntPtr.Zero)
        {
            if (IsSupportedFileManagerWindow(foreground))
                explorerWindow = foreground;
        }

        if (explorerWindow == IntPtr.Zero)
            return foreground == IntPtr.Zero ? null : GetTitlePath(foreground);

        // Directory Opus 分支
        if (TryGetDirectoryOpusPath(explorerWindow) is { } dopusPath)
            return dopusPath;

        // Windows Explorer 分支：通过 Shell COM 获取当前目录路径
        return GetExplorerPathByCom(explorerWindow);
    }

    private static string? GetExplorerPathByCom(IntPtr targetHwnd)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;

        try
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var window in shell.Windows())
            {
                try
                {
                    if (new IntPtr(Convert.ToInt64(window.HWND)) != targetHwnd)
                        continue;

                    string url = "";
                    try { url = (string)window.LocationURL; } catch { }
                    if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        try { return new Uri(url).LocalPath; } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    // ─── Directory Opus ────────────────────────────────────────────

    private static string? TryGetDirectoryOpusPath(IntPtr hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return null;

        var processName = GetProcessName(pid);
        if (string.IsNullOrWhiteSpace(processName) ||
            !processName.StartsWith("dopus", StringComparison.OrdinalIgnoreCase))
            return null;

        var dopusrtPath = FindDopusrt();
        if (dopusrtPath is null) return null;

        // 查缓存（5 秒内有效）
        lock (DirectoryOpusCacheLock)
        {
            if (!_directoryOpusCacheRefreshing &&
                (DateTimeOffset.UtcNow - _directoryOpusCacheUpdatedAt).TotalSeconds < 5 &&
                _directoryOpusCachedActivePath is not null)
            {
                return _directoryOpusCachedActivePath;
            }
        }

        // 后台异步刷新
        RefreshDirectoryOpusCache(dopusrtPath);

        // 返回现有缓存（可能略旧，但不阻塞）
        lock (DirectoryOpusCacheLock)
        {
            return _directoryOpusCachedActivePath;
        }
    }

    private static void RefreshDirectoryOpusCache(string dopusrtPath)
    {
        lock (DirectoryOpusCacheLock)
        {
            if (_directoryOpusCacheRefreshing) return;
            _directoryOpusCacheRefreshing = true;
        }

        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = dopusrtPath,
                    Arguments = "/info ...,pathtab",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null) return;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                // 解析 XML 输出
                if (!string.IsNullOrWhiteSpace(output))
                {
                    try
                    {
                        var xml = XDocument.Parse(output);
                        var activePath = xml
                            .Descendants("Path")
                            .Select(p => p.Value?.Trim())
                            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p));

                        lock (DirectoryOpusCacheLock)
                        {
                            _directoryOpusCachedActivePath = activePath;
                            _directoryOpusCacheUpdatedAt = DateTimeOffset.UtcNow;
                        }
                        return;
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                lock (DirectoryOpusCacheLock)
                {
                    _directoryOpusCacheRefreshing = false;
                }
            }
        });
    }

    // ─── 辅助方法 ──────────────────────────────────────────────

    private static string? FindDopusrt()
    {
        // 常见安装路径
        var candidates = new[]
        {
            @"E:\迅雷\Directory Opus\dopusrt.exe",
            @"C:\Program Files\Directory Opus\dopusrt.exe",
            @"C:\Program Files (x86)\Directory Opus\dopusrt.exe",
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // 从 PATH 搜索
        try
        {
            var result = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "dopusrt.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (result is not null)
            {
                var path = result.StandardOutput.ReadLine();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
        }
        catch { }

        return null;
    }

    private static string? GetProcessName(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch { return null; }
    }

    private static bool IsSupportedFileManagerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        if (pid == 0) return false;

        var name = GetProcessName(pid);
        if (string.IsNullOrWhiteSpace(name)) return false;

        return name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("dopus", StringComparison.OrdinalIgnoreCase);
    }

    private static (int X, int Y)? GetCursorPosition()
    {
        if (GetCursorPos(out var pt))
            return (pt.X, pt.Y);
        return null;
    }

    private static string? GetTitlePath(IntPtr hwnd)
    {
        var title = GetWindowText(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return null;

        // 尝试从窗口标题提取路径
        foreach (var drive in DriveInfo.GetDrives())
        {
            var root = drive.Name.TrimEnd('\\');
            if (title.Contains(root, StringComparison.OrdinalIgnoreCase))
            {
                // 取标题中路径部分：通常形如 "D:\some\path - Explorer"
                foreach (var part in title.Split(new[] { " - ", " — " }, StringSplitOptions.None))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 3 && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
                    {
                        if (Directory.Exists(trimmed))
                            return trimmed;
                    }
                }
            }
        }
        return null;
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    // ─── Win32 P/Invoke ─────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

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
}
