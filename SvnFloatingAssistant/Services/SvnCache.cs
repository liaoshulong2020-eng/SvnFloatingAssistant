using SvnFloatingAssistant.Models;

namespace SvnFloatingAssistant.Services;

public sealed class SvnCache
{
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public SvnInfo? GetInfo(string path)
    {
        return TryGet(path, out var entry) && DateTimeOffset.Now - entry.InfoUpdatedAt < TimeSpan.FromSeconds(60)
            ? entry.Info
            : null;
    }

    public SvnStatusSummary? GetStatus(string path)
    {
        return TryGet(path, out var entry) && DateTimeOffset.Now - entry.StatusUpdatedAt < TimeSpan.FromSeconds(10)
            ? entry.Status
            : null;
    }

    public IReadOnlyList<SvnLogEntry>? GetLogs(string path)
    {
        return TryGet(path, out var entry) && DateTimeOffset.Now - entry.LogUpdatedAt < TimeSpan.FromSeconds(60)
            ? entry.Logs
            : null;
    }

    public void SetInfo(string path, SvnInfo info)
    {
        var entry = GetOrCreate(path);
        entry.Info = info;
        entry.InfoUpdatedAt = DateTimeOffset.Now;
    }

    public void SetStatus(string path, SvnStatusSummary status)
    {
        var entry = GetOrCreate(path);
        entry.Status = status;
        entry.StatusUpdatedAt = DateTimeOffset.Now;
    }

    public void SetLogs(string path, IReadOnlyList<SvnLogEntry> logs)
    {
        var entry = GetOrCreate(path);
        entry.Logs = logs;
        entry.LogUpdatedAt = DateTimeOffset.Now;
    }

    private bool TryGet(string path, out CacheEntry entry) => _entries.TryGetValue(path, out entry!);

    private CacheEntry GetOrCreate(string path)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = new CacheEntry();
            _entries[path] = entry;
        }

        return entry;
    }

    private sealed class CacheEntry
    {
        public SvnInfo? Info { get; set; }
        public DateTimeOffset InfoUpdatedAt { get; set; }
        public SvnStatusSummary? Status { get; set; }
        public DateTimeOffset StatusUpdatedAt { get; set; }
        public IReadOnlyList<SvnLogEntry>? Logs { get; set; }
        public DateTimeOffset LogUpdatedAt { get; set; }
    }
}
