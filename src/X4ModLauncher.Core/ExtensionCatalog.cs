using System.Text.RegularExpressions;
using System.Xml;

namespace X4ModLauncher.Core;

public static class ExtensionCatalog
{
    public static IReadOnlyList<ExtensionEntry> Discover(string extensionsRoot, ProfileSnapshot profile)
    {
        var results = new Dictionary<string, ExtensionEntry>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(extensionsRoot))
        {
            foreach (var folder in Directory.EnumerateDirectories(extensionsRoot))
            {
                var metadataPath = Path.Combine(folder, "content.xml");
                if (!File.Exists(metadataPath))
                    continue;
                try
                {
                    var metadata = ReadMetadata(metadataPath, Path.GetFileName(folder));
                    profile.Extensions.TryGetValue(metadata.Id, out var existing);
                    results[metadata.Id] = new ExtensionEntry
                    {
                        Id = metadata.Id,
                        Name = metadata.Name,
                        Version = metadata.Version,
                        FolderName = Path.GetFileName(folder),
                        FolderPath = folder,
                        IsDlc = metadata.IsDlc,
                        IsAvailable = true,
                        CurrentIsPresent = existing is not null,
                        CurrentEnabled = existing?.Enabled ?? false,
                        CurrentSync = existing?.Sync,
                        Dependencies = metadata.Dependencies
                    };
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    // The folder is installed but its metadata is unreadable. Keep it in the catalog as installed and
                    // read-only so its profile entry is neither toggled nor offered for deprecated-entry cleanup.
                    var folderName = Path.GetFileName(folder);
                    var id = TryReadIdLeniently(metadataPath) ?? folderName;
                    profile.Extensions.TryGetValue(id, out var existing);
                    results[id] = new ExtensionEntry
                    {
                        Id = id,
                        Name = id,
                        Version = "Unknown",
                        FolderName = folderName,
                        FolderPath = folder,
                        IsDlc = IsDlcIdentifier(id) || IsDlcIdentifier(folderName),
                        IsAvailable = true,
                        CurrentIsPresent = existing is not null,
                        CurrentEnabled = existing?.Enabled ?? false,
                        CurrentSync = existing?.Sync,
                        Dependencies = Array.Empty<ExtensionDependency>(),
                        MetadataError = ex.Message
                    };
                }
            }
        }

        foreach (var state in profile.Extensions.Values)
        {
            if (results.ContainsKey(state.Id))
                continue;
            results[state.Id] = new ExtensionEntry
            {
                Id = state.Id,
                Name = state.Id,
                Version = "Unknown",
                FolderName = state.Id,
                FolderPath = Path.Combine(extensionsRoot, state.Id),
                IsDlc = IsDlcIdentifier(state.Id),
                IsAvailable = false,
                CurrentIsPresent = true,
                CurrentEnabled = state.Enabled,
                CurrentSync = state.Sync,
                Dependencies = Array.Empty<ExtensionDependency>()
            };
        }

        return results.Values.OrderBy(x => x.IsDlc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string Id, string Name, string Version, bool IsDlc, IReadOnlyList<ExtensionDependency> Dependencies) ReadMetadata(string path, string folderName)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(path);
        var root = document.DocumentElement ?? throw new XmlException("Missing content root.");
        var id = root.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(id))
            id = folderName;
        var name = root.GetAttribute("name");
        if (string.IsNullOrWhiteSpace(name))
            name = id;
        var version = root.GetAttribute("version");
        if (string.IsNullOrWhiteSpace(version))
            version = "Unknown";
        var type = root.GetAttribute("type");
        var dependencies = root.ChildNodes.OfType<XmlElement>()
            .Where(x => x.Name.Equals("dependency", StringComparison.OrdinalIgnoreCase))
            .Select(x => new ExtensionDependency(
                x.GetAttribute("id"),
                x.HasAttribute("version") ? x.GetAttribute("version") : null,
                x.GetAttribute("optional").Equals("true", StringComparison.OrdinalIgnoreCase)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
        return (id, name, version, IsDlcIdentifier(id) || IsDlcIdentifier(folderName) || type.Equals("dlc", StringComparison.OrdinalIgnoreCase), dependencies);
    }

    // Best-effort read of <content id="..."> from metadata that is not well-formed XML.
    private static string? TryReadIdLeniently(string path)
    {
        try
        {
            var match = Regex.Match(File.ReadAllText(path), "<content\\b[^>]*?\\sid\\s*=\\s*[\"'](?<id>[^\"']+)[\"']", RegexOptions.IgnoreCase);
            return match.Success && !string.IsNullOrWhiteSpace(match.Groups["id"].Value) ? match.Groups["id"].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsDlcIdentifier(string value) => value.StartsWith("ego_dlc_", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase);
}
