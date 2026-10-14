using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

/// <summary>Paths and naming rules shared by every bundle-tools component (EXPERIMENTAL feature).</summary>
public static partial class BundleLayout
{
    /// <summary>
    /// Folder in the server root that holds the source projects, one sub-folder per bundle name (server/panel_src/NAME).
    /// It lives next to data/ and not inside data/ugc/NAME because REServer scans a bundle folder recursively: any config.toml below a bundle
    /// (for example data/config.toml in a template) makes it report "Invalid bundle nesting" and refuse the whole bundle.
    /// </summary>
    public const string SourceFolder = "panel_src";

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z")] private static partial Regex NameRule();
    public static bool ValidName(string? name) => name is not null && NameRule().IsMatch(name);
    public static string RequireName(string? name) => ValidName(name) ? name! : throw new InvalidOperationException("bundle.invalid");

    public static string StateDir(string configPath) => Path.GetDirectoryName(Path.GetFullPath(configPath))!;
    public static string Uploads(string configPath) => Path.Combine(StateDir(configPath), "uploads");
    public static string Stage(string configPath) => Path.Combine(StateDir(configPath), "bundle-stage");
    public static string Backups(string configPath) => Path.Combine(StateDir(configPath), "bundle-backups");
    public static string Jobs(string configPath) => Path.Combine(StateDir(configPath), "bundle-jobs");
    public static string Toolchain(string configPath) => Path.Combine(StateDir(configPath), "toolchain");
    public static string Registry(string configPath) => Path.Combine(StateDir(configPath), "bundle-projects.json");

    public static string UgcRoot(PanelConfig config)
    {
        if (!config.Configured) throw new InvalidOperationException("setup.required");
        return Path.GetFullPath(Path.Combine(config.ServerRoot, "data", "ugc"));
    }

    /// <summary>Resolves a bundle folder and guarantees it stays inside data/ugc and is not a link.</summary>
    public static string BundleDir(PanelConfig config, string name)
    {
        RequireName(name);
        var root = UgcRoot(config);
        var directory = Path.GetFullPath(Path.Combine(root, name));
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("bundle.invalid");
        BundlePaths.Require(directory);
        BundlePaths.Require(directory);
        return directory;
    }

    public static string SourceRoot(PanelConfig config)
    {
        if (!config.Configured) throw new InvalidOperationException("setup.required");
        return Path.GetFullPath(Path.Combine(config.ServerRoot, SourceFolder));
    }

    public static string SourceDir(PanelConfig config, string name)
    {
        RequireName(name);
        var root = SourceRoot(config);
        var directory = Path.GetFullPath(Path.Combine(root, name));
        if (!IsPathInside(root, directory)) throw new InvalidOperationException("bundle.invalid");
        if (Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is not null) throw new InvalidOperationException("bundleTools.path");
        return directory;
    }

    public static bool IsPathInside(string root, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
