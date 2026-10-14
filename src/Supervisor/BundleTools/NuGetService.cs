using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record PackageRef(string File, string Id, string Version, bool Floating);

/// <summary>
/// Reads and changes the replay.blackice.* package versions of a bundle's sources. Nothing here runs by itself:
/// listing versions and applying one are explicit user actions.
/// </summary>
public sealed partial class NuGetService(HttpClient http)
{
    public const string FlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    public static readonly string[] ReplayIds = ["replay.blackice.server", "replay.blackice.client", "replay.blackice.shared"];
    private static readonly string[] CompilerIds = ["microsoft.dotnet.ilcompiler.llvm", "runtime.win-x64.microsoft.dotnet.ilcompiler.llvm", "runtime.linux-x64.microsoft.dotnet.ilcompiler.llvm"];

    [GeneratedRegex(@"<PackageReference\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex Tag();
    [GeneratedRegex("Include=\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex Include();
    [GeneratedRegex("Version=\"([^\"]*)\"", RegexOptions.IgnoreCase)] private static partial Regex VersionAttr();

    private static IEnumerable<string> ProjectFiles(string source) =>
        Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            : [];

    public static PackageRef[] Read(string source)
    {
        var result = new List<PackageRef>();
        foreach (var file in ProjectFiles(source))
        {
            string text;
            try { text = File.ReadAllText(file); } catch (IOException) { continue; }
            foreach (Match tag in Tag().Matches(text))
            {
                var id = Include().Match(tag.Value);
                var version = VersionAttr().Match(tag.Value);
                if (!id.Success || !version.Success || !id.Groups[1].Value.StartsWith("replay.blackice.", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new PackageRef(Path.GetRelativePath(source, file).Replace('\\', '/'), id.Groups[1].Value, version.Groups[1].Value, version.Groups[1].Value.Contains('*')));
            }
        }
        return [.. result];
    }

    public static bool IsStable(string version) => !version.Contains('-');

    private static int[] Core(string version) => [.. version.Split('-')[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : 0)];
    public static int Compare(string a, string b)
    {
        var x = Core(a); var y = Core(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        var stable=IsStable(a).CompareTo(IsStable(b));if(stable!=0)return stable;
        var ap=a.Split('+')[0].Split('-',2);var bp=b.Split('+')[0].Split('-',2);
        if(ap.Length<2||bp.Length<2)return 0;
        var al=ap[1].Split('.');var bl=bp[1].Split('.');
        for(var i=0;i<Math.Min(al.Length,bl.Length);i++){
            var an=long.TryParse(al[i],out var av);var bn=long.TryParse(bl[i],out var bv);
            var comparison=an&&bn?av.CompareTo(bv):an!=bn?(an?-1:1):StringComparer.OrdinalIgnoreCase.Compare(al[i],bl[i]);
            if(comparison!=0)return comparison;
        }return al.Length.CompareTo(bl.Length);
    }

    public async Task<string[]> VersionsAsync(string id, bool includePrerelease, CancellationToken ct)
    {
        if (!ReplayIds.Contains(id.ToLowerInvariant())) throw new InvalidOperationException("bundleTools.package");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(FlatContainer + id.ToLowerInvariant() + "/index.json", ct));
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).Where(v => includePrerelease || IsStable(v)).ToList();
        versions.Sort(Compare);
        return [.. versions];
    }

    /// <summary>Versions that exist for every replay package the project uses, newest last.</summary>
    public async Task<string[]> CommonVersionsAsync(IEnumerable<string> ids, bool includePrerelease, CancellationToken ct)
    {
        string[]? common = null;
        foreach (var id in ids.Select(i => i.ToLowerInvariant()).Distinct())
        {
            var versions = await VersionsAsync(id, includePrerelease, ct);
            common = common is null ? versions : [.. common.Intersect(versions, StringComparer.OrdinalIgnoreCase)];
        }
        return common ?? [];
    }

    /// <summary>The compiler package versions a replay.blackice.server release depends on (exact pins).</summary>
    public async Task<Dictionary<string, string>> CompilerPinsAsync(string version, CancellationToken ct)
    {
        var url = FlatContainer + "replay.blackice.server/" + version.ToLowerInvariant() + "/replay.blackice.server.nuspec";
        var xml = XDocument.Parse(await http.GetStringAsync(url, ct));
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in xml.Descendants().Where(e => e.Name.LocalName == "dependency"))
        {
            var id = dependency.Attribute("id")?.Value;
            var range = dependency.Attribute("version")?.Value;
            if (id is not null && range is not null && CompilerIds.Contains(id.ToLowerInvariant())) pins[id] = range.Trim('[', ']', '(', ')', ' ');
        }
        return pins;
    }

    /// <summary>Rewrites replay.blackice.* (and explicitly pinned compiler) package versions in every csproj below the source folder.</summary>
    public static int Apply(string source, string version, IReadOnlyDictionary<string, string> compilerPins)
    {
        if (!Regex.IsMatch(version, @"\A[0-9A-Za-z][0-9A-Za-z.+-]{0,60}\z")) throw new InvalidOperationException("bundleTools.package");
        var changed = 0;
        foreach (var file in ProjectFiles(source))
        {
            var text = File.ReadAllText(file);
            var updated = Tag().Replace(text, match =>
            {
                var id = Include().Match(match.Value);
                if (!id.Success) return match.Value;
                var name = id.Groups[1].Value;
                string? target = null;
                if (name.StartsWith("replay.blackice.", StringComparison.OrdinalIgnoreCase) && ReplayIds.Contains(name.ToLowerInvariant())) target = version;
                else if (compilerPins.TryGetValue(name, out var pin)) target = VersionAttr().Match(match.Value) is { Success: true } current && current.Groups[1].Value.StartsWith('[') ? "[" + pin + "]" : pin;
                if (target is null || !VersionAttr().IsMatch(match.Value)) return match.Value;
                return VersionAttr().Replace(match.Value, "Version=\"" + target + "\"", 1);
            });
            if (updated != text) { File.WriteAllText(file, updated); changed++; }
        }
        return changed;
    }
}
