namespace X4ModLauncher.Core;

/// <summary>The toggle states a mod profile would produce, plus installed extensions the profile does not mention.</summary>
public sealed record ProfilePreview(IReadOnlyDictionary<string, bool> DesiredStates, IReadOnlyList<string> UnlistedIds);

public static class ModProfileLogic
{
    public const string NativeProfileName = "Native";

    /// <summary>Built-in profile with every toggleable extension off.</summary>
    public static ModProfile Native { get; } = new() { Name = NativeProfileName, Enabled = new(), Disabled = null };

    /// <summary>
    /// Works out the desired state of every toggleable extension for a profile. Extensions the profile does not
    /// mention keep their current state and are reported as unlisted, so a newly installed mod is never switched
    /// off silently. Profiles saved before v1.1 (no disabled list) keep their original "everything else off" meaning.
    /// </summary>
    public static ProfilePreview Preview(ModProfile profile, IEnumerable<ExtensionEntry> catalog)
    {
        var enabled = profile.Enabled.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabled = profile.Disabled?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var unlisted = new List<string>();
        foreach (var entry in catalog.Where(entry => entry.CanToggle))
        {
            if (enabled.Contains(entry.Id))
                desired[entry.Id] = true;
            else if (disabled is null || disabled.Contains(entry.Id))
                desired[entry.Id] = false;
            else
            {
                desired[entry.Id] = entry.CurrentEnabled;
                unlisted.Add(entry.Id);
            }
        }
        return new ProfilePreview(desired, unlisted);
    }

    /// <summary>True when the toggles are exactly what the profile would produce.</summary>
    public static bool Matches(ModProfile profile, IEnumerable<ExtensionEntry> catalog, IReadOnlyDictionary<string, bool> toggles)
    {
        var preview = Preview(profile, catalog);
        return preview.DesiredStates.All(pair => toggles.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    /// <summary>
    /// Records the toggle states into a profile. IDs the profile already lists that are not in the current catalog
    /// (for example a mod that is temporarily uninstalled) are kept so re-saving does not lose them.
    /// </summary>
    public static void Capture(ModProfile profile, IReadOnlyDictionary<string, bool> toggles)
    {
        var keptEnabled = profile.Enabled.Where(id => !toggles.ContainsKey(id));
        var keptDisabled = (profile.Disabled ?? new List<string>()).Where(id => !toggles.ContainsKey(id));
        profile.Enabled = Sorted(toggles.Where(pair => pair.Value).Select(pair => pair.Key).Concat(keptEnabled));
        profile.Disabled = Sorted(toggles.Where(pair => !pair.Value).Select(pair => pair.Key).Concat(keptDisabled)
            .Where(id => !profile.Enabled.Contains(id, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>The extension states that differ from the X4 profile and therefore need to be written.</summary>
    public static Dictionary<string, bool> ChangesToApply(IEnumerable<ExtensionEntry> catalog, IReadOnlyDictionary<string, bool> toggles)
    {
        var changes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Where(entry => entry.CanToggle))
        {
            if (!toggles.TryGetValue(entry.Id, out var desired))
                continue;
            var differs = !entry.CurrentIsPresent
                ? desired
                : desired != entry.CurrentEnabled
                    || (LegacyExtensionCatalog.IsNativeHotkeyApi(entry.Id) && entry.CurrentSync != desired);
            if (differs)
                changes[entry.Id] = desired;
        }
        return changes;
    }

    /// <summary>Normalized folder of an X4 profile content.xml, used to scope mod profiles.</summary>
    public static string? FolderKey(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return null;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(profilePath));
            return string.IsNullOrWhiteSpace(folder) ? null : folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Profiles that belong to the given X4 profile folder, plus unscoped ones.</summary>
    public static IEnumerable<ModProfile> InScope(IEnumerable<ModProfile> profiles, string? folderKey) =>
        profiles.Where(profile => profile.X4ProfileFolder is null
            || (folderKey is not null && profile.X4ProfileFolder.Equals(folderKey, StringComparison.OrdinalIgnoreCase)));

    public static ModProfile? Find(IEnumerable<ModProfile> profiles, string? folderKey, string name) =>
        name.Equals(NativeProfileName, StringComparison.OrdinalIgnoreCase)
            ? Native
            : InScope(profiles, folderKey).FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static List<string> Sorted(IEnumerable<string> ids) => ids
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
