using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor;
public sealed record ServerConfigValues(string Name, string Description, string Dlc, string Region, string BindAddress, string Endpoint, bool Lan, string VoipAddress = "", bool Privacy = false, bool UgcAutomaticRestarts = false, string Channel = "alpha", bool AutoUpdate = false, string RestartAfterUpdate = "auto");
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
        bool Flag(string key) => Get(key).ToLowerInvariant() is "true" or "1";
        var settings = ReadSettings(root);
        return new(File.Exists(path), new(Get("sv_name"), Get("sv_desc"), Get("sv_dlc"), Get("sv_region"), Get("sv_bindaddr"), Get("sv_endpoint"), Flag("sv_lan"), Get("sv_voip_addr"), Flag("sv_privacy"), Flag("sv_ugc_automatic_restarts"),
            settings?["channel"]?.GetValue<string>() ?? "alpha", settings?["auto_update"]?.GetValue<bool>() ?? false, settings?["restart_after_update"]?.GetValue<string>() ?? "auto"), running);
    }
    public static void Write(string root, ServerConfigValues v)
    {
        Check(v.Name, 1, 128, "serverConfig.name"); Check(v.Description, 0, 512, "serverConfig.description");
        if (!Dlc.IsMatch(v.Dlc)) throw new InvalidOperationException("serverConfig.dlc");
        if (!Region.IsMatch(v.Region)) throw new InvalidOperationException("serverConfig.region");
        if (!Address.IsMatch(v.BindAddress) || !Address.IsMatch(v.Endpoint)) throw new InvalidOperationException("serverConfig.address");
        if (!Address.IsMatch(v.VoipAddress)) throw new InvalidOperationException("serverConfig.address");
        if (!Token.IsMatch(v.Channel) || v.Channel.Length == 0 || !Token.IsMatch(v.RestartAfterUpdate) || v.RestartAfterUpdate.Length == 0) throw new InvalidOperationException("serverConfig.settings");
        var path = PathFor(root);
        if (!File.Exists(path)) throw new InvalidOperationException("serverConfig.missing");
        var raw = File.ReadAllText(path);
        var eol = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        Apply(lines, "sv_name", v.Name, true); Apply(lines, "sv_desc", v.Description, true);
        Apply(lines, "sv_dlc", v.Dlc, true); Apply(lines, "sv_region", v.Region, true);
        Apply(lines, "sv_bindaddr", v.BindAddress, true); Apply(lines, "sv_endpoint", v.Endpoint, true);
        Apply(lines, "sv_voip_addr", v.VoipAddress, true);
        Apply(lines, "sv_lan", v.Lan ? "true" : "false", false);
        Apply(lines, "sv_privacy", v.Privacy ? "true" : "false", false);
        Apply(lines, "sv_ugc_automatic_restarts", v.UgcAutomaticRestarts ? "true" : "false", false);
        var backup = path + ".panel.bak";
        if (!File.Exists(backup)) File.Copy(path, backup);
        var temp = path + ".panel.tmp";
        File.WriteAllText(temp, string.Join(eol, lines) + eol, new UTF8Encoding(false));
        File.Move(temp, path, true);
        WriteSettings(root, v);
    }
    private static readonly Regex Token = new(@"\A[a-z0-9_-]{0,32}\z", RegexOptions.Compiled);
    private static string SettingsPath(string root) => Path.Combine(root, "settings", "server.settings.json");
    private static JsonObject? ReadSettings(string root)
    {
        try { return File.Exists(SettingsPath(root)) ? JsonNode.Parse(File.ReadAllText(SettingsPath(root))) as JsonObject : null; } catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }
    private static void WriteSettings(string root, ServerConfigValues v)
    {
        var path = SettingsPath(root);
        var node = ReadSettings(root) ?? [];
        node["channel"] = v.Channel; node["auto_update"] = v.AutoUpdate; node["restart_after_update"] = v.RestartAfterUpdate;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".panel.tmp";
        File.WriteAllText(temp, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
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
