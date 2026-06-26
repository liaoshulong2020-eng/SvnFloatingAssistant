using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using SvnFloatingAssistant.Models;

namespace SvnFloatingAssistant.Services;

public sealed class SvnService
{
    private readonly AppSettings _settings;
    private readonly SvnCache _cache;
    private readonly string? _svnPath;
    private readonly string? _subWcRevPath;

    public SvnService(AppSettings settings, SvnCache cache)
    {
        _settings = settings;
        _cache = cache;
        _svnPath = ExecutableLocator.Find(
            "svn.exe",
            settings.SvnPath,
            @"C:\Program Files\TortoiseSVN\bin\svn.exe",
            @"C:\Program Files\SlikSvn\bin\svn.exe",
            @"C:\Program Files\VisualSVN\bin\svn.exe");
        _subWcRevPath = ExecutableLocator.Find(
            "SubWCRev.exe",
            settings.SubWCRevPath,
            @"C:\Program Files\TortoiseSVN\bin\SubWCRev.exe",
            @"C:\Program Files (x86)\TortoiseSVN\bin\SubWCRev.exe");
    }

    public bool IsSvnAvailable => !string.IsNullOrWhiteSpace(_svnPath);

    public bool IsTortoiseCompatibilityAvailable => !string.IsNullOrWhiteSpace(_subWcRevPath);

    public async Task<SvnSnapshot> GetSnapshotAsync(string path, bool includeLogs, CancellationToken cancellationToken)
    {
        var target = ResolveCommandTarget(path);
        if (target is null)
        {
            return SvnSnapshot.NotWorkingCopy(path);
        }

        if (!IsSvnAvailable)
        {
            return IsTortoiseCompatibilityAvailable
                ? await GetSubWcRevSnapshotAsync(path, target, cancellationToken)
                : new SvnSnapshot(
                    path,
                    SvnHealth.ToolMissing,
                    null,
                    SvnStatusSummary.Empty,
                    Array.Empty<SvnLogEntry>(),
                    "未找到 svn.exe 或 SubWCRev.exe");
        }

        var info = _cache.GetInfo(path);
        if (info is null)
        {
            var infoResult = await RunProcessAsync(_svnPath!, target.WorkingDirectory, $"info --xml {target.Argument}", TimeSpan.FromSeconds(1), cancellationToken);
            if (infoResult.TimedOut)
            {
                return Slow(path);
            }

            if (infoResult.ExitCode != 0)
            {
                return SvnSnapshot.NotWorkingCopy(path);
            }

            info = ParseInfoFromXml(infoResult.StandardOutput);

            // 获取远程 HEAD 版本号
            var headResult = await RunProcessAsync(_svnPath!, target.WorkingDirectory, $"info -r HEAD --xml {target.Argument}", TimeSpan.FromSeconds(3), cancellationToken);
            if (headResult.ExitCode == 0 && !headResult.TimedOut)
            {
                var headInfo = ParseInfoFromXml(headResult.StandardOutput);
                info = info with { RemoteRevision = headInfo?.Revision ?? info.Revision };
            }
            else
            {
                info = info with { RemoteRevision = info.Revision };
            }

            _cache.SetInfo(path, info);
        }

        var status = _cache.GetStatus(path);
        if (status is null)
        {
            var statusResult = await RunProcessAsync(_svnPath!, target.WorkingDirectory, $"status {target.Argument}", TimeSpan.FromSeconds(3), cancellationToken);
            if (statusResult.TimedOut)
            {
                return Slow(path, info);
            }

            status = statusResult.ExitCode == 0
                ? ParseStatus(statusResult.StandardOutput)
                : SvnStatusSummary.Empty;
            _cache.SetStatus(path, status);
        }

        IReadOnlyList<SvnLogEntry> logs = Array.Empty<SvnLogEntry>();
        if (includeLogs)
        {
            logs = _cache.GetLogs(path) ?? await LoadLogsAsync(path, cancellationToken: cancellationToken);
        }

        var health = status.Conflicted > 0
            ? SvnHealth.Conflict
            : status.HasChanges
                ? SvnHealth.Modified
                : SvnHealth.Clean;

        return new SvnSnapshot(path, health, info, status, logs, null);
    }

    public async Task<IReadOnlyList<SvnLogEntry>> LoadLogsAsync(string path, int limit = 20, int skip = 0, CancellationToken cancellationToken = default)
    {
        var target = ResolveCommandTarget(path);
        if (target is null)
        {
            return Array.Empty<SvnLogEntry>();
        }

        var logResult = await RunProcessAsync(_svnPath!, target.WorkingDirectory, $"log -l {limit} --xml {target.Argument}", TimeSpan.FromSeconds(5), cancellationToken);
        if (logResult.ExitCode != 0 || logResult.TimedOut)
        {
            return Array.Empty<SvnLogEntry>();
        }

        var logs = ParseLogs(logResult.StandardOutput);
        if (skip == 0)
            _cache.SetLogs(path, logs);
        return logs;
    }

    public async Task<IReadOnlyList<SvnLogEntry>> LoadMoreLogsAsync(string path, int totalLoaded, CancellationToken cancellationToken)
    {
        return await LoadLogsAsync(path, 20, totalLoaded, cancellationToken);
    }

