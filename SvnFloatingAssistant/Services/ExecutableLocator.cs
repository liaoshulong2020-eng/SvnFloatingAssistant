using System.IO;

namespace SvnFloatingAssistant.Services;

public static class ExecutableLocator
{
    public static string? Find(string exeName, params string?[] candidates)
    {
        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var fullPath = Path.Combine(folder.Trim(), exeName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
