using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Security;

namespace X4ModLauncher.Core;

public sealed class SettingsStore
{
    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X4ModLauncher", "settings.json");
    }

    public X4LauncherSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new X4LauncherSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<X4LauncherSettings>(
                File.ReadAllText(_settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new X4LauncherSettings();
            settings.ModProfiles ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (settings.ModProfiles.Comparer != StringComparer.OrdinalIgnoreCase)
                settings.ModProfiles = new Dictionary<string, List<string>>(settings.ModProfiles, StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (JsonException)
        {
            return new X4LauncherSettings();
        }
    }

    public void Save(X4LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("Settings path has no directory.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    public static X4LauncherSettings WithDiscoveredDefaults(X4LauncherSettings settings)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(settings.X4Root) || !File.Exists(Path.Combine(settings.X4Root, "X4.exe")))
        {
            var candidates = DiscoverSteamX4Roots().Concat(new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "X4 Foundations"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "X4 Foundations")
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            settings.X4Root = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "X4.exe"))) ?? candidates.FirstOrDefault() ?? string.Empty;
        }
        if ((string.IsNullOrWhiteSpace(settings.ExtensionsRoot) || !Directory.Exists(settings.ExtensionsRoot)) && !string.IsNullOrWhiteSpace(settings.X4Root))
            settings.ExtensionsRoot = Path.Combine(settings.X4Root, "extensions");
        if (string.IsNullOrWhiteSpace(settings.ProfilePath) || !File.Exists(settings.ProfilePath))
            settings.ProfilePath = FindNewestProfile();
        if (string.IsNullOrWhiteSpace(settings.BackupDirectory) && !string.IsNullOrWhiteSpace(settings.ProfilePath))
            settings.BackupDirectory = Path.Combine(Path.GetDirectoryName(settings.ProfilePath) ?? documents, "X4ModLauncherBackups");
        return settings;
    }

    private static string? FindNewestProfile()
    {
        var documentsRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Documents"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);

        var profiles = new List<string>();
        foreach (var documents in documentsRoots)
        {
            var root = Path.Combine(documents, "Egosoft", "X4");
            if (!Directory.Exists(root))
                continue;
            try
            {
                profiles.AddRange(Directory.EnumerateFiles(root, "content.xml", SearchOption.AllDirectories)
                    .Where(path => !path.Contains("backup", StringComparison.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException)
            {
                // One profile root may be restricted; continue with the others.
            }
            catch (IOException)
            {
                // One profile root may be unavailable; continue with the others.
            }
        }
        return profiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static IEnumerable<string> DiscoverSteamX4Roots()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registryPath in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
        {
            foreach (var property in new[] { "SteamPath", "InstallPath" })
            {
                try
                {
                    var value = Registry.GetValue(registryPath, property, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        steamRoots.Add(value);
                }
                catch (ArgumentException)
                {
                    // A missing or malformed registry path is not a fatal discovery error.
                }
                catch (SecurityException)
                {
                    // Registry access can be restricted by policy; the user can enter paths manually.
                }
                catch (IOException)
                {
                    // Continue with other registry locations and the normal Program Files candidates.
                }
            }
        }
        foreach (var root in steamRoots)
        {
            yield return Path.Combine(root, "steamapps", "common", "X4 Foundations");
            var libraryFolders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFolders))
                continue;
            foreach (var libraryRoot in ReadSteamLibraryPaths(libraryFolders))
                yield return Path.Combine(libraryRoot, "steamapps", "common", "X4 Foundations");
        }
    }

    private static IEnumerable<string> ReadSteamLibraryPaths(string libraryFoldersPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(libraryFoldersPath);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups["path"].Value.Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(path))
                yield return path;
        }
    }
}
