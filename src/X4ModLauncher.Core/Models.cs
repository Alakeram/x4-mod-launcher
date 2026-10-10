using System.Text.Json.Serialization;

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

    /// <summary>Set when the extension folder exists but its content.xml could not be read. Such entries are never toggled.</summary>
    public string? MetadataError { get; init; }

    public bool CanToggle => IsAvailable && !IsDlc && MetadataError is null;
    public string AvailabilityLabel => !IsAvailable ? "Folder not found" : MetadataError is null ? "Installed" : "Installed (metadata error)";
}

public sealed record ApplyChange(string Id, bool? PreviousEnabled, bool DesiredEnabled, bool? PreviousSync, bool? DesiredSync);

public sealed record ApplyResult(string ProfilePath, string BackupPath, IReadOnlyList<ApplyChange> Changes);

public sealed record ProfileCleanupResult(string ProfilePath, string BackupPath, IReadOnlyList<string> RemovedIds);

public sealed record ProfileExtensionState(string Id, bool Enabled, bool? Sync);

public sealed record ProfileSnapshot(string Path, string RawXml, IReadOnlyDictionary<string, ProfileExtensionState> Extensions);

public sealed class ModProfile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The X4 profile folder this mod profile belongs to, or null when it is shown for every X4 profile.</summary>
    public string? X4ProfileFolder { get; set; }

    public List<string> Enabled { get; set; } = new();

    /// <summary>
    /// Extensions saved as off. Null marks a profile saved before v1.1, which only recorded enabled
    /// extensions; for those, every extension not in <see cref="Enabled"/> is treated as off.
    /// </summary>
    public List<string>? Disabled { get; set; }
}

public sealed class X4LauncherSettings
{
    public const int DefaultBackupRetention = 20;

    public string? X4Root { get; set; }
    public string? ProfilePath { get; set; }
    public string? ExtensionsRoot { get; set; }
    public string? BackupDirectory { get; set; }
    public bool LaunchAfterApply { get; set; }
    public List<ModProfile> Profiles { get; set; } = new();
    public string? SelectedProfileName { get; set; }

    /// <summary>Number of newest launcher backups to keep in the backup folder; 0 keeps all.</summary>
    public int BackupRetention { get; set; } = DefaultBackupRetention;

    /// <summary>v1.0 profile format (name to enabled IDs). Read for migration only; never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? ModProfiles { get; set; }
}

public static class LegacyExtensionCatalog
{
    public static bool IsNativeHotkeyApi(string id) => id.Equals("ws_3750545906", StringComparison.OrdinalIgnoreCase);
}
