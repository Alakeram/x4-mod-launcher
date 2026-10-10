using System.Globalization;
using System.Text.RegularExpressions;

namespace X4ModLauncher.Core;

public static class BackupRetention
{
    // Matches only files this launcher created: <label>-Backup-MM-dd-yyyy_HH-mm-ss[-N].xml
    private static readonly Regex BackupName = new(
        @"-Backup-(?<stamp>\d{2}-\d{2}-\d{4}_\d{2}-\d{2}-\d{2})(?:-(?<suffix>\d+))?\.xml$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Deletes the oldest launcher backups so that at most <paramref name="keep"/> remain. Age comes from the
    /// timestamp in the file name, because File.Copy preserves the profile's original write time.
    /// <paramref name="protectedPath"/> (normally the backup just created) is never deleted. Files that cannot
    /// be deleted are skipped. A keep value of 0 or less keeps everything.
    /// </summary>
    public static IReadOnlyList<string> Prune(string backupDirectory, int keep, string? protectedPath = null)
    {
        if (keep <= 0 || !Directory.Exists(backupDirectory))
            return Array.Empty<string>();

        List<(string Path, DateTime Stamp, int Suffix)> backups;
        try
        {
            backups = Directory.EnumerateFiles(backupDirectory, "*-Backup-*.xml", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Parsed: TryParse(Path.GetFileName(path))))
                .Where(item => item.Parsed is not null)
                .Select(item => (item.Path, item.Parsed!.Value.Stamp, item.Parsed!.Value.Suffix))
                .OrderByDescending(item => item.Stamp)
                .ThenByDescending(item => item.Suffix)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        var protectedFull = protectedPath is null ? null : Path.GetFullPath(protectedPath);
        var deleted = new List<string>();
        foreach (var backup in backups.Skip(keep))
        {
            if (protectedFull is not null && Path.GetFullPath(backup.Path).Equals(protectedFull, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.Delete(backup.Path);
                deleted.Add(backup.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or read-only backup is left in place; pruning is best-effort.
            }
        }
        return deleted;
    }

    public static (DateTime Stamp, int Suffix)? TryParse(string fileName)
    {
        var match = BackupName.Match(fileName);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["stamp"].Value, "MM-dd-yyyy_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
            return null;
        var suffix = match.Groups["suffix"].Success && int.TryParse(match.Groups["suffix"].Value, out var parsed) ? parsed : 1;
        return (stamp, suffix);
    }
}
