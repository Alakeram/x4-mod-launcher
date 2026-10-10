using System.Text;
using System.Xml;

namespace X4ModLauncher.Core;

public sealed class DuplicateExtensionIdException : Exception
{
    public DuplicateExtensionIdException(string id)
        : base($"The profile contains more than one extension entry for ID '{id}'. No changes were written.")
    {
    }
}

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(string message) : base(message) { }
}

public sealed class X4ProfileService
{
    public ProfileSnapshot Read(string profilePath)
    {
        if (!File.Exists(profilePath))
            throw new FileNotFoundException($"The profile content file was not found: {profilePath}", profilePath);

        var raw = File.ReadAllText(profilePath, Encoding.UTF8);
        var document = LoadDocument(raw, profilePath);
        var states = ReadStates(document, profilePath);
        return new ProfileSnapshot(profilePath, raw, states);
    }

    public ApplyResult Apply(
        string profilePath,
        string backupDirectory,
        IReadOnlyCollection<ExtensionEntry> availableExtensions,
        IReadOnlyDictionary<string, bool> desiredStates,
        bool requireGameClosed = true,
        Func<bool>? isGameRunning = null,
        string? backupLabel = null)
    {
        if (requireGameClosed && isGameRunning?.Invoke() == true)
            throw new InvalidOperationException("X4 is running. Close the game before applying extension changes.");

        var snapshot = Read(profilePath);
        var document = LoadDocument(snapshot.RawXml, profilePath);
        var extensionNodes = GetExtensionNodes(document, profilePath);
        var availableById = availableExtensions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var desired in desiredStates)
        {
            if (!availableById.TryGetValue(desired.Key, out var extension))
                throw new InvalidOperationException($"Extension '{desired.Key}' is not available in the discovered catalog.");
            if (!extension.IsAvailable)
                throw new InvalidOperationException($"Extension '{extension.Name}' is selected but its folder is unavailable.");
            if (extension.IsDlc)
                throw new InvalidOperationException($"DLC extension '{extension.Name}' is read-only in this launcher.");
        }

