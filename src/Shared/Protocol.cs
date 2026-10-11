using System.Text.Json;
namespace ReplayPanel.Shared;
public sealed record Request(string Action, string? Command = null, string? ShutdownMode = null);
public sealed record Reply(bool Ok, string? Error = null, ServerStatus? Status = null, string[]? Lines = null);
public sealed record ServerStatus(string State, int? Pid, string ServerRoot, string ShutdownMode, string? Detail = null);
public sealed class PanelConfig
{
    public string ServerRoot { get; set; } = "";
    public string[] ServerArguments { get; set; } = [];
    public string PipeName { get; set; } = "replay-panel";
    public string ShutdownMode { get; set; } = "keep-running";
    public int StopTimeoutSeconds { get; set; } = 30;
    public bool AutoRestart { get; set; } = false;
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string Url { get; set; } = "http://127.0.0.1:20772";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public static PanelConfig Load(string file)
    {
        var c = JsonSerializer.Deserialize<PanelConfig>(File.ReadAllText(file), Json) ?? throw new InvalidOperationException("Invalid config.");
        c.ServerRoot = Path.GetFullPath(c.ServerRoot);
        if (c.ShutdownMode is not ("keep-running" or "stop-server")) throw new InvalidOperationException("Invalid shutdown mode.");
        if (c.StopTimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("Invalid stop timeout.");
        if (!Uri.TryCreate(c.Url, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http") throw new InvalidOperationException("Local release binds to loopback HTTP only; use the documented proxy architecture for future hosting.");
        return c;
    }
}
