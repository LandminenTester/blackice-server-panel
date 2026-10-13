using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record ToolInfo(string Id, bool Installed, string? Version, string Source, int SizeMb, bool Installable, string? Note);

/// <summary>
/// Build tools the panel manages on its own so nobody has to open a shell: .NET SDK, git (portable MinGit on Windows, system git elsewhere),
/// Node (only needed for a web UI) and the WASI SDK (fetched by the Replay NuGet package into a panel-owned folder on the first build).
/// Downloads come from fixed official hosts and are verified against the hashes the vendors publish.
/// </summary>
public sealed class Toolchain(string directory, HttpClient http)
{
    public const string DotnetMetadata = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json";
    public const string GitReleases = "https://api.github.com/repos/git-for-windows/git/releases/latest";
    public const string NodeIndex = "https://nodejs.org/dist/latest-v24.x/";

    private static readonly string[] Components = ["git", "dotnet", "node", "wasi"];
    private string Dir(string id) => Path.Combine(directory, id);
    private static bool Windows => OperatingSystem.IsWindows();

    // ---------------------------------------------------------------- locations
    public string DotnetRoot => Dir("dotnet");
    public string? DotnetExe => Existing(Path.Combine(DotnetRoot, Windows ? "dotnet.exe" : "dotnet"));
    public string? NodeExe => Existing(Windows ? Path.Combine(Dir("node"), "node.exe") : Path.Combine(Dir("node"), "bin", "node"));
    public string? NpmCli => Existing(Windows ? Path.Combine(Dir("node"), "node_modules", "npm", "bin", "npm-cli.js") : Path.Combine(Dir("node"), "lib", "node_modules", "npm", "bin", "npm-cli.js"));
    public string WasiRoot => Dir("wasi-sdk");
    public string NuGetPackages => Path.Combine(directory, "nuget-packages");
    public string Home => Path.Combine(directory, "home");

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private string? systemGit;
    private DateTime systemGitChecked;
    public string? GitExe
    {
        get
        {
            var managed = Existing(Path.Combine(Dir("git"), "cmd", Windows ? "git.exe" : "git"));
            if (managed is not null) return managed;
            // Developer/test override: use an explicitly named git program (never set by the panel itself).
            if (Environment.GetEnvironmentVariable("PANEL_GIT_EXE") is { Length: > 0 } custom && File.Exists(custom)) return custom;
            if (Windows) return null;
            if (DateTime.UtcNow - systemGitChecked < TimeSpan.FromSeconds(30)) return systemGit;
            systemGitChecked = DateTime.UtcNow;
            systemGit = new[] { "/usr/bin/git", "/usr/local/bin/git", "/bin/git" }.FirstOrDefault(File.Exists);
            return systemGit;
        }
    }