        var changes = new List<ApplyChange>();
        foreach (var desired in desiredStates)
        {
            var extension = availableById[desired.Key];
            var nodes = extensionNodes.Where(x => x.GetAttribute("id").Equals(extension.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            XmlElement node;
            bool? previousEnabled;
            bool? previousSync;
            if (nodes.Count > 1)
                throw new DuplicateExtensionIdException(extension.Id);

            if (nodes.Count == 0)
            {
                node = document.CreateElement("extension");
                node.SetAttribute("id", extension.Id);
                document.DocumentElement!.AppendChild(node);
                previousEnabled = null;
                previousSync = null;
            }
            else
            {
                node = nodes[0];
                previousEnabled = ParseBooleanAttribute(node, "enabled");
                previousSync = ParseNullableBooleanAttribute(node, "sync");
            }

            node.SetAttribute("enabled", desired.Value ? "true" : "false");
            bool? desiredSync = null;
            if (LegacyExtensionCatalog.IsNativeHotkeyApi(extension.Id))
            {
                desiredSync = desired.Value;
                node.SetAttribute("sync", desired.Value ? "true" : "false");
            }

            changes.Add(new ApplyChange(extension.Id, previousEnabled, desired.Value, previousSync, desiredSync));
        }

        var tempPath = profilePath + $".x4modlauncher-{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(backupDirectory);
        var backupPath = CreateBackupPath(backupDirectory, profilePath, backupLabel);
        WriteDocument(document, tempPath);
        var replaced = false;

        try
        {
            var verification = Read(tempPath);
            VerifyDesiredStates(verification, desiredStates, profilePath);

            File.Copy(profilePath, backupPath, overwrite: false);
            ReplaceAtomically(tempPath, profilePath);
            replaced = true;

            var afterReplace = Read(profilePath);
            VerifyDesiredStates(afterReplace, desiredStates, profilePath);
            return new ApplyResult(profilePath, backupPath, changes);
        }
        catch
        {
            TryDelete(tempPath);
            if (replaced && File.Exists(backupPath))
            {
                try { File.Copy(backupPath, profilePath, overwrite: true); }
                catch { /* Preserve the original exception; the backup remains available for manual recovery. */ }
            }
            throw;
        }
    }

    public void Restore(string profilePath, string backupPath, bool requireGameClosed = true, Func<bool>? isGameRunning = null)
    {
        if (requireGameClosed && isGameRunning?.Invoke() == true)
            throw new InvalidOperationException("X4 is running. Close the game before restoring a profile.");
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"Backup file not found: {backupPath}", backupPath);

        var backup = Read(backupPath);
        var tempPath = profilePath + $".x4modlauncher-restore-{Guid.NewGuid():N}.tmp";
        File.Copy(backupPath, tempPath, overwrite: false);
        try
        {
            _ = Read(tempPath);
            ReplaceAtomically(tempPath, profilePath);
            _ = Read(profilePath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public ProfileCleanupResult RemoveDeprecated(
        string profilePath,
        string backupDirectory,
        IReadOnlyCollection<string> deprecatedIds,
        bool requireGameClosed = true,
        Func<bool>? isGameRunning = null,
        string? backupLabel = null)
    {
        if (requireGameClosed && isGameRunning?.Invoke() == true)
            throw new InvalidOperationException("X4 is running. Close the game before removing deprecated profile entries.");

        var snapshot = Read(profilePath);
        var document = LoadDocument(snapshot.RawXml, profilePath);
        var ids = deprecatedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodes = GetExtensionNodes(document, profilePath)
            .Where(node => ids.Contains(node.GetAttribute("id")))
            .ToList();
        var removedIds = nodes.Select(node => node.GetAttribute("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (removedIds.Count == 0)
            throw new InvalidOperationException("No deprecated extension entries were found in the selected profile.");

        foreach (var node in nodes)
            node.ParentNode?.RemoveChild(node);

        var tempPath = profilePath + $".x4modlauncher-cleanup-{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(backupDirectory);
        var backupPath = CreateBackupPath(backupDirectory, profilePath, backupLabel);
        WriteDocument(document, tempPath);
        var replaced = false;
        try
        {
            var verification = Read(tempPath);
            foreach (var id in removedIds)
            {
                if (verification.Extensions.ContainsKey(id))
                    throw new IOException($"Verification failed while removing deprecated extension '{id}'. The original profile was not replaced.");
            }

            File.Copy(profilePath, backupPath, overwrite: false);
            ReplaceAtomically(tempPath, profilePath);
            replaced = true;
            var afterReplace = Read(profilePath);
            foreach (var id in removedIds)
            {
                if (afterReplace.Extensions.ContainsKey(id))
                    throw new IOException($"Verification failed after removing deprecated extension '{id}'. The original profile was restored.");
            }
            return new ProfileCleanupResult(profilePath, backupPath, removedIds);
        }
        catch
        {
            TryDelete(tempPath);
            if (replaced && File.Exists(backupPath))
            {
                try { File.Copy(backupPath, profilePath, overwrite: true); }
                catch { /* Preserve the original exception; the backup remains available for manual recovery. */ }
            }
            throw;
        }
    }

    private static XmlDocument LoadDocument(string raw, string path)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        try
        {
            document.LoadXml(raw);
        }
        catch (XmlException ex)
        {
            throw new ProfileValidationException($"The profile is not valid XML: {path}. {ex.Message}");
        }

        if (document.DocumentElement is null || !document.DocumentElement.Name.Equals("content", StringComparison.OrdinalIgnoreCase))
            throw new ProfileValidationException($"Unexpected XML root in profile: {path}. Expected <content>.");
        return document;
    }

    private static IReadOnlyDictionary<string, ProfileExtensionState> ReadStates(XmlDocument document, string path)
    {
        var nodes = GetExtensionNodes(document, path);
        var states = new Dictionary<string, ProfileExtensionState>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var id = node.GetAttribute("id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (states.ContainsKey(id))
                throw new DuplicateExtensionIdException(id);
            states[id] = new ProfileExtensionState(id, ParseBooleanAttribute(node, "enabled") ?? true, ParseNullableBooleanAttribute(node, "sync"));
        }
        return states;
    }

    private static List<XmlElement> GetExtensionNodes(XmlDocument document, string path)
    {
        if (document.DocumentElement is null)
            throw new ProfileValidationException($"Profile has no document element: {path}");
        return document.DocumentElement.ChildNodes.OfType<XmlElement>()
            .Where(x => x.Name.Equals("extension", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool? ParseNullableBooleanAttribute(XmlElement node, string name)
    {
        if (!node.HasAttribute(name))
            return null;
        return ParseBooleanAttribute(node, name);
    }

    private static bool? ParseBooleanAttribute(XmlElement node, string name)
    {
        if (!node.HasAttribute(name))
            return null;
        var value = node.GetAttribute(name);
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new ProfileValidationException($"Extension '{node.GetAttribute("id")}' has invalid {name}='{value}'.");
    }

    private static void VerifyDesiredStates(ProfileSnapshot snapshot, IReadOnlyDictionary<string, bool> desiredStates, string profilePath)
    {
        foreach (var desired in desiredStates)
        {
            if (!snapshot.Extensions.TryGetValue(desired.Key, out var state) || state.Enabled != desired.Value)
                throw new IOException($"Verification failed for extension '{desired.Key}'. The original profile was not replaced: {profilePath}");
            if (LegacyExtensionCatalog.IsNativeHotkeyApi(desired.Key) && state.Sync != desired.Value)
                throw new IOException($"Verification failed for Native Hotkey API Workshop sync. The original profile was not replaced: {profilePath}");
        }
    }

    private static void WriteDocument(XmlDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = true
        };
        using var writer = XmlWriter.Create(path, settings);
        document.Save(writer);
    }

    private static string CreateBackupPath(string backupDirectory, string profilePath, string? backupLabel)
    {
        var fallback = Path.GetFileName(Path.GetDirectoryName(profilePath)) ?? "X4Profile";
        var label = SanitizeFileName(string.IsNullOrWhiteSpace(backupLabel) ? fallback : backupLabel);
        var timestamp = DateTimeOffset.Now.ToString("MM-dd-yyyy_HH-mm-ss");
        var basePath = Path.Combine(backupDirectory, $"{label}-Backup-{timestamp}");
        var path = basePath + ".xml";
        var suffix = 2;
        while (File.Exists(path))
            path = $"{basePath}-{suffix++}.xml";
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "X4Profile" : sanitized;
    }

    private static void ReplaceAtomically(string tempPath, string destinationPath)
    {
        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
