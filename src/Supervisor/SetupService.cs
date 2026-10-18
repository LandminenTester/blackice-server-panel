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
    // 8 characters from an unambiguous alphabet (no 0/O/1/I/L).
    private const string KeyAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int MaxKeyFailures = 5;
    private readonly object keyLock = new();
    private string key = NewKey();
    private int keyFailures;
    private static string NewKey() => string.Create(8, 0, (span, _) => { for (var i = 0; i < span.Length; i++) span[i] = KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)]; });
    private readonly SemaphoreSlim completion = new(1);
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private InstallJob? job;
    public void PrintKey() { if (!config.Configured) Console.WriteLine("Setup key: " + key); }
    /// <summary>The key is shown in the browser terminal only to a genuinely local request; remote visitors are pointed at the supervisor console.</summary>
    public string[] Banner(bool local)
    {
        if (config.Configured) throw new InvalidOperationException("setup.completed");
        lock (keyLock)
            return local ? ["BlackICE supervisor ready.", "Setup key: " + key] : ["BlackICE supervisor ready.", "Setup key hidden for remote access. Read it in the Supervisor console or in .local/supervisor.log."];
    }
    public void Authorize(string supplied)
    {
        if (config.Configured) throw new InvalidOperationException("setup.completed");
        var candidate = (supplied ?? "").Trim().ToUpperInvariant();
        lock (keyLock)
        {
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(candidate), System.Text.Encoding.UTF8.GetBytes(key))) { keyFailures = 0; return; }
            if (++keyFailures < MaxKeyFailures) throw new InvalidOperationException("setup.key");
            // Brute-force protection: too many wrong guesses invalidate the key and print a fresh one to the supervisor console only.
            key = NewKey(); keyFailures = 0;
            Console.WriteLine("Too many wrong setup keys - the key was regenerated.");
            Console.WriteLine("Setup key: " + key);
            throw new InvalidOperationException("setup.locked");
        }
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
    public string DefaultRoot()
    {
        var mounts = Environment.GetEnvironmentVariable("REPLAY_PANEL_MOUNTS")?.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (mounts is { Length: > 0 }) return Path.GetFullPath(mounts[0]);
        var directory = Path.GetDirectoryName(configPath)!;
        var panelRoot = string.Equals(Path.GetFileName(directory), ".local", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(directory) ?? directory : directory;
        return Path.Combine(panelRoot, "server");
    }
    private readonly object pickSync = new();
    private string pickState = "idle", pickPath = "";
    public void PickStart(string initial)
    {
        lock (pickSync) { if (pickState == "running") return; pickState = "running"; pickPath = ""; }
        _ = Task.Run(() => {
            string state = "cancelled", path = "";
            try { (state, path) = NativePicker.Pick(string.IsNullOrWhiteSpace(initial) || !Path.IsPathFullyQualified(initial) ? DefaultRoot() : initial); }
            catch { state = "unavailable"; }
            lock (pickSync) { pickState = state; pickPath = path; }
        });
    }
    public object PickStatus() { lock (pickSync) return new { state = pickState, path = pickPath }; }
    public static void Validate(string path, string backend, bool fresh = false)
    {
        path = Root(path);
        if (backend is not ("windows" or "linux" or "docker")) throw new InvalidOperationException("platform.invalid");
        if (backend == "windows" != OperatingSystem.IsWindows()) throw new InvalidOperationException("platform.invalid");
        if (!Directory.Exists(path)) throw new InvalidOperationException("path.missing");
        if (fresh && !EmptyInstallDirectory(path)) throw new InvalidOperationException("install.exists");
        var probe = Path.Combine(path, ".panel-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(probe)) {} File.Delete(probe); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new InvalidOperationException("path.readonly"); }
        if (fresh || backend == "docker") return;
        var exe = Path.Combine(path, backend == "windows" ? "REServer.exe" : "REServer");
        if (!File.Exists(exe) || !File.Exists(Path.Combine(path, "cfg", "server.cfg"))) throw new InvalidOperationException("server.files");
        using var file = File.OpenRead(exe);
        var magic = new byte[4]; file.ReadExactly(magic);
        if (backend == "windows" ? magic[0] != 'M' || magic[1] != 'Z' : magic[0] != 127 || magic[1] != 'E' || magic[2] != 'L' || magic[3] != 'F') throw new InvalidOperationException("server.platform");
        if (backend == "linux" && !OperatingSystem.IsWindows()) {
            var mode = File.GetUnixFileMode(exe);
            if ((mode & UnixFileMode.UserExecute) == 0 || (mode & (UnixFileMode.OtherWrite | UnixFileMode.GroupWrite)) != 0) throw new InvalidOperationException("server.executable");
            var start = new System.Diagnostics.ProcessStartInfo("ldd") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(exe); start.Environment["LD_LIBRARY_PATH"] = path;
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0 || output.Contains("not found")) throw new InvalidOperationException("server.libraries");
        }
    }
    private static bool EmptyInstallDirectory(string path,string? staging=null){
        bool Empty(string directory){foreach(var entry in Directory.EnumerateFileSystemEntries(directory)){
            if(entry==staging)continue;
            if(!Directory.Exists(entry)||(File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0||Path.GetRelativePath(path,entry).Replace('\\','/') is not ("cfg" or "settings" or "data" or "data/ugc")||!Empty(entry))return false;
        }return true;}
        return Empty(path);
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
                var mountRoot=Environment.GetEnvironmentVariable("REPLAY_PANEL_MOUNTS")?.Split(';').Any(x=>Path.GetFullPath(x)==target)==true;
                var staging = mountRoot?Path.Combine(target,".panel-install-"+job.Id):target + ".install-" + job.Id;
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
                        if(config.GamePort is <1 or >65535||config.VoicePort is <1 or >65535||config.GamePort==config.VoicePort)throw new InvalidOperationException("setup.validation");
                        if(config.GamePort!=20770)await File.AppendAllTextAsync(Path.Combine(cfg,"server.cfg"),$"\nsv_bindaddr \"0.0.0.0:{config.GamePort}\"\n",token);
                        if(config.VoicePort!=20771)await File.AppendAllTextAsync(Path.Combine(cfg,"server.cfg"),$"\nsv_voip_addr \"0.0.0.0:{config.VoicePort}\"\n",token);
                        Directory.CreateDirectory(Path.Combine(staging,"settings"));
                        await File.WriteAllTextAsync(Path.Combine(staging,"settings","server.settings.json"), "{\"auto_update\":false,\"channel\":\"alpha\",\"restart_after_update\":\"auto\"}", token);
                        Validate(staging,input.Backend);
                        
                    }
                    if (!EmptyInstallDirectory(target,mountRoot?staging:null)) throw new InvalidOperationException("install.exists");
                    token.ThrowIfCancellationRequested();
                    if(mountRoot){
                        // A bind-mount root cannot be renamed or replaced. Publish
                        // verified entries inside it, retaining its ACL/identity.
                        foreach(var entry in Directory.EnumerateFileSystemEntries(staging)){
                            var destination=Path.Combine(target,Path.GetFileName(entry));
                            if(Directory.Exists(destination))Directory.Delete(destination,true);
                            if(Directory.Exists(entry))Directory.Move(entry,destination);else File.Move(entry,destination);
                        }
                        Directory.Delete(staging);
                    }else{Directory.Delete(target,true);Directory.Move(staging,target);}
                } catch (OperationCanceledException) { finalState="cancelled"; }
                  catch (Exception e) { Console.Error.WriteLine(e);finalState="failed"; failure=e is InvalidOperationException ? e.Message : "install.failed"; }
                finally {
                    if(finalState!="completed" && input.Backend=="docker" && config.ContainerId.Length>0){try{await new DockerEngine(config).Remove();config.ContainerId="";PanelConfig.Save(configPath,config);}catch(Exception e){Console.Error.WriteLine(e);}}
                    try {if (Directory.Exists(staging)) Directory.Delete(staging,true);}catch(Exception e){Console.Error.WriteLine(e);finalState="failed";failure="install.failed";}
                    lock(sync)job=job! with {State=finalState,Phase=finalState,Error=failure};
                }
            });
            return job;
        }
    }
    private void Update(string phase,long bytes) { lock(sync) job=job! with {Phase=phase,Bytes=bytes}; }
    public InstallJob? Status() { lock(sync) return job; }
    public void Cancel() { lock(sync) cancellation?.Cancel(); }
    public async Task StopInstallation(){Cancel();while(Status()?.State=="running")await Task.Delay(100);}
    // Installs the current official build from the same alpha manifest the update check uses, so a fresh install is never older than "latest".
    private async Task DownloadWindows(string staging,CancellationToken ct)
    {
        const string prefix = "https://artifacts.replay.re/server_windows/alpha/";
        using var client = new HttpClient { Timeout=TimeSpan.FromMinutes(30) };
        using var version = JsonDocument.Parse(await client.GetStringAsync(prefix + "version.json", ct));
        var commit = version.RootElement.GetProperty("commit").GetString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(commit, "^[a-f0-9]{8,40}$")) throw new InvalidOperationException("install.failed");
        var files = JsonSerializer.Deserialize<Dictionary<string,string>>(await client.GetStringAsync(prefix + "contents.json", ct))!;
        files.Remove("data/ugc/.gitkeep"); // Official empty-directory marker; the user's bundle directory is created empty.
        if (!files.ContainsKey("REServer.exe")) throw new InvalidOperationException("install.failed");
        long total = 0;
        foreach (var (name, digest) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (name.Length == 0 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(x => x is "" or "." or "..")) throw new InvalidOperationException("install.failed");
            var path = Path.GetFullPath(Path.Combine(staging, name));
            if (!path.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("install.failed");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var response = await client.GetAsync(prefix + string.Join('/', name.Split('/').Select(Uri.EscapeDataString)), HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using (var output = File.Create(path)) { await using var input = await response.Content.ReadAsStreamAsync(ct); var buffer = new byte[65536]; long bytes = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0) { bytes += count; total += count; if (bytes > 512L*1024*1024 || total > 4L*1024*1024*1024) throw new InvalidOperationException("install.size"); await output.WriteAsync(buffer.AsMemory(0, count), ct); Update("download", total); } }
            await using (var check = File.OpenRead(path)) if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(check, ct)), digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("install.failed");
        }
        Update("extract", 0);
        foreach (var dir in new[] { "logs", "cache", Path.Combine("data", "ugc") }) Directory.CreateDirectory(Path.Combine(staging, dir));
        await File.WriteAllTextAsync(Path.Combine(staging, "contents.json"), JsonSerializer.Serialize(files), ct);
        await File.WriteAllTextAsync(Path.Combine(staging, "version.json"), JsonSerializer.Serialize(new { commit }), ct);
    }
    public async Task Finish(SetupInput input)
    {
        Authorize(input.Key); await completion.WaitAsync();
        try {
            Authorize(input.Key);
            if(string.IsNullOrWhiteSpace(input.DashboardName) || input.DashboardName.Length>80 || !System.Text.RegularExpressions.Regex.IsMatch(input.Username,@"\A[A-Za-z0-9_.-]{3,32}\z") || input.Password.Length is <8 or >256 || input.Password!=input.Confirmation || input.Locale is not ("en" or "de")) throw new InvalidOperationException("setup.validation");
            if(Status()?.State=="running") throw new InvalidOperationException("install.busy");
            if(input.Backend=="docker" && config.ContainerId.Length==0)throw new InvalidOperationException("docker.identity");
            Validate(input.ServerRoot,input.Backend);
            var previous=JsonSerializer.Serialize(config,PanelConfig.Json);
            config.ServerRoot=Root(input.ServerRoot); config.Backend=input.Backend; config.DashboardName=input.DashboardName.Trim(); config.Username=input.Username; config.Locale=input.Locale; config.SetPassword(input.Password);
            try {PanelConfig.Save(configPath,config);} catch {var restored=JsonSerializer.Deserialize<PanelConfig>(previous,PanelConfig.Json)!;config.PasswordHash=restored.PasswordHash;config.PasswordSalt=restored.PasswordSalt;config.ServerRoot=restored.ServerRoot;throw;}
        } finally { completion.Release(); }
    }
}
