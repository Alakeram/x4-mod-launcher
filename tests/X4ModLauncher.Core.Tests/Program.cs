using System.Security.Cryptography;
using X4ModLauncher.Core;

namespace X4ModLauncher.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("toggle preserves unrelated XML and creates a timestamped backup", TogglePreservesXml),
            ("duplicate extension IDs fail closed", DuplicateIdsFailClosed),
            ("unavailable folders are reported and cannot be selected", UnavailableFoldersFailClosed),
            ("optional dependencies are not treated as required", OptionalDependenciesAreMarked),
            ("extension versions are discovered from metadata", ExtensionVersionsAreDiscovered),
            ("Native Hotkey API writes explicit Steam sync state", NativeHotkeySyncIsExplicit),
            ("restore returns the original profile", RestoreReturnsOriginal),
            ("auto-discovery is non-fatal when locations are unavailable", AutoDiscoveryIsNonFatal),
            ("Steam library paths are discovered from libraryfolders.vdf", SteamLibraryPathDiscovery),
            ("deprecated profile entries can be removed with backup", DeprecatedEntriesCanBeRemoved),
            ("named mod profiles persist in settings", ModProfilesPersist)
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"FAIL {test.Name}: {ex.Message}");
                Console.WriteLine(failures[^1]);
            }
        }

        Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void TogglePreservesXml()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content custom=\"keep\">\n  <metadata value=\"untouched\" />\n  <extension id=\"alpha\" enabled=\"false\" custom=\"keep-too\" />\n  <extension id=\"other\" enabled=\"true\" />\n</content>");
        var alpha = temp.AddExtension("alpha", "Alpha Extension");
        var other = temp.AddExtension("other", "Other Extension");
        var snapshot = new X4ProfileService().Read(profile);
        var catalog = ExtensionCatalog.Discover(temp.ExtensionsRoot, snapshot);

        var result = new X4ProfileService().Apply(profile, temp.BackupsRoot, catalog, new Dictionary<string, bool> { ["alpha"] = true }, isGameRunning: () => false, backupLabel: "Story Mods");
        var raw = File.ReadAllText(profile);
        Assert(raw.Contains("custom=\"keep\""), "root attribute was lost");
        Assert(raw.Contains("custom=\"keep-too\""), "unrelated extension attribute was lost");
        Assert(raw.Contains("<metadata value=\"untouched\" />"), "unrelated XML node was lost");
        Assert(new X4ProfileService().Read(profile).Extensions["alpha"].Enabled, "alpha was not enabled");
        Assert(File.Exists(result.BackupPath), "backup was not created");
        Assert(Path.GetFileName(result.BackupPath).StartsWith("Story Mods-Backup-", StringComparison.Ordinal), "backup does not use the profile-name prefix");
        Assert(Hash(result.BackupPath) != Hash(profile), "backup should contain the previous profile state");
        _ = alpha;
        _ = other;
    }

    private static void DuplicateIdsFailClosed()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content><extension id=\"alpha\" enabled=\"true\" /><extension id=\"alpha\" enabled=\"false\" /></content>");
        var originalHash = Hash(profile);
        try
        {
            _ = new X4ProfileService().Read(profile);
            throw new InvalidOperationException("duplicate IDs were accepted");
        }
        catch (DuplicateExtensionIdException)
        {
            Assert(Hash(profile) == originalHash, "duplicate-ID failure changed the profile");
        }
    }

    private static void UnavailableFoldersFailClosed()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content><extension id=\"missing_extension\" enabled=\"true\" /></content>");
        var service = new X4ProfileService();
        var catalog = ExtensionCatalog.Discover(temp.ExtensionsRoot, service.Read(profile));
        var entry = catalog.Single();
        Assert(!entry.IsAvailable, "profile-only extension should be unavailable");
        var originalHash = Hash(profile);
        try
        {
            _ = service.Apply(profile, temp.BackupsRoot, catalog, new Dictionary<string, bool> { [entry.Id] = false }, isGameRunning: () => false);
            throw new InvalidOperationException("unavailable extension was accepted");
        }
        catch (InvalidOperationException)
        {
            Assert(Hash(profile) == originalHash, "unavailable-folder failure changed the profile");
        }
    }

    private static void NativeHotkeySyncIsExplicit()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content><extension id=\"ws_3750545906\" enabled=\"true\" sync=\"true\" /></content>");
        temp.AddExtension("ws_3750545906", "Native Hotkey API");
        var service = new X4ProfileService();
        var catalog = ExtensionCatalog.Discover(temp.ExtensionsRoot, service.Read(profile));
        service.Apply(profile, temp.BackupsRoot, catalog, new Dictionary<string, bool> { ["ws_3750545906"] = false }, isGameRunning: () => false);
        var disabled = service.Read(profile).Extensions["ws_3750545906"];
        Assert(!disabled.Enabled && disabled.Sync == false, "unmodded Native Hotkey API state was not explicit");
        service.Apply(profile, temp.BackupsRoot, catalog, new Dictionary<string, bool> { ["ws_3750545906"] = true }, isGameRunning: () => false);
        var enabled = service.Read(profile).Extensions["ws_3750545906"];
        Assert(enabled.Enabled && enabled.Sync == true, "modded Native Hotkey API state was not explicit");
    }

    private static void OptionalDependenciesAreMarked()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content />");
        var folder = Path.Combine(temp.ExtensionsRoot, "native");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.xml"), "<content id=\"native\" name=\"Native\"><dependency id=\"missing_support\" optional=\"true\" /></content>");
        var entry = ExtensionCatalog.Discover(temp.ExtensionsRoot, new X4ProfileService().Read(profile)).Single();
        Assert(entry.Dependencies.Count == 1 && entry.Dependencies[0].Optional, "optional dependency flag was not preserved");
    }

    private static void ExtensionVersionsAreDiscovered()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content />");
        var folder = Path.Combine(temp.ExtensionsRoot, "versioned");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.xml"), "<content id=\"versioned\" name=\"Versioned Extension\" version=\"2.7.1\" />");
        var entry = ExtensionCatalog.Discover(temp.ExtensionsRoot, new X4ProfileService().Read(profile)).Single();
        Assert(entry.Version == "2.7.1", "extension version was not read from content.xml");
    }

    private static void RestoreReturnsOriginal()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content><extension id=\"alpha\" enabled=\"false\" /></content>");
        temp.AddExtension("alpha", "Alpha Extension");
        var originalHash = Hash(profile);
        var service = new X4ProfileService();
        var catalog = ExtensionCatalog.Discover(temp.ExtensionsRoot, service.Read(profile));
        var result = service.Apply(profile, temp.BackupsRoot, catalog, new Dictionary<string, bool> { ["alpha"] = true }, isGameRunning: () => false);
        Assert(Hash(profile) != originalHash, "apply did not change the fixture profile");
        service.Restore(profile, result.BackupPath, isGameRunning: () => false);
        Assert(Hash(profile) == originalHash, "restore did not return the original profile");
    }

    private static void AutoDiscoveryIsNonFatal()
    {
        var settings = SettingsStore.WithDiscoveredDefaults(new X4LauncherSettings());
        Assert(settings is not null, "auto-discovery returned no settings object");
    }

    private static void SteamLibraryPathDiscovery()
    {
        var settings = SettingsStore.WithDiscoveredDefaults(new X4LauncherSettings());
        Assert(!string.IsNullOrWhiteSpace(settings.X4Root), "auto-discovery did not return an X4 root candidate");
    }

    private static void DeprecatedEntriesCanBeRemoved()
    {
        using var temp = new TempWorkspace();
        var profile = temp.WriteProfile("<content><metadata keep=\"true\" /><extension id=\"old_extension\" enabled=\"false\" /><extension id=\"alpha\" enabled=\"true\" /></content>");
        temp.AddExtension("alpha", "Alpha Extension");
        var service = new X4ProfileService();
        var catalog = ExtensionCatalog.Discover(temp.ExtensionsRoot, service.Read(profile));
        var old = catalog.Single(entry => entry.Id == "old_extension");
        Assert(!old.IsAvailable && !old.IsDlc, "old profile entry should be deprecated");
        var result = service.RemoveDeprecated(profile, temp.BackupsRoot, new[] { old.Id }, isGameRunning: () => false);
        var after = service.Read(profile);
        Assert(!after.Extensions.ContainsKey(old.Id), "deprecated profile entry was not removed");
        Assert(after.Extensions.ContainsKey("alpha"), "unrelated installed extension was removed");
        Assert(File.Exists(result.BackupPath), "cleanup did not create a backup");
    }

    private static void ModProfilesPersist()
    {
        using var temp = new TempWorkspace();
        var settingsPath = Path.Combine(temp.Root, "settings.json");
        var store = new SettingsStore(settingsPath);
        var settings = new X4LauncherSettings
        {
            ModProfiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Story Mods"] = new() { "alpha", "ws_3750545906" }
            },
            SelectedProfileName = "Story Mods"
        };
        store.Save(settings);
        var loaded = store.Load();
        Assert(loaded.SelectedProfileName == "Story Mods", "selected profile name was not persisted");
        Assert(loaded.ModProfiles.TryGetValue("Story Mods", out var ids) && ids.SequenceEqual(new[] { "alpha", "ws_3750545906" }), "profile extension IDs were not persisted");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "X4ModLauncherTests", Guid.NewGuid().ToString("N"));
            ExtensionsRoot = Path.Combine(Root, "extensions");
            BackupsRoot = Path.Combine(Root, "backups");
            Directory.CreateDirectory(ExtensionsRoot);
            Directory.CreateDirectory(BackupsRoot);
        }

        public string Root { get; }
        public string ExtensionsRoot { get; }
        public string BackupsRoot { get; }

        public string WriteProfile(string xml)
        {
            var path = Path.Combine(Root, "content.xml");
            File.WriteAllText(path, xml);
            return path;
        }

        public string AddExtension(string id, string name)
        {
            var folder = Path.Combine(ExtensionsRoot, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "content.xml"), $"<content id=\"{id}\" name=\"{name}\" />");
            return folder;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