    private static SvnSnapshot Slow(string path, SvnInfo? info = null) =>
        new(path, SvnHealth.Slow, info, SvnStatusSummary.Empty, Array.Empty<SvnLogEntry>(), "SVN响应慢");

    private async Task<SvnSnapshot> GetSubWcRevSnapshotAsync(string path, CommandTarget target, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(_subWcRevPath!, target.WorkingDirectory, QuoteArgument(target.WorkingDirectory), TimeSpan.FromSeconds(2), cancellationToken);
        if (result.TimedOut)
        {
            return Slow(path);
        }

        if (result.ExitCode != 0)
        {
            return SvnSnapshot.NotWorkingCopy(path);
        }

        var info = ParseSubWcRevInfo(result.StandardOutput);
        var status = ParseSubWcRevStatus(result.StandardOutput);
        var health = status.HasChanges ? SvnHealth.Modified : SvnHealth.Clean;

        return new SvnSnapshot(
            path,
            health,
            info,
            status,
            Array.Empty<SvnLogEntry>(),
            "TortoiseSVN兼容模式");
    }

    private static CommandTarget? ResolveCommandTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return new CommandTarget(path, path, QuoteArgument(path));
        }

        if (File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            return new CommandTarget(path, directory, QuoteArgument(path));
        }

        return null;
    }

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString(), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessResult(-1, output.ToString(), error.ToString(), true);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, output.ToString(), ex.Message, false);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort timeout cleanup.
        }
    }

    private static SvnInfo ParseInfoFromXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var entry = doc.Descendants("entry").FirstOrDefault();
            if (entry is null) return new SvnInfo(null, null, null, null, null);

            var revision = entry.Attribute("revision")?.Value;
            var url = entry.Element("url")?.Value;
            var root = entry.Element("root")?.Value;
            var commit = entry.Element("commit");
            var author = commit?.Element("author")?.Value;
            DateTimeOffset? date = null;
            var dateStr = commit?.Element("date")?.Value;
            if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                date = d.ToLocalTime();

            var repoUrl = !string.IsNullOrWhiteSpace(root) ? root : url;
            return new SvnInfo(revision, null, repoUrl, author, date);
        }
        catch
        {
            return new SvnInfo(null, null, null, null, null);
        }
    }

    private static SvnInfo ParseInfo(string output)
    {
        var map = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        map.TryGetValue("Revision", out var revision);
        map.TryGetValue("Repository Root", out var repositoryUrl);
        map.TryGetValue("Last Changed Author", out var author);
        map.TryGetValue("Last Changed Date", out var dateText);

        DateTimeOffset? date = null;
        if (!string.IsNullOrWhiteSpace(dateText))
        {
            var bracketIndex = dateText.IndexOf('(');
            var normalized = bracketIndex > 0 ? dateText[..bracketIndex].Trim() : dateText;
            if (DateTimeOffset.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                date = parsed;
            }
        }

        return new SvnInfo(revision, null, repositoryUrl, author, date);
    }

    private static SvnInfo ParseSubWcRevInfo(string output)
    {
        string? lastCommittedRevision = null;
        string? updatedRevision = null;

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            const string lastCommittedPrefix = "Last committed at revision";
            const string updatedPrefix = "Updated to revision";

            if (line.StartsWith(lastCommittedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                lastCommittedRevision = line[lastCommittedPrefix.Length..].Trim();
            }
            else if (line.StartsWith(updatedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                updatedRevision = line[updatedPrefix.Length..].Trim();
            }
        }

        return new SvnInfo(updatedRevision ?? lastCommittedRevision, null, null, null, null);
    }

    private static SvnStatusSummary ParseSubWcRevStatus(string output)
    {
        var modified = output.Contains("Modified files found", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var unversioned = output.Contains("Unversioned items found", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var conflicted = output.Contains("conflict", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        return new SvnStatusSummary(modified, 0, 0, unversioned, 0, conflicted);
    }

    private static SvnStatusSummary ParseStatus(string output)
    {
        var modified = 0;
        var added = 0;
        var deleted = 0;
        var unversioned = 0;
        var missing = 0;
        var conflicted = 0;

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0)
            {
                continue;
            }

            switch (line[0])
            {
                case 'M':
                    modified++;
                    break;
                case 'A':
                    added++;
                    break;
                case 'D':
                    deleted++;
                    break;
                case '?':
                    unversioned++;
                    break;
                case '!':
                    missing++;
                    break;
                case 'C':
                    conflicted++;
                    break;
            }
        }

        return new SvnStatusSummary(modified, added, deleted, unversioned, missing, conflicted);
    }

    private static IReadOnlyList<SvnLogEntry> ParseLogs(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            return document
                .Descendants("logentry")
                .Select(entry =>
                {
                    var revision = entry.Attribute("revision")?.Value ?? "?";
                    var author = entry.Element("author")?.Value ?? "";
                    var message = entry.Element("msg")?.Value?.Trim() ?? "(无提交说明)";
                    DateTimeOffset? date = null;
                    if (DateTimeOffset.TryParse(entry.Element("date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        date = parsed.ToLocalTime();
                    }

                    return new SvnLogEntry(revision, author, date, message);
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<SvnLogEntry>();
        }
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(无提交说明)";
        }

        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0].Trim();
    }

    private sealed record CommandTarget(string DisplayPath, string WorkingDirectory, string Argument);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
}
