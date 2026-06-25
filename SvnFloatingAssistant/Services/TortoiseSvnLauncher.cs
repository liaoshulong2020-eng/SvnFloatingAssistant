using System.Diagnostics;

namespace SvnFloatingAssistant.Services;

public sealed class TortoiseSvnLauncher
{
    private readonly string? _tortoisePath;

    public TortoiseSvnLauncher(AppSettings settings)
    {
        _tortoisePath = ExecutableLocator.Find(
            "TortoiseProc.exe",
            settings.TortoiseSvnProcPath,
            @"C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe",
            @"C:\Program Files (x86)\TortoiseSVN\bin\TortoiseProc.exe");
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_tortoisePath);

    public void OpenLog(string path) => Run($"/command:log /path:\"{path}\"");

    public void OpenCommit(string path) => Run($"/command:commit /path:\"{path}\"");

    public void OpenUpdate(string path) => Run($"/command:update /path:\"{path}\"");

    private void Run(string arguments)
    {
        if (!IsAvailable)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _tortoisePath!,
            Arguments = arguments,
            UseShellExecute = false
        });
    }
}
