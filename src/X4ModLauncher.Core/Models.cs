namespace X4ModLauncher.Core;

public sealed record ExtensionDependency(string Id, string? Version = null, bool Optional = false);

public sealed class ExtensionEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string FolderName { get; init; }
    public required string FolderPath { get; init; }
    public required bool IsDlc { get; init; }
    public required bool IsAvailable { get; init; }
    public required bool CurrentIsPresent { get; init; }
    public required bool CurrentEnabled { get; init; }
    public required bool? CurrentSync { get; init; }
    public required IReadOnlyList<ExtensionDependency> Dependencies { get; init; }
    public string AvailabilityLabel => IsAvailable ? "Installed" : "Folder not found";
}

public sealed record ApplyChange(string Id, bool? PreviousEnabled, bool DesiredEnabled, bool? PreviousSync, bool? DesiredSync);

public sealed record ApplyResult(string ProfilePath, string BackupPath, IReadOnlyList<ApplyChange> Changes);

public sealed record ProfileCleanupResult(string ProfilePath, string BackupPath, IReadOnlyList<string> RemovedIds);

public sealed record ProfileExtensionState(string Id, bool Enabled, bool? Sync);

public sealed record ProfileSnapshot(string Path, string RawXml, IReadOnlyDictionary<string, ProfileExtensionState> Extensions);

public sealed class X4LauncherSettings
{
    public string? X4Root { get; set; }
    public string? ProfilePath { get; set; }
    public string? ExtensionsRoot { get; set; }
    public string? BackupDirectory { get; set; }
    public bool LaunchAfterApply { get; set; }
    public Dictionary<string, List<string>> ModProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SelectedProfileName { get; set; }
}

public static class LegacyExtensionCatalog
{
    public static readonly IReadOnlySet<string> DefaultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ws_2042901274",
        "ws_3715253556",
        "ws_3750545906",
        "ship_equipment_salvaging",
        "ws_3770927339",
        "pirates_code",
        "kuerteeUIExtensionsAndHUD",
        "ws_3477279743"
    };

    public static bool IsNativeHotkeyApi(string id) => id.Equals("ws_3750545906", StringComparison.OrdinalIgnoreCase);
}
