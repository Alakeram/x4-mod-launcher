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
        X4LauncherSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<X4LauncherSettings>(
                File.ReadAllText(_settingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new X4LauncherSettings();
        }
        catch (JsonException)
        {
            // Keep the unreadable file so saved profiles can be recovered by hand instead of being overwritten.
            try { File.Copy(_settingsPath, $"{_settingsPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false); }
            catch (IOException) { /* A copy from the same second already exists. */ }
            return new X4LauncherSettings();
        }

        settings.Profiles ??= new List<ModProfile>();
        settings.Profiles.RemoveAll(profile => profile is null || string.IsNullOrWhiteSpace(profile.Name));
        foreach (var profile in settings.Profiles)
            profile.Enabled ??= new List<string>();
        if (settings.BackupRetention < 0)
            settings.BackupRetention = X4LauncherSettings.DefaultBackupRetention;
        MigrateV1Profiles(settings);
        return settings;
    }

    public void Save(X4LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath) ?? throw new InvalidOperationException("Settings path has no directory.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        // Write to a temp file and swap it in so a crash mid-write cannot leave a truncated settings file.
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    // v1.0 stored profiles as name -> enabled IDs and shared them across every X4 profile. They become
    // legacy profiles (Disabled = null) scoped to the X4 profile that was configured when they were made.
    private static void MigrateV1Profiles(X4LauncherSettings settings)
    {
        if (settings.ModProfiles is null)
            return;
        var folder = ModProfileLogic.FolderKey(settings.ProfilePath);
        foreach (var (name, enabled) in settings.ModProfiles)
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.Equals(ModProfileLogic.NativeProfileName, StringComparison.OrdinalIgnoreCase)
                || settings.Profiles.Any(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            settings.Profiles.Add(new ModProfile
            {
                Name = name,
                X4ProfileFolder = folder,
                Enabled = enabled ?? new List<string>(),
                Disabled = null
            });
        }
        settings.ModProfiles = null;
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

    public static IEnumerable<string> ReadSteamLibraryPaths(string libraryFoldersPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(libraryFoldersPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
