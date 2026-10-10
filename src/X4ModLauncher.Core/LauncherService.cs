using System.Diagnostics;

namespace X4ModLauncher.Core;

public sealed class X4LauncherService
{
    public bool IsGameRunning() => Process.GetProcessesByName("X4").Length > 0;

    public Process Launch(string x4Root)
    {
        var executable = Path.Combine(x4Root, "X4.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException($"X4.exe was not found at '{executable}'.", executable);
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = x4Root,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows did not return a process handle for X4.");
    }
}
