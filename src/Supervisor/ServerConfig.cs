using System.Text;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor;
public sealed record ServerConfigValues(string Name, string Description, string Dlc, string Region, string BindAddress, string Endpoint, bool Lan);
public sealed record ServerConfigView(bool Exists, ServerConfigValues Values, bool ServerRunning);
/// <summary>Reads and edits the common keys of cfg/server.cfg while preserving comments and every other line.</summary>
public static class ServerConfig
{
    private static string PathFor(string root) => Path.Combine(root, "cfg", "server.cfg");
    private static readonly Regex Region = new(@"\A[A-Z]{3}\z", RegexOptions.Compiled);
    private static readonly Regex Dlc = new(@"\A[a-z0-9_-]{0,32}\z", RegexOptions.Compiled);
    private static readonly Regex Address = new(@"\A[A-Za-z0-9.:_\[\]-]{0,255}\z", RegexOptions.Compiled);
    public static ServerConfigView Read(string root, bool running)
    {
        var path = PathFor(root);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(path))
            foreach (var line in File.ReadAllLines(path))
                if (TryParse(line, out var key, out var value) && !map.ContainsKey(key)) map[key] = value;
        string Get(string key) => map.TryGetValue(key, out var v) ? v : "";
        var lan = Get("sv_lan").ToLowerInvariant() is "true" or "1";
        return new(File.Exists(path), new(Get("sv_name"), Get("sv_desc"), Get("sv_dlc"), Get("sv_region"), Get("sv_bindaddr"), Get("sv_endpoint"), lan), running);
    }
    public static void Write(string root, ServerConfigValues v)
    {
        Check(v.Name, 1, 128, "serverConfig.name"); Check(v.Description, 0, 512, "serverConfig.description");
        if (!Dlc.IsMatch(v.Dlc)) throw new InvalidOperationException("serverConfig.dlc");
        if (!Region.IsMatch(v.Region)) throw new InvalidOperationException("serverConfig.region");
        if (!Address.IsMatch(v.BindAddress) || !Address.IsMatch(v.Endpoint)) throw new InvalidOperationException("serverConfig.address");
        var path = PathFor(root);
        if (!File.Exists(path)) throw new InvalidOperationException("serverConfig.missing");
        var raw = File.ReadAllText(path);
        var eol = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        Apply(lines, "sv_name", v.Name, true); Apply(lines, "sv_desc", v.Description, true);
        Apply(lines, "sv_dlc", v.Dlc, true); Apply(lines, "sv_region", v.Region, true);
        Apply(lines, "sv_bindaddr", v.BindAddress, true); Apply(lines, "sv_endpoint", v.Endpoint, true);
        Apply(lines, "sv_lan", v.Lan ? "true" : "false", false);
        var backup = path + ".panel.bak";
        if (!File.Exists(backup)) File.Copy(path, backup);
        var temp = path + ".panel.tmp";
        File.WriteAllText(temp, string.Join(eol, lines) + eol, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
    private static void Check(string value, int min, int max, string error)
    {
        if (value.Length < min || value.Length > max || value.Any(c => char.IsControl(c) || c is '"' or '\\')) throw new InvalidOperationException(error);
    }
    private static bool TryParse(string line, out string key, out string value)
    {
        key = value = "";
        var m = Regex.Match(line, @"\A\s*([A-Za-z0-9_]+)\s+(?:""(.*)""|(\S+))\s*\z");
        if (!m.Success) return false;
        key = m.Groups[1].Value; value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return true;
    }
    private static void Apply(List<string> lines, string key, string value, bool quote)
    {
        var text = key + " " + (quote ? "\"" + value + "\"" : value);
        var active = new Regex(@"\A\s*" + key + @"(\s|\z)");
        var commented = new Regex(@"\A\s*#\s*" + key + @"(\s|\z)");
        var index = lines.FindIndex(l => active.IsMatch(l));
        if (index >= 0) {
            if (quote && value.Length == 0 && key is "sv_endpoint" or "sv_bindaddr") lines[index] = "#" + text; else lines[index] = text;
            return;
        }
        if (quote && value.Length == 0 && key is "sv_endpoint" or "sv_bindaddr") return;
        index = lines.FindIndex(l => commented.IsMatch(l));
        if (index >= 0) lines[index] = text; else lines.Add(text);
    }
}
