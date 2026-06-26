namespace SvnFloatingAssistant.Models;

public enum SvnHealth
{
    Unknown,
    NotWorkingCopy,
    Clean,
    Modified,
    Conflict,
    Refreshing,
    Slow,
    ToolMissing
}

public sealed record SvnInfo(
    string? Revision,
    string? RemoteRevision,
    string? RepositoryUrl,
    string? Author,
    DateTimeOffset? Date)
{
    public int RevisionNumber => int.TryParse(Revision, out var n) ? n : 0;
    public int RemoteRevisionNumber => int.TryParse(RemoteRevision, out var n) ? n : 0;
    public int BehindCount => Math.Max(0, RemoteRevisionNumber - RevisionNumber);
    public bool IsUpToDate => BehindCount == 0;
}

public sealed record SvnStatusSummary(
    int Modified,
    int Added,
    int Deleted,
    int Unversioned,
    int Missing,
    int Conflicted)
{
    public static SvnStatusSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public bool HasChanges => Modified + Added + Deleted + Unversioned + Missing + Conflicted > 0;
    public string SummaryText
    {
        get
        {
            if (!HasChanges) return "无本地修改";
            var parts = new List<string>();
            if (Modified > 0) parts.Add($"修改 {Modified}");
            if (Added > 0) parts.Add($"新增 {Added}");
            if (Deleted > 0) parts.Add($"删除 {Deleted}");
            if (Conflicted > 0) parts.Add($"冲突 {Conflicted}");
            if (Unversioned > 0) parts.Add($"未加入 {Unversioned}");
            if (Missing > 0) parts.Add($"缺失 {Missing}");
            return string.Join(" | ", parts);
        }
    }
}

public sealed record SvnLogEntry(
    string Revision,
    string Author,
    DateTimeOffset? Date,
    string Message,
    IReadOnlyList<string>? ChangedFiles = null)
{
    public int RevisionNumber => int.TryParse(Revision, out var n) ? n : 0;

    /// <summary>提交消息的第一行，用于列表缩略显示。</summary>
    public string ShortMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Message)) return "(无提交说明)";
            var firstLine = Message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0].Trim();
            return string.IsNullOrWhiteSpace(firstLine) ? "(无提交说明)" : firstLine;
        }
    }
}

public sealed record SvnSnapshot(
    string Path,
    SvnHealth Health,
    SvnInfo? Info,
    SvnStatusSummary Status,
    IReadOnlyList<SvnLogEntry> Logs,
    string? Message)
{
    public static SvnSnapshot Refreshing(string path) =>
        new(path, SvnHealth.Refreshing, null, SvnStatusSummary.Empty, Array.Empty<SvnLogEntry>(), null);

    public static SvnSnapshot NotWorkingCopy(string path) =>
        new(path, SvnHealth.NotWorkingCopy, null, SvnStatusSummary.Empty, Array.Empty<SvnLogEntry>(), null);
}
