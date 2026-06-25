using System.IO;
using System.Text.Json;

namespace SvnFloatingAssistant.Services;

public sealed class AppSettings
{
    public bool AutoRefresh { get; set; } = true;
    public int ExplorerPollMilliseconds { get; set; } = 500;
    public int DebounceMilliseconds { get; set; } = 400;
    public int BubbleSize { get; set; } = 72;
    public double Opacity { get; set; } = 0.95;
    public bool DarkMode { get; set; }
    public string? SvnPath { get; set; }
    public string? SubWCRevPath { get; set; }
    public string? TortoiseSvnProcPath { get; set; }

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SvnFloatingAssistant",
            "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
