namespace ReplayPanel.Supervisor.BundleTools;

/// <summary>
/// Replaces the shipped artifacts of a bundle (config.toml, blobs, ui/ ...) with freshly staged files.
/// The sources (panel_src) and the runtime data in vfs/ are never removed; vfs files from the stage only fill gaps.
/// </summary>
public static class BundleSwap
{
    private static bool Preserved(string name) => name.Equals(BundleLayout.SourceFolder, StringComparison.Ordinal) || name.Equals("vfs", StringComparison.Ordinal);

    public static string? Backup(string target, string backupsRoot, string name, int keep, Action<string> log)
    {
        if (!Directory.Exists(target)) return null;
        var entries = new DirectoryInfo(target).GetFileSystemInfos().Where(e => !Preserved(e.Name)).ToArray();
        if (entries.Length == 0) return null;
        var directory = Path.Combine(backupsRoot, name, DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(directory);
        foreach (var entry in entries) CopyEntry(entry, Path.Combine(directory, entry.Name));
        log($"Backup of the previous bundle files: {directory}");
        var all = Directory.GetDirectories(Path.Combine(backupsRoot, name)).OrderDescending(StringComparer.Ordinal).ToArray();
        foreach (var old in all.Skip(Math.Max(1, keep))) try { Directory.Delete(old, true); } catch (IOException) { }
        return directory;
    }

    public static string? LatestBackup(string backupsRoot, string name)
    {
        var directory = Path.Combine(backupsRoot, name);
        return Directory.Exists(directory) ? Directory.GetDirectories(directory).OrderDescending(StringComparer.Ordinal).FirstOrDefault() : null;
    }

    /// <summary>Replaces the artifacts in <paramref name="target"/> with the content of <paramref name="stage"/>.</summary>
    public static void Apply(string stage, string target, Action<string> log)
    {
        Directory.CreateDirectory(target);
        foreach (var existing in new DirectoryInfo(target).GetFileSystemInfos().Where(e => !Preserved(e.Name)))
        {
            if (existing is DirectoryInfo dir) dir.Delete(true); else existing.Delete();
        }
        foreach (var entry in new DirectoryInfo(stage).GetFileSystemInfos())
        {
            if (entry.Name.Equals(BundleLayout.SourceFolder, StringComparison.Ordinal)) continue;
            var destination = Path.Combine(target, entry.Name);
            if (entry.Name.Equals("vfs", StringComparison.Ordinal) && entry is DirectoryInfo vfs) { SeedMissing(vfs, destination); continue; }
            if (entry is DirectoryInfo directory) Directory.Move(directory.FullName, destination); else File.Move(entry.FullName, destination, true);
        }
        log("Bundle files replaced.");
    }

    private static void SeedMissing(DirectoryInfo source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in source.GetFiles()) { var path = Path.Combine(destination, file.Name); if (!File.Exists(path)) file.CopyTo(path); }
        foreach (var dir in source.GetDirectories()) SeedMissing(dir, Path.Combine(destination, dir.Name));
    }

    private static void CopyEntry(FileSystemInfo entry, string destination)
    {
        if (entry is FileInfo file) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); file.CopyTo(destination, true); return; }
        var directory = (DirectoryInfo)entry;
        Directory.CreateDirectory(destination);
        foreach (var child in directory.GetFileSystemInfos()) CopyEntry(child, Path.Combine(destination, child.Name));
    }

    public static void CopyDirectory(string source, string destination) => CopyEntry(new DirectoryInfo(source), destination);
}
