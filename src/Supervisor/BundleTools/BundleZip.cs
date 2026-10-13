using System.IO.Compression;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record ZipCheck(string Id, string Status, string? Detail = null);
public sealed record ZipMeta(string? Author, string? Description, string? Version, string? ServerBlob, string? ClientBlob, bool HasUi, bool HasVfs);
public sealed record ZipReport(bool Ok, string Name, string Root, long Size, long Files, ZipMeta? Meta, ZipCheck[] Checks);

/// <summary>
/// Structure check for an uploaded bundle ZIP: is it laid out like a startable bundle (config.toml, referenced blobs and UI files present)?
/// It deliberately does not validate the code inside; it only refuses unsafe archives (traversal, links, size bombs).
/// </summary>
public static class BundleZip
{
    public const int MaxEntries = 20000;
    public const long MaxExpandedBytes = 4L * 1024 * 1024 * 1024;

    public static string? Normalize(string name)
    {
        name = name.Replace('\\', '/');
        while (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
        if (name.Length == 0 || name.StartsWith('/') || name.Contains(':') || name.Any(char.IsControl)) return null;
        var segments = name.Split('/');
        if (segments.Any(s => s == ".." || (s == "." && s.Length > 0))) return null;
        return name;
    }

    public static string SuggestName(string? originalFileName, string? wrappedFolder)
    {
        static string Clean(string value)
        {
            var cleaned = Regex.Replace(value, @"[^A-Za-z0-9_.-]+", "-").Trim('-', '.', '_');
            return cleaned.Length > 100 ? cleaned[..100] : cleaned;
        }
        var fromFolder = wrappedFolder is null ? "" : Clean(wrappedFolder);
        if (BundleLayout.ValidName(fromFolder)) return fromFolder;
        var stem = originalFileName is null ? "" : Clean(Path.GetFileNameWithoutExtension(originalFileName));
        return BundleLayout.ValidName(stem) ? stem : "";
    }

    private static bool Glob(string pattern, string path)
    {
        var regex = "^" + Regex.Escape(pattern).Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*").Replace("\u0001", ".*").Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(path, regex);
    }

    public static ZipReport Inspect(string zipPath, string? originalFileName, long maxBytes)
    {
        var checks = new List<ZipCheck>();
        ZipReport Fail(string id, string detail) { checks.Add(new(id, "fail", detail)); return new(false, "", "", 0, 0, null, [.. checks]); }
        var info = new FileInfo(zipPath);
        if (!info.Exists) return Fail("zip.missing", "The file was not found.");
        if (info.Length > maxBytes) return Fail("zip.size", "The ZIP is larger than the configured upload limit.");
        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zipPath); }
        catch (Exception e) when (e is InvalidDataException or IOException) { return Fail("zip.invalid", "This is not a readable ZIP file."); }
        using (archive)
        {
            if (archive.Entries.Count > MaxEntries) return Fail("zip.entries", "The ZIP has too many entries.");
            var files = new List<(string Path, long Length)>();
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) return Fail("zip.unsafe", "The ZIP contains a symbolic link.");
                var name = Normalize(entry.FullName);
                if (name is null) return Fail("zip.unsafe", "The ZIP contains an unsafe path.");
                if (name.EndsWith('/')) continue;
                expanded += entry.Length;
                if (expanded > MaxExpandedBytes) return Fail("zip.size", "The ZIP would expand to more than 4 GB.");
                files.Add((name, entry.Length));
            }
            if (files.Count == 0) return Fail("zip.empty", "The ZIP is empty.");

            // The manifest sits at the archive root, or inside one single wrapping folder.
            string root;
            string? wrapped = null;
            if (files.Any(f => f.Path.Equals("config.toml", StringComparison.Ordinal))) root = "";
            else
            {
                var tops = files.Select(f => f.Path.Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
                if (tops.Length == 1 && files.Any(f => f.Path.Equals(tops[0] + "/config.toml", StringComparison.Ordinal))) { root = tops[0] + "/"; wrapped = tops[0]; }
                else return Fail("manifest.missing", "No config.toml found at the root of the ZIP (or inside a single top-level folder).");
            }
            checks.Add(new("manifest.found", "ok", root.Length == 0 ? "config.toml at the ZIP root" : "config.toml inside " + root.TrimEnd('/')));

            var relative = files.Where(f => f.Path.StartsWith(root, StringComparison.Ordinal)).Select(f => f.Path[root.Length..]).ToHashSet(StringComparer.Ordinal);
            var manifestEntry = archive.GetEntry(root + "config.toml")!;
            if (manifestEntry.Length > 256 * 1024) return Fail("manifest.size", "config.toml is unusually large.");
            string text;
            using (var reader = new StreamReader(manifestEntry.Open())) text = reader.ReadToEnd();
            var doc = TomlDoc.Parse(text);

            var version = doc.String("Meta", "config_version");
            checks.Add(version == "alpha_playtest" ? new("manifest.version", "ok", "config_version alpha_playtest") : new("manifest.version", "warn", "[Meta] config_version is not \"alpha_playtest\"."));

            var serverBlob = doc.String("Scripting", "server_blob");
            var clientBlob = doc.String("Scripting", "client_blob");
            foreach (var (label, blob) in new[] { ("server_blob", serverBlob), ("client_blob", clientBlob) })
            {
                if (string.IsNullOrEmpty(blob)) continue;
                var path = Normalize(blob);
                checks.Add(path is not null && relative.Contains(path) ? new("blob." + label, "ok", blob) : new("blob." + label, "fail", $"{label} \"{blob}\" is not in the ZIP."));
            }

            var uiFiles = doc.Array("ReUi", "files");
            var initial = doc.String("ReUi", "initial_page");
            foreach (var pattern in uiFiles)
            {
                var normalized = Normalize(pattern);
                if (normalized is null) { checks.Add(new("ui.files", "fail", $"Unsafe ReUi file entry \"{pattern}\".")); continue; }
                var isGlob = normalized.Contains('*') || normalized.Contains('?');
                if (!isGlob) checks.Add(relative.Contains(normalized) ? new("ui.file", "ok", normalized) : new("ui.file", "fail", $"ReUi file \"{pattern}\" is not in the ZIP."));
                else checks.Add(relative.Any(r => Glob(normalized, r)) ? new("ui.glob", "ok", normalized) : new("ui.glob", "warn", $"ReUi pattern \"{pattern}\" matches no file."));
            }
            if (uiFiles.Length > 0 && !string.IsNullOrEmpty(initial) && !initial.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var contentRoot = doc.String("ReUi", "content_root") ?? "";
                var page = Normalize(contentRoot.TrimEnd('/') + "/" + initial) ?? initial;
                checks.Add(relative.Contains(page.TrimStart('/')) || relative.Contains(initial) ? new("ui.initial", "ok", initial) : new("ui.initial", "warn", $"initial_page \"{initial}\" was not found."));
            }
            if (string.IsNullOrEmpty(serverBlob) && string.IsNullOrEmpty(clientBlob) && uiFiles.Length == 0)
                checks.Add(new("bundle.empty", "warn", "The manifest names no server blob, client blob or UI files."));
            if (relative.Any(r => r.StartsWith(BundleLayout.SourceFolder + "/", StringComparison.Ordinal)))
                checks.Add(new("bundle.sources", "warn", $"The ZIP contains a {BundleLayout.SourceFolder} folder; it is ignored (sources are managed as projects)."));

            var meta = new ZipMeta(doc.String("General", "author"), doc.String("General", "description"), doc.String("General", "version"), serverBlob, clientBlob,
                uiFiles.Length > 0, relative.Any(r => r.StartsWith("vfs/", StringComparison.Ordinal)));
            var suggested = SuggestName(originalFileName, wrapped);
            return new(checks.All(c => c.Status != "fail"), suggested, root, info.Length, files.Count(f => f.Path.StartsWith(root, StringComparison.Ordinal)), meta, [.. checks]);
        }
    }

    /// <summary>Extracts the bundle (below the detected root) into an empty staging folder with the same safety rules as the inspection.</summary>
    public static void Extract(string zipPath, string root, string stage, CancellationToken ct)
    {
        stage = Path.GetFullPath(stage);
        Directory.CreateDirectory(stage);
        using var archive = ZipFile.OpenRead(zipPath);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = Normalize(entry.FullName) ?? throw new InvalidOperationException("bundleTools.zipUnsafe");
            if (!name.StartsWith(root, StringComparison.Ordinal)) continue;
            var relative = name[root.Length..];
            if (relative.Length == 0) continue;
            if (relative == BundleLayout.SourceFolder || relative.StartsWith(BundleLayout.SourceFolder + "/", StringComparison.Ordinal)) continue;
            var target = Path.GetFullPath(Path.Combine(stage, relative));
            if (!BundleLayout.IsPathInside(stage, target) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidOperationException("bundleTools.zipUnsafe");
            if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            expanded += entry.Length;
            if (expanded > MaxExpandedBytes) throw new InvalidOperationException("bundleTools.zipSize");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }
    }
}
