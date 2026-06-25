using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Window = System.Windows.Window;
using SvnFloatingAssistant.Models;
using SvnFloatingAssistant.Services;
using SvnFloatingAssistant.ViewModels;

namespace SvnFloatingAssistant;

public partial class MainWindow : Window
{
    // ── 依赖注入 ──
    private readonly AppSettings _settings;
    private readonly ExplorerPathMonitor _explorerPathMonitor = new();
    private readonly SvnCache _cache = new();
    private readonly DispatcherTimer _pollTimer = new();
    private readonly MainViewModel _viewModel = new();
    private readonly SvnService _svnService;
    private readonly TortoiseSvnLauncher _tortoiseSvnLauncher;

    // ── 状态 ──
    private CancellationTokenSource? _refreshCts;
    private string? _currentPath;
    private string? _pendingPath;
    private DateTimeOffset _pendingSince;
    private int _totalLogsLoaded;
    private bool _isPinned;
    private bool _isCollapsed;
    private bool _isSnappedRight = true;
    private int _nullPathCount;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _settings.Save();
        _svnService = new SvnService(_settings, _cache);
        _tortoiseSvnLauncher = new TortoiseSvnLauncher(_settings);

        DataContext = _viewModel;
        Opacity = Math.Clamp(_settings.Opacity, 0.2, 1);

