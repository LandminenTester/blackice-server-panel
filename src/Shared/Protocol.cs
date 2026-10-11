using System.Text.Json;
using System.Security.Cryptography;
namespace ReplayPanel.Shared;
public sealed record Request(string Action, string? Command = null, string? ShutdownMode = null, bool? AutoStartServer = null, string? Locale = null);
public sealed record Reply(bool Ok, string? Error = null, ServerStatus? Status = null, string[]? Lines = null);
public sealed record ServerStatus(string State, int? Pid, string ServerRoot, string ShutdownMode, string? Detail = null, bool AutoStartServer = true);
public sealed class PanelConfig
{
    public List<PanelUser>? Users { get; set; }
    public int SchemaVersion { get; set; } = 2;
    public string DashboardName { get; set; } = "Unofficial BlackICE Server Webpanel";
    public string Username { get; set; } = "admin";
    public string Locale { get; set; } = "en";
    public string Backend { get; set; } = OperatingSystem.IsWindows() ? "windows" : "linux";
    public int GamePort { get; set; } = 20770;
    public int VoicePort { get; set; } = 20771;
    public string ContainerId { get; set; } = "";
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DockerSocket { get; set; } = "/var/run/docker.sock";
    public string ServerRoot { get; set; } = "";
    public string[] ServerArguments { get; set; } = [];
    public string PipeName { get; set; } = "replay-panel";
    public string ShutdownMode { get; set; } = "keep-running";
    public int StopTimeoutSeconds { get; set; } = 30;
    public bool AutoStartServer { get; set; } = true;
    public bool AutoRestart { get; set; }
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string Url { get; set; } = Environment.GetEnvironmentVariable("REPLAY_PANEL_URL") ?? "http://127.0.0.1:20772";
    public string[] AllowedHosts { get; set; } = Environment.GetEnvironmentVariable("REPLAY_PANEL_HOSTS")?.Split(';') ?? [];
    public string[] TrustedProxies { get; set; } = [];
    public string? PublicUrl { get; set; }
    public bool Configured => PasswordHash.Length > 0 && ServerRoot.Length > 0;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static PanelConfig Load(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        if (!File.Exists(file)) { var fresh = new PanelConfig(); Save(file, fresh); return fresh; }
        var text = File.ReadAllText(file);
        var c = JsonSerializer.Deserialize<PanelConfig>(text, Json) ?? throw new InvalidOperationException("config.invalid");
        if (!text.Contains("schemaVersion")) { if(!File.Exists(file+".v1.bak")) {try { File.Copy(file,file+".v1.bak",false); } catch(IOException) when(File.Exists(file+".v1.bak")) {} } c.SchemaVersion = 2; Save(file, c); }
        if (c.ServerRoot.Length > 0) c.ServerRoot = Path.GetFullPath(c.ServerRoot);
        if (c.ShutdownMode is not ("keep-running" or "stop-server") || c.StopTimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("config.invalid");
        if (!Uri.TryCreate(c.Url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || (!uri.IsLoopback && c.AllowedHosts.Length == 0)) throw new InvalidOperationException("config.invalid");
        return c;
    }
    public static void Save(string file, PanelConfig config)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, file, true);
    }
    public void SetPassword(string password)
    {
        PasswordSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(PasswordSalt), 210000, HashAlgorithmName.SHA256, 32));
    }
    public bool VerifyPassword(string password) => Configured && CryptographicOperations.FixedTimeEquals(
        Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(PasswordSalt), 210000, HashAlgorithmName.SHA256, 32), Convert.FromBase64String(PasswordHash));
}
