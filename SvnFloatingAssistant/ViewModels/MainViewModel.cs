using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using SvnFloatingAssistant.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace SvnFloatingAssistant.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ── 颜色常量 ──
    public static Brush BlueBg => new SolidColorBrush(Color.FromRgb(24, 95, 165));
    public static Brush DarkBlueBg => new SolidColorBrush(Color.FromRgb(12, 68, 124));
    public static Brush LightBlueBg => new SolidColorBrush(Color.FromRgb(230, 241, 251));
    public static Brush WhiteBg => new SolidColorBrush(Colors.White);
    public static Brush GrayBg => new SolidColorBrush(Color.FromRgb(245, 245, 245));
    public static Brush GreenText => new SolidColorBrush(Color.FromRgb(59, 109, 17));
    public static Brush RedText => new SolidColorBrush(Color.FromRgb(163, 45, 45));
    public static Brush AmberText => new SolidColorBrush(Color.FromRgb(133, 79, 11));
    public static Brush NormalText => new SolidColorBrush(Color.FromRgb(51, 51, 51));
    public static Brush MutedText => new SolidColorBrush(Color.FromRgb(136, 136, 136));
    public static Brush WhiteText => new SolidColorBrush(Colors.White);

    private SvnSnapshot _snapshot = SvnSnapshot.NotWorkingCopy("");
    private bool _isLoading;
    private string _statusText = "等待资源管理器窗口";
    private Brush _statusColor = MutedText;
    private string _refreshTime = "";
    private string _userName = "";
    private string _localRevision = "-";
    private string _remoteRevision = "-";
    private string _behindText = "";
    private Brush _behindColor = MutedText;
    private string _projectName = "SVN Assistant";
    private string _branchPath = "";
    private bool _hasMoreLogs = true;
    private bool _isLogLoading;
    private bool _isNotSvn;
    private bool _isConnected;

    public ObservableCollection<LogEntryViewModel> Logs { get; } = new();
    public ObservableCollection<string> ChangedFiles { get; } = new();

    // ── 标题栏 ──
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }
    public string BranchPath { get => _branchPath; set => Set(ref _branchPath, value); }

    // ── 版本栏 ──
    public string LocalRevision { get => _localRevision; set => Set(ref _localRevision, value); }
    public string RemoteRevision { get => _remoteRevision; set => Set(ref _remoteRevision, value); }
    public string BehindText { get => _behindText; set => Set(ref _behindText, value); }
    public Brush BehindColor { get => _behindColor; set => Set(ref _behindColor, value); }

    // ── 状态栏 ──
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public Brush StatusColor { get => _statusColor; set => Set(ref _statusColor, value); }
    public string RefreshTime { get => _refreshTime; set => Set(ref _refreshTime, value); }

    // ── 状态标志 ──
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public bool IsLogLoading { get => _isLogLoading; set => Set(ref _isLogLoading, value); }
    public bool HasMoreLogs { get => _hasMoreLogs; set => Set(ref _hasMoreLogs, value); }
    public bool IsNotSvn { get => _isNotSvn; set => Set(ref _isNotSvn, value); }
    public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }

    // ── 本机用户名（用于高亮"自己的提交"） ──
    public string UserName { get => _userName; set => Set(ref _userName, value); }

    /// <summary>
    /// 应用完整的 SVN 快照数据。
    /// </summary>
    public void Apply(SvnSnapshot snapshot, string? userName = null)
    {
        _snapshot = snapshot;

        IsNotSvn = snapshot.Health == SvnHealth.NotWorkingCopy;
        IsConnected = snapshot.Health is SvnHealth.Clean or SvnHealth.Modified or SvnHealth.Conflict;

        if (snapshot.Health == SvnHealth.Refreshing)
        {
            IsLoading = true;
            StatusText = "正在刷新...";
            StatusColor = AmberText;
            return;
        }

        IsLoading = false;

        // 标题栏
        var path = snapshot.Path;
        ProjectName = string.IsNullOrWhiteSpace(path) ? "SVN Assistant"
            : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "SVN Assistant";

        BranchPath = "";
        if (snapshot.Info?.RepositoryUrl is not null && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var root = snapshot.Info.RepositoryUrl;
                var localRoot = GetSvnRootFromPath(path);
                if (localRoot is not null)
                    BranchPath = localRoot;
                else
                    BranchPath = path;
            }
            catch { BranchPath = path; }
        }

        // 版本栏
        var info = snapshot.Info;
        if (info is not null)
        {
            LocalRevision = $"r{info.Revision}";
            RemoteRevision = info.RemoteRevision is not null ? $"r{info.RemoteRevision}" : "r?";
            if (info.IsUpToDate)
            {
                BehindText = "✓ 已最新";
                BehindColor = GreenText;
            }
            else
            {
                BehindText = $"落后 {info.BehindCount}";
                BehindColor = RedText;
            }
        }
        else
        {
            LocalRevision = "-";
            RemoteRevision = "-";
            BehindText = "";
            BehindColor = MutedText;
        }

        // 日志区
        Logs.Clear();
        foreach (var log in snapshot.Logs)
        {
            var isOwn = !string.IsNullOrWhiteSpace(userName) &&
                        string.Equals(log.Author, userName, StringComparison.OrdinalIgnoreCase);
            Logs.Add(new LogEntryViewModel(log, isOwn, info?.RevisionNumber));
        }
        HasMoreLogs = snapshot.Logs.Count >= 20;

        // 状态栏
        StatusText = snapshot.Health switch
        {
            SvnHealth.Clean => "已连接 · SVN 工作副本",
            SvnHealth.Modified => "已连接 · 有本地修改",
            SvnHealth.Conflict => "已连接 · 存在冲突",
            SvnHealth.NotWorkingCopy => "非 SVN 工作副本",
            SvnHealth.ToolMissing => "未找到 svn.exe",
            SvnHealth.Slow => "SVN 响应慢",
            _ => "未知状态"
        };
        StatusColor = snapshot.Health switch
        {
            SvnHealth.Clean or SvnHealth.Modified => GreenText,
            SvnHealth.Conflict or SvnHealth.ToolMissing => RedText,
            SvnHealth.NotWorkingCopy => MutedText,
            _ => AmberText
        };
        RefreshTime = $"刷新于 {DateTime.Now:HH:mm}";

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }

    public void ApplyRefreshing(string path)
    {
        var name = string.IsNullOrWhiteSpace(path) ? "SVN Assistant"
            : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "SVN Assistant";
        ProjectName = name;
        IsLoading = true;
        StatusText = "正在刷新...";
        StatusColor = AmberText;
        IsNotSvn = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }

    public void AppendLogs(IReadOnlyList<SvnLogEntry> newLogs, int? currentRevision, string? userName)
    {
        foreach (var log in newLogs)
        {
            var isOwn = !string.IsNullOrWhiteSpace(userName) &&
                        string.Equals(log.Author, userName, StringComparison.OrdinalIgnoreCase);
            Logs.Add(new LogEntryViewModel(log, isOwn, currentRevision));
        }
        HasMoreLogs = newLogs.Count >= 20;
        IsLogLoading = false;
    }

    private static string? GetSvnRootFromPath(string path)
    {
        var di = new DirectoryInfo(path);
        while (di is not null)
        {
            if (Directory.Exists(Path.Combine(di.FullName, ".svn")))
                return di.FullName;
            di = di.Parent;
        }
        return null;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

public sealed class LogEntryViewModel
{
    public SvnLogEntry Entry { get; }
    public bool IsOwnCommit { get; }
    public bool IsCurrentRevision { get; }
    public string RevisionBadge { get; }
    public string AuthorDisplay { get; }
    public string TimeDisplay { get; }
    public string MessageDisplay { get; }
    public string FilesChangedText { get; }
    public Brush RowBackground { get; }
    public Brush RevisionBadgeBg { get; }
    public Brush RevisionBadgeText { get; }
    public Brush AuthorColor { get; }
    public FontWeight AuthorWeight { get; }
    public bool HasFilesChanged { get; }

    public LogEntryViewModel(SvnLogEntry entry, bool isOwnCommit, int? currentRevision)
    {
        Entry = entry;
        IsOwnCommit = isOwnCommit;
        IsCurrentRevision = currentRevision.HasValue && entry.RevisionNumber == currentRevision.Value;

        RevisionBadge = $"r{entry.Revision}";
        AuthorDisplay = string.IsNullOrWhiteSpace(entry.Author) ? "(未知)" : entry.Author;
        MessageDisplay = string.IsNullOrWhiteSpace(entry.Message) ? "(无提交说明)" : entry.Message;
        HasFilesChanged = entry.ChangedFiles?.Count > 0;
        FilesChangedText = HasFilesChanged ? $"变更 {entry.ChangedFiles!.Count} 个文件" : "";

        if (entry.Date.HasValue)
        {
            var local = entry.Date.Value.LocalDateTime;
            var today = DateTime.Now.Date;
            if (local.Date == today)
                TimeDisplay = $"今天 {local:HH:mm}";
            else if (local.Date == today.AddDays(-1))
                TimeDisplay = $"昨天 {local:HH:mm}";
            else
                TimeDisplay = local.ToString("MM-dd HH:mm");
        }
        else
        {
            TimeDisplay = "";
        }

        // 样式
        if (IsCurrentRevision)
        {
            RevisionBadgeBg = MainViewModel.DarkBlueBg;
            RevisionBadgeText = MainViewModel.WhiteText;
        }
        else
        {
            RevisionBadgeBg = MainViewModel.BlueBg;
            RevisionBadgeText = MainViewModel.WhiteText;
        }

        RowBackground = isOwnCommit ? MainViewModel.LightBlueBg : (Brush)MainViewModel.WhiteBg;
        AuthorColor = isOwnCommit ? MainViewModel.BlueBg : MainViewModel.MutedText;
        AuthorWeight = isOwnCommit ? FontWeights.SemiBold : FontWeights.Normal;
    }
}