        _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(800, _settings.ExplorerPollMilliseconds));
        _pollTimer.Tick += PollTimer_Tick;

        // 获取本机 SVN 用户名
        _ = DetectUserNameAsync();
    }

    private async Task DetectUserNameAsync()
    {
        try
        {
            var path = _explorerPathMonitor.GetCurrentExplorerPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                var info = await _svnService.GetSnapshotAsync(path, false, CancellationToken.None);
                if (info?.Info?.Author is { Length: > 0 } author)
                    _viewModel.UserName = author;
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════
    //  窗口事件
    // ═══════════════════════════════════════════════════

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 初始位置：右边缘
        _isSnappedRight = true;
        Left = SystemParameters.WorkArea.Right - Width;
        Top = SystemParameters.WorkArea.Top + 120;

        _pollTimer.Start();
        InitTrayIcon();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            // 拖拽后取消吸附
            _isSnappedRight = Left > SystemParameters.WorkArea.Width / 2;
            CheckSnap();
        }
    }

    // ═══════════════════════════════════════════════════
    //  粘性吸附
    // ═══════════════════════════════════════════════════

    private void CheckSnap()
    {
        var workArea = SystemParameters.WorkArea;
        var snapThreshold = 150.0;

        if (Left + Width > workArea.Right - snapThreshold)
        {
            _isSnappedRight = true;
            Left = workArea.Right - Width;
        }
        else if (Left < snapThreshold)
        {
            _isSnappedRight = false;
            Left = 0;
        }
        else
        {
            _isSnappedRight = Left > workArea.Width / 2;
        }
    }

    // ═══════════════════════════════════════════════════
    //  折叠 / 展开
    // ═══════════════════════════════════════════════════

    private void SetCollapsed(bool collapsed)
    {
        _isCollapsed = collapsed;
        Width = collapsed ? 18 : 260;
        if (collapsed)
        {
            // 保持吸附位置不变
        }
        else
        {
            Width = 260;
        }
    }

    // ═══════════════════════════════════════════════════
    //  标题栏按钮
    // ═══════════════════════════════════════════════════

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        // PinButton.Content = _isPinned ? "📌" : "📍";
        // 简化：切换 Topmost
        Topmost = _isPinned;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SetVisibleState(false);
    }

    // ═══════════════════════════════════════════════════
    //  资源管理器路径监听
    // ═══════════════════════════════════════════════════

    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        var path = _explorerPathMonitor.GetCurrentExplorerPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            // 非 Explorer 窗口 → 透明隐藏（不销毁窗口状态）
            _nullPathCount++;
            if (_nullPathCount >= 3 && !_isPinned)
            {
                SetVisibleState(false);
            }
            return;
        }

        _nullPathCount = 0;

        // 恢复显示
        SetVisibleState(true);

        if (string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            return;

        // 消抖
        if (!string.Equals(path, _pendingPath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingPath = path;
            _pendingSince = DateTimeOffset.Now;
            return;
        }

        if (DateTimeOffset.Now - _pendingSince < TimeSpan.FromMilliseconds(Math.Max(100, _settings.DebounceMilliseconds)))
            return;

        _currentPath = path;
        _pendingPath = null;
        _totalLogsLoaded = 0;
        await RefreshAsync(path, includeLogs: true);
    }

    // ═══════════════════════════════════════════════════
    //  数据刷新
    // ═══════════════════════════════════════════════════

    private async Task RefreshAsync(string path, bool includeLogs)
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        _viewModel.ApplyRefreshing(path);

        try
        {
            var snapshot = await Task.Run(() => _svnService.GetSnapshotAsync(path, includeLogs, token), token);
            if (!token.IsCancellationRequested)
            {
                _totalLogsLoaded = snapshot.Logs.Count;
                _viewModel.Apply(snapshot, _viewModel.UserName);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ═══════════════════════════════════════════════════
    //  日志无限滚动
    // ═══════════════════════════════════════════════════

    private async void LogScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.ViewportHeight + e.VerticalOffset >= e.ExtentHeight - 50 &&
            _viewModel.HasMoreLogs && !_viewModel.IsLogLoading && _currentPath is not null)
        {
            _viewModel.IsLogLoading = true;
            try
            {
                var token = _refreshCts?.Token ?? CancellationToken.None;
                var moreLogs = await Task.Run(() => _svnService.LoadMoreLogsAsync(_currentPath, _totalLogsLoaded, token), token);
                if (!token.IsCancellationRequested)
                {
                    _totalLogsLoaded += moreLogs.Count;
                    _viewModel.AppendLogs(moreLogs, null, _viewModel.UserName);
                }
            }
            catch { _viewModel.IsLogLoading = false; }
        }
    }

    // ═══════════════════════════════════════════════════
    //  操作按钮
    // ═══════════════════════════════════════════════════

    private async void RefreshMenu_Click(object sender, RoutedEventArgs e)
    {
        var path = _currentPath ?? _explorerPathMonitor.GetCurrentExplorerPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _currentPath = path;
            _totalLogsLoaded = 0;
            await RefreshAsync(path, includeLogs: true);
        }
    }

    private void UpdateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentPath))
            _tortoiseSvnLauncher.OpenUpdate(_currentPath);
    }

    private void CommitMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentPath))
            _tortoiseSvnLauncher.OpenCommit(_currentPath);
    }

    private void OpenLogMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentPath))
            _tortoiseSvnLauncher.OpenLog(_currentPath);
    }

    private void RefreshTime_Click(object sender, MouseButtonEventArgs e)
    {
        RefreshMenu_Click(sender, e);
    }

    /// <summary>
    /// 显示/隐藏窗口（通过透明度而非 WPF Hide/Show，避免 HWND 销毁）。
    /// 显示时不激活窗口，防止抢 Explorer 焦点。
    /// </summary>
    private void SetVisibleState(bool visible)
    {
        if (visible)
        {
            Opacity = Math.Clamp(_settings.Opacity, 0.2, 1);
            // 显示但不激活（SW_SHOWNA = 8），不抢 Explorer 焦点
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                ShowWindowAsync(hwnd, 8);
        }
        else
        {
            Opacity = 0;
            // 用 SW_HIDE 隐藏，始终保持 ShowInTaskbar=false
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                ShowWindowAsync(hwnd, 0);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    // ═══════════════════════════════════════════════════
    //  系统托盘
    // ═══════════════════════════════════════════════════

    private void InitTrayIcon()
    {
        // 从文件路径提取图标（如果 System.Drawing 不可用则跳过）
        System.Drawing.Icon? appIcon = null;
        try
        {
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(location))
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(location);
        }
        catch { }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "SVN Overlay · 等待连接",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleVisibility());
        menu.Items.Add("强制刷新", null, async (_, _) =>
        {
            if (_currentPath is not null)
                await RefreshAsync(_currentPath, includeLogs: true);
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    private void ToggleVisibility()
    {
        SetVisibleState(Opacity < 0.5);
    }

    private void ExitApp()
    {
        _refreshCts?.Cancel();
        _trayIcon?.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