    private string? Marker(string id)
    {
        var file = Path.Combine(Dir(id), ".panel-tool.json");
        if (!File.Exists(file)) return null;
        try { return JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("version").GetString(); }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException) { return null; }
    }

    public ToolInfo[] Status()
    {
        var git = GitExe;
        var gitManaged = git is not null && git.StartsWith(Dir("git"), StringComparison.OrdinalIgnoreCase);
        var wasi = Directory.Exists(WasiRoot) && Directory.EnumerateDirectories(WasiRoot).Any();
        return
        [
            new("git", git is not null, gitManaged ? Marker("git") : git is null ? null : GitVersion(git), gitManaged ? "panel" : git is null ? "none" : "system", 40, Windows, Windows ? null : "Install git with your package manager (for example: apt install git). The Docker image already contains it."),
            new("dotnet", DotnetExe is not null, DotnetExe is null ? null : Marker("dotnet"), DotnetExe is null ? "none" : "panel", 300, true, null),
            new("node", NodeExe is not null && NpmCli is not null, NodeExe is null ? null : Marker("node"), NodeExe is null ? "none" : "panel", 40, true, "Only needed for projects with a web UI (ReUi)."),
            new("wasi", wasi, wasi ? Directory.EnumerateDirectories(WasiRoot).Select(Path.GetFileName).OrderDescending().FirstOrDefault() : null, wasi ? "panel" : "none", Windows ? 620 : 190, false, "Downloaded automatically by the first build (needs internet access)."),
        ];
    }

    private static string? GitVersion(string git)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(git, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var text = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(3000);
            return text?.Replace("git version ", "");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>Scrubbed environment for build tools: nothing of the panel's own environment (tokens, config path, secrets) is passed on.</summary>
    public Dictionary<string, string> BuildEnvironment(string? extraPath = null)
    {
        Directory.CreateDirectory(Home);
        var temp = Path.Combine(directory, "tmp");
        Directory.CreateDirectory(temp);
        var path = new List<string>();
        if (DotnetExe is not null) path.Add(DotnetRoot);
        if (NodeExe is not null) path.Add(Path.GetDirectoryName(NodeExe)!);
        var git = GitExe;
        if (git is not null) path.Add(Path.GetDirectoryName(git)!);
        if (extraPath is not null) path.Add(extraPath);
        if (Windows) { var system = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows"; path.Add(Path.Combine(system, "System32")); path.Add(system); }
        else path.AddRange(["/usr/local/bin", "/usr/bin", "/bin"]);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = string.Join(Path.PathSeparator, path),
            ["HOME"] = Home,
            ["TEMP"] = temp,
            ["TMP"] = temp,
            ["TMPDIR"] = temp,
            ["DOTNET_ROOT"] = DotnetRoot,
            ["DOTNET_CLI_HOME"] = Home,
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["NUGET_PACKAGES"] = NuGetPackages,
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(directory, "nuget-http-cache"),
            ["WasiSdkRoot"] = WasiRoot,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["npm_config_cache"] = Path.Combine(directory, "npm-cache"),
            ["npm_config_update_notifier"] = "false",
            ["npm_config_fund"] = "false",
            ["npm_config_audit"] = "false",
            ["CI"] = "1",
        };
        // Harmless system variables that Windows tooling needs (NuGet, MSBuild and git resolve well-known folders from them). Nothing else is inherited.
        foreach (var name in new[] { "SystemDrive", "ProgramData", "ALLUSERSPROFILE", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "PUBLIC", "OS", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION", "NUMBER_OF_PROCESSORS", "COMPUTERNAME" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) env[name] = value;
        if (Windows)
        {
            env["USERPROFILE"] = Home;
            env["APPDATA"] = Path.Combine(Home, "AppData", "Roaming");
            env["LOCALAPPDATA"] = Path.Combine(Home, "AppData", "Local");
            env["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            env["windir"] = env["SystemRoot"];
            env["ComSpec"] = Path.Combine(env["SystemRoot"], "System32", "cmd.exe");
            env["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
        }
        else env["LANG"] = "C.UTF-8";
        return env;
    }

    // ---------------------------------------------------------------- install / remove
    public void Remove(string id)
    {
        if (!Components.Contains(id)) throw new InvalidOperationException("bundleTools.tool");
        var target = Dir(id == "wasi" ? "wasi-sdk" : id);
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    public async Task InstallAsync(string id, JobContext ctx)
    {
        switch (id)
        {
            case "git" when Windows: await InstallGit(ctx); break;
            case "dotnet": await InstallDotnet(ctx); break;
            case "node": await InstallNode(ctx); break;
            default: throw new InvalidOperationException("bundleTools.tool");
        }
    }

    public async Task<Dictionary<string, string?>> LatestVersionsAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, string?>();
        try { using var doc = JsonDocument.Parse(await http.GetStringAsync(DotnetMetadata, ct)); result["dotnet"] = doc.RootElement.GetProperty("latest-sdk").GetString(); } catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException) { result["dotnet"] = null; }
        try { var (name, _) = await NodeAsset(ct); result["node"] = Regex.Match(name, @"v([0-9.]+)").Groups[1].Value; } catch (Exception e) when (e is HttpRequestException or InvalidOperationException) { result["node"] = null; }
        if (Windows) try { using var doc = JsonDocument.Parse(await http.GetStringAsync(GitReleases, ct)); result["git"] = doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v'); } catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException) { result["git"] = null; }
        return result;
    }

    private async Task<string> Download(string url, string hashAlgorithm, string expectedHex, JobContext ctx, string label)
    {
        Directory.CreateDirectory(Path.Combine(directory, "downloads"));
        var file = Path.Combine(directory, "downloads", Guid.NewGuid().ToString("N") + ".part");
        ctx.Phase("Downloading " + label);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ctx.Token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var input = await response.Content.ReadAsStreamAsync(ctx.Token))
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long bytes = 0; var timer = Stopwatch.StartNew(); int read;
            while ((read = await input.ReadAsync(buffer, ctx.Token)) > 0)
            {
                bytes += read;
                if (bytes > 2L * 1024 * 1024 * 1024) throw new InvalidOperationException("bundleTools.toolSize");
                await output.WriteAsync(buffer.AsMemory(0, read), ctx.Token);
                if (timer.ElapsedMilliseconds > 400) { ctx.Progress(bytes, total); timer.Restart(); }
            }
            ctx.Progress(bytes, total);
        }
        ctx.Phase("Verifying " + label);
        byte[] hash;
        await using (var verify = File.OpenRead(file))
            hash = hashAlgorithm == "sha512" ? await SHA512.HashDataAsync(verify, ctx.Token) : await SHA256.HashDataAsync(verify, ctx.Token);
        if (!Convert.ToHexString(hash).Equals(expectedHex, StringComparison.OrdinalIgnoreCase)) { File.Delete(file); throw new InvalidOperationException("bundleTools.toolHash"); }
        return file;
    }

    private void Place(string id, string staging, string version)
    {
        File.WriteAllText(Path.Combine(staging, ".panel-tool.json"), JsonSerializer.Serialize(new { version, installed = DateTime.UtcNow }));
        var target = Dir(id);
        var old = target + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(target)) Directory.Move(target, old);
        try { Directory.Move(staging, target); }
        catch { if (Directory.Exists(old) && !Directory.Exists(target)) Directory.Move(old, target); throw; }
        if (Directory.Exists(old)) try { Directory.Delete(old, true); } catch (IOException) { }
    }

    private static void ExtractZip(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(archive, destination, true);
    }

    private static void ExtractTarGz(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        using var input = File.OpenRead(archive);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, true);
    }

    /// <summary>Moves a single wrapping folder's content up (Node archives contain node-vX-os-arch/).</summary>
    private static void Unwrap(string staging)
    {
        var entries = Directory.GetFileSystemEntries(staging);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        var inner = entries[0];
        foreach (var item in Directory.GetFileSystemEntries(inner)) Directory.Move(item, Path.Combine(staging, Path.GetFileName(item)));
        Directory.Delete(inner);
    }

    private async Task InstallDotnet(JobContext ctx)
    {
        ctx.Phase("Reading official .NET release metadata");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(DotnetMetadata, ctx.Token));
        var latest = doc.RootElement.GetProperty("latest-sdk").GetString()!;
        var rid = Windows ? "win-x64" : "linux-x64";
        var extension = Windows ? ".zip" : ".tar.gz";
        string? url = null, hash = null;
        foreach (var release in doc.RootElement.GetProperty("releases").EnumerateArray())
        {
            if (!release.TryGetProperty("sdk", out var sdk) || sdk.GetProperty("version").GetString() != latest) continue;
            foreach (var file in sdk.GetProperty("files").EnumerateArray())
                if (file.GetProperty("rid").GetString() == rid && file.GetProperty("name").GetString()!.EndsWith(extension, StringComparison.Ordinal)) { url = file.GetProperty("url").GetString(); hash = file.GetProperty("hash").GetString(); }
            if (url is not null) break;
        }
        if (url is null || hash is null || hash.Length != 128 || !url.StartsWith("https://builds.dotnet.microsoft.com/", StringComparison.Ordinal)) throw new InvalidOperationException("bundleTools.toolMetadata");
        ctx.Log($"Installing .NET SDK {latest} ({rid})");
        var archive = await Download(url, "sha512", hash, ctx, ".NET SDK " + latest);
        var staging = Dir("dotnet") + ".installing";
        try
        {
            ctx.Phase("Extracting .NET SDK");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Windows) ExtractZip(archive, staging); else ExtractTarGz(archive, staging);
            var exe = Path.Combine(staging, Windows ? "dotnet.exe" : "dotnet");
            if (!File.Exists(exe)) throw new InvalidOperationException("bundleTools.toolArchive");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            Place("dotnet", staging, latest);
            ctx.Log(".NET SDK " + latest + " installed.");
        }
        finally { try { File.Delete(archive); } catch (IOException) { } if (Directory.Exists(staging)) try { Directory.Delete(staging, true); } catch (IOException) { } }
    }

    private async Task<(string Name, string Hash)> NodeAsset(CancellationToken ct)
    {
        var sums = await http.GetStringAsync(NodeIndex + "SHASUMS256.txt", ct);
        var suffix = Windows ? "-win-x64.zip" : "-linux-x64.tar.gz";
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split("  ", 2);
            if (parts.Length == 2 && parts[1].StartsWith("node-v", StringComparison.Ordinal) && parts[1].EndsWith(suffix, StringComparison.Ordinal) && Regex.IsMatch(parts[0], "^[0-9a-f]{64}$")) return (parts[1], parts[0]);
        }
        throw new InvalidOperationException("bundleTools.toolMetadata");
    }

    private async Task InstallNode(JobContext ctx)
    {
        ctx.Phase("Reading Node.js release list");
        var (name, hash) = await NodeAsset(ctx.Token);
        var version = Regex.Match(name, @"v([0-9.]+)").Groups[1].Value;
        var archive = await Download(NodeIndex + name, "sha256", hash, ctx, "Node.js " + version);
        var staging = Dir("node") + ".installing";
        try
        {
            ctx.Phase("Extracting Node.js");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Windows) ExtractZip(archive, staging); else ExtractTarGz(archive, staging);
            Unwrap(staging);
            Place("node", staging, version);
            ctx.Log("Node.js " + version + " installed.");
        }
        finally { try { File.Delete(archive); } catch (IOException) { } if (Directory.Exists(staging)) try { Directory.Delete(staging, true); } catch (IOException) { } }
    }

    private async Task InstallGit(JobContext ctx)
    {
        ctx.Phase("Reading git-for-windows releases");
        using var request = new HttpRequestMessage(HttpMethod.Get, GitReleases);
        using var response = await http.SendAsync(request, ctx.Token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ctx.Token));
        var version = doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v');
        string? url = null, hash = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString()!;
            if (!Regex.IsMatch(name, @"^MinGit-[0-9.]+-64-bit\.zip$")) continue;
            url = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal)) hash = digest[7..];
        }
        if (url is null || hash is null || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$") || !url.StartsWith("https://github.com/git-for-windows/git/releases/download/", StringComparison.Ordinal)) throw new InvalidOperationException("bundleTools.toolMetadata");
        ctx.Log("Installing MinGit " + version);
        var archive = await Download(url, "sha256", hash, ctx, "MinGit " + version);
        var staging = Dir("git") + ".installing";
        try
        {
            ctx.Phase("Extracting MinGit");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ExtractZip(archive, staging);
            if (!File.Exists(Path.Combine(staging, "cmd", "git.exe"))) throw new InvalidOperationException("bundleTools.toolArchive");
            Place("git", staging, version);
            ctx.Log("MinGit " + version + " installed.");
        }
        finally { try { File.Delete(archive); } catch (IOException) { } if (Directory.Exists(staging)) try { Directory.Delete(staging, true); } catch (IOException) { } }
    }
}
