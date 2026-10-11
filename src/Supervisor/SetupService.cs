using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed record SetupInput(string Key, string DashboardName, string Username, string Password, string Confirmation, string Locale, string ServerRoot, string Backend);
public sealed record InstallInput(string Key, string ServerRoot, string Backend);
public sealed record InstallJob(string Id, string State, string Phase, long Bytes, string? Error = null);
public sealed class SetupService(PanelConfig config, string configPath)
{
    private readonly string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim completion = new(1);
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private InstallJob? job;
    public void PrintKey() { if (!config.Configured) Console.WriteLine("Setup key: " + key); }
    public void Authorize(string supplied)
    {
        if (config.Configured) throw new InvalidOperationException("setup.completed");
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied ?? ""), System.Text.Encoding.UTF8.GetBytes(key))) throw new InvalidOperationException("setup.key");
    }
    private static string Root(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException("path.absolute");
        var root = Path.GetFullPath(path);
        for(var ancestor=new DirectoryInfo(root);ancestor is not null;ancestor=ancestor.Parent) if(ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("path.mount");
        var allowed = Environment.GetEnvironmentVariable("REPLAY_PANEL_MOUNTS");
        if (allowed is not null && !allowed.Split(';').Any(x => root == Path.GetFullPath(x) || root.StartsWith(Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new InvalidOperationException("path.mount");
        return root;
    }
    public object Folders(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) {
            var mounts = Environment.GetEnvironmentVariable("REPLAY_PANEL_MOUNTS");
            return new { path = "", parent = (string?)null, directories = mounts?.Split(';') ?? (OperatingSystem.IsWindows() ? Directory.GetLogicalDrives() : ["/"]) };
        }
        var path = Root(input);
        if (!Directory.Exists(path)) throw new InvalidOperationException("path.missing");
        return new { path, parent = Environment.GetEnvironmentVariable("REPLAY_PANEL_MOUNTS")?.Split(';').Contains(path)==true ? null : Directory.GetParent(path)?.FullName, directories = Directory.EnumerateDirectories(path).Where(x => { try { return (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0; } catch { return false; } }).Order().Take(500).ToArray() };
    }
    public static void Validate(string path, string backend, bool fresh = false)
    {
        path = Root(path);
        if (backend is not ("windows" or "linux" or "docker")) throw new InvalidOperationException("platform.invalid");
        if (backend == "windows" != OperatingSystem.IsWindows()) throw new InvalidOperationException("platform.invalid");
        if (!Directory.Exists(path)) throw new InvalidOperationException("path.missing");
        if (fresh && Directory.EnumerateFileSystemEntries(path).Any()) throw new InvalidOperationException("install.exists");
        var probe = Path.Combine(path, ".panel-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(probe)) {} File.Delete(probe); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new InvalidOperationException("path.readonly"); }
        if (fresh || backend == "docker") return;
        var exe = Path.Combine(path, backend == "windows" ? "REServer.exe" : "REServer");
        if (!File.Exists(exe) || !File.Exists(Path.Combine(path, "cfg", "server.cfg"))) throw new InvalidOperationException("server.files");
        using var file = File.OpenRead(exe);
        var magic = new byte[4]; file.ReadExactly(magic);
        if (backend == "windows" ? magic[0] != 'M' || magic[1] != 'Z' : magic[0] != 127 || magic[1] != 'E' || magic[2] != 'L' || magic[3] != 'F') throw new InvalidOperationException("server.platform");
        if (backend == "linux" && !OperatingSystem.IsWindows()) {
            if ((File.GetUnixFileMode(exe) & UnixFileMode.UserExecute) == 0) throw new InvalidOperationException("server.executable");
            var start = new System.Diagnostics.ProcessStartInfo("ldd") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(exe); start.Environment["LD_LIBRARY_PATH"] = path;
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0 || output.Contains("not found")) throw new InvalidOperationException("server.libraries");
        }
    }
    public InstallJob Start(InstallInput input)
    {
        Authorize(input.Key); var target = Root(input.ServerRoot);
        Directory.CreateDirectory(target); Validate(target, input.Backend, true);
        lock (sync) {
            if (job?.State == "running") throw new InvalidOperationException("install.busy");
            cancellation = new(); job = new(Guid.NewGuid().ToString("N"), "running", "download", 0);
            var token = cancellation.Token;
            _ = Task.Run(async () => {
                var staging = target + ".install-" + job.Id;
                string finalState="completed"; string? failure=null;
                try {
                    Directory.CreateDirectory(staging);
                    if (input.Backend == "docker") {
                        var docker = new DockerEngine(config); config.ContainerId = await docker.CreateServer(target, token); PanelConfig.Save(configPath,config); await docker.InitializeFiles(staging,token);
                    } else if (input.Backend == "linux") await OciInstaller.ExtractSeed(staging, value => Update("download", value), token);
                    else await DownloadWindows(staging, token);
                    token.ThrowIfCancellationRequested(); Update("verify", 0);
                    if (input.Backend != "docker") {
                        var cfg = Path.Combine(staging,"cfg"); Directory.CreateDirectory(cfg);
                        File.Copy(Path.Combine(cfg,"server.cfg.example"), Path.Combine(cfg,"server.cfg"), false);
                        await File.AppendAllTextAsync(Path.Combine(cfg,"server.cfg"),"\nsv_lan true\n",token);
                        Directory.CreateDirectory(Path.Combine(staging,"settings"));
                        await File.WriteAllTextAsync(Path.Combine(staging,"settings","server.settings.json"), "{\"auto_update\":false,\"channel\":\"alpha\",\"restart_after_update\":\"auto\"}", token);
                        Validate(staging,input.Backend);
                        
                    }
                    if (Directory.EnumerateFileSystemEntries(target).Any()) throw new InvalidOperationException("install.exists");
                    token.ThrowIfCancellationRequested();Directory.Delete(target);Directory.Move(staging,target);
                } catch (OperationCanceledException) { finalState="cancelled"; }
                  catch (Exception e) { Console.Error.WriteLine(e);finalState="failed"; failure=e is InvalidOperationException ? e.Message : "install.failed"; }
                finally {
                    if(finalState!="completed" && input.Backend=="docker" && config.ContainerId.Length>0){try{await new DockerEngine(config).Remove();config.ContainerId="";PanelConfig.Save(configPath,config);}catch(Exception e){Console.Error.WriteLine(e);}}
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    lock(sync)job=job! with {State=finalState,Phase=finalState,Error=failure};
                }
            });
            return job;
        }
    }
    private void Update(string phase,long bytes) { lock(sync) job=job! with {Phase=phase,Bytes=bytes}; }
    public InstallJob? Status() { lock(sync) return job; }
    public void Cancel() { lock(sync) cancellation?.Cancel(); }
    private async Task DownloadWindows(string staging,CancellationToken ct)
    {
        using var client = new HttpClient { Timeout=TimeSpan.FromMinutes(30) };
        using var response=await client.GetAsync("https://artifacts.replay.re/server_windows/reserver.zip",HttpCompletionOption.ResponseHeadersRead,ct); response.EnsureSuccessStatusCode();
        var zip=staging+".zip";
        try {
            await using(var output=File.Create(zip)) { await using var input=await response.Content.ReadAsStreamAsync(ct); var buffer=new byte[65536]; long bytes=0; int count;
                while((count=await input.ReadAsync(buffer,ct))>0) { bytes+=count; if(bytes>4L*1024*1024*1024) throw new InvalidOperationException("install.size"); await output.WriteAsync(buffer.AsMemory(0,count),ct); Update("download",bytes); } }
            Update("extract",0); ArchiveInstaller.Extract(zip,staging,ct);
        } finally { if(File.Exists(zip)) File.Delete(zip); }
    }
    public async Task Finish(SetupInput input)
    {
        Authorize(input.Key); await completion.WaitAsync();
        try {
            Authorize(input.Key);
            if(string.IsNullOrWhiteSpace(input.DashboardName) || input.DashboardName.Length>80 || !System.Text.RegularExpressions.Regex.IsMatch(input.Username,@"\A[A-Za-z0-9_.-]{3,32}\z") || input.Password.Length is <12 or >256 || input.Password!=input.Confirmation || input.Locale is not ("en" or "de")) throw new InvalidOperationException("setup.validation");
            if(Status()?.State=="running") throw new InvalidOperationException("install.busy");
            if(input.Backend=="docker" && config.ContainerId.Length==0)throw new InvalidOperationException("docker.identity");
            Validate(input.ServerRoot,input.Backend);
            var previous=JsonSerializer.Serialize(config,PanelConfig.Json);
            config.ServerRoot=Root(input.ServerRoot); config.Backend=input.Backend; config.DashboardName=input.DashboardName.Trim(); config.Username=input.Username; config.Locale=input.Locale; config.SetPassword(input.Password);
            try {PanelConfig.Save(configPath,config);} catch {var restored=JsonSerializer.Deserialize<PanelConfig>(previous,PanelConfig.Json)!;config.PasswordHash=restored.PasswordHash;config.PasswordSalt=restored.PasswordSalt;config.ServerRoot=restored.ServerRoot;throw;}
        } finally { completion.Release(); }
    }
}
