using System.ComponentModel;
using System.Diagnostics;

namespace X4ModLauncher.Core;

public enum LaunchMethod
{
    Steam,
    Executable
}

public sealed class X4LauncherService
{
    public const int SteamAppId = 392160;

    public bool IsGameRunning() => Process.GetProcessesByName("X4").Length > 0;

    /// <summary>
    /// Starts X4. Steam installs go through steam://rungameid so Steam owns the session (overlay, playtime,
    /// Workshop sync) instead of X4 restarting itself through Steam; other installs run X4.exe directly.
    /// </summary>
    public LaunchMethod Launch(string x4Root)
    {
        var executable = Path.Combine(x4Root, "X4.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException($"X4.exe was not found at '{executable}'.", executable);

        if (IsSteamInstall(x4Root))
        {
            try
            {
                Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });
                return LaunchMethod.Steam;
            }
            catch (Win32Exception)
            {
                // No steam:// handler is registered; fall back to the executable.
            }
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = x4Root,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows did not return a process handle for X4.");
        return LaunchMethod.Executable;
    }

    public static bool IsSteamInstall(string x4Root) =>
        x4Root.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("steamapps", StringComparison.OrdinalIgnoreCase));
}
