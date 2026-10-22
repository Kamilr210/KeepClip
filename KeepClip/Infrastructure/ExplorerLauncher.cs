using System.Diagnostics;

namespace KeepClip.Infrastructure;

internal static class ExplorerLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!Path.IsPathFullyQualified(windows))
            throw new InvalidOperationException("Windows directory is unavailable.");
        var fullPath = Path.GetFullPath(path);
        var startInfo = new ProcessStartInfo { FileName = Path.Combine(windows, "explorer.exe"), UseShellExecute = false };
        startInfo.ArgumentList.Add(File.Exists(fullPath) ? $"/select,{fullPath}" : fullPath);
        return startInfo;
    }
}