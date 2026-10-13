using System.Text;
namespace ReplayPanel.Supervisor.BundleTools;

/// <summary>
/// Small tolerant reader for the subset of TOML used by bundle manifests (tables, strings, booleans, numbers and string arrays).
/// It never throws on malformed input; unknown constructs are skipped. It is for structure checks and display, not for validation of semantics.
/// </summary>
public sealed class TomlDoc
{
    private readonly Dictionary<string, Dictionary<string, object>> tables = new(StringComparer.OrdinalIgnoreCase);

    public static TomlDoc Parse(string text)
    {
        var doc = new TomlDoc();
        var section = "";
        doc.tables[section] = new(StringComparer.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                if (end < 0) continue;
                section = line[1..end].Trim().Trim('[', ']').Trim();
                if (!doc.tables.ContainsKey(section)) doc.tables[section] = new(StringComparer.Ordinal);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().Trim('"');
            var value = line[(eq + 1)..].Trim();
            if (value.StartsWith('['))
            {
                var buffer = new StringBuilder(value);
                while (!BracketsBalanced(buffer.ToString()) && i + 1 < lines.Length) buffer.Append(' ').Append(StripComment(lines[++i]).Trim());
                doc.tables[section][key] = ParseArray(buffer.ToString());
            }
            else if (ParseScalar(value) is { } scalar) doc.tables[section][key] = scalar;
        }
        return doc;
    }

    public string? String(string table, string key) => tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v) ? v as string : null;
    public bool? Bool(string table, string key) => tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v) ? v as bool? : null;
    public string[] Array(string table, string key) => tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v) && v is string[] a ? a : [];
    public bool HasTable(string table) => tables.ContainsKey(table);

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0') { if (c == '\\' && quote == '"') i++; else if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '#') return line[..i];
        }
        return line;
    }

    private static bool BracketsBalanced(string value)
    {
        var depth = 0; var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0') { if (c == '\\' && quote == '"') i++; else if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '[') depth++;
            else if (c == ']') depth--;
        }
        return depth <= 0;
    }

    private static object? ParseScalar(string value)
    {
        if (value.Length >= 2 && value[0] == '"') return ReadQuoted(value, 0, out _);
        if (value.Length >= 2 && value[0] == '\'') { var end = value.IndexOf('\'', 1); return end > 0 ? value[1..end] : null; }
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return long.TryParse(value, out var number) ? number : null;
    }

    private static string[] ParseArray(string value)
    {
        var result = new List<string>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"') { result.Add(ReadQuoted(value, i, out var next)); i = next; }
            else if (value[i] == '\'') { var end = value.IndexOf('\'', i + 1); if (end < 0) break; result.Add(value[(i + 1)..end]); i = end; }
        }
        return [.. result];
    }

    private static string ReadQuoted(string value, int start, out int end)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        for (; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var n = value[++i];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => n });
            }
            else if (c == '"') break;
            else sb.Append(c);
        }
        end = i;
        return new string(sb.ToString().Where(ch => !char.IsControl(ch) || ch is '\n' or '\t').ToArray());
    }
}
