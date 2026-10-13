using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record LastBuild(DateTimeOffset Time, bool Ok, string? Commit, string? Error);

public sealed class BundleProject
{
    public string Name { get; set; } = "";
    /// <summary>template, import or adopted (a panel_src folder that was already there)</summary>
    public string Source { get; set; } = "template";
    public string? Remote { get; set; }
    public string? AccountId { get; set; }
    public string? Branch { get; set; }
    public string? UiFramework { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public LastBuild? Build { get; set; }
}

/// <summary>Persistent list of source projects (kept in the panel state, not inside the game server's ugc folder).</summary>
public sealed class ProjectRegistry(string file)
{
    private readonly object sync = new();

    public List<BundleProject> All()
    {
        lock (sync)
        {
            if (!File.Exists(file)) return [];
            try { return JsonSerializer.Deserialize<List<BundleProject>>(File.ReadAllText(file), PanelConfig.Json) ?? []; }
            catch (Exception e) when (e is IOException or JsonException) { return []; }
        }
    }

    public BundleProject? Get(string name) => All().FirstOrDefault(p => p.Name == name);

    public void Update(string name, Action<BundleProject> change, Func<BundleProject>? create = null)
    {
        lock (sync)
        {
            var list = All();
            var project = list.FirstOrDefault(p => p.Name == name);
            if (project is null) { if (create is null) return; project = create(); list.Add(project); }
            change(project);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, PanelConfig.Json));
            File.Move(temp, file, true);
        }
    }

    public void Remove(string name)
    {
        lock (sync)
        {
            var list = All();
            if (list.RemoveAll(p => p.Name == name) == 0) return;
            File.WriteAllText(file, JsonSerializer.Serialize(list, PanelConfig.Json));
        }
    }
}

/// <summary>Source projects of the EXPERIMENTAL bundle tools: create from a template, build into the bundle folder, manage package versions, roll back.</summary>
public sealed partial class BundleProjects(BundleToolsService service)
{
    public ProjectRegistry Registry { get; } = new(BundleLayout.Registry(service.ConfigPath));
    private PanelConfig Config => service.Config;
    private readonly NuGetService nuget = new(service.Http);

    [GeneratedRegex("(?:replay\\.blackice\\.(server|client))", RegexOptions.IgnoreCase)] private static partial Regex ReplayKind();
    [GeneratedRegex("<AssemblyName>([^<]+)</AssemblyName>", RegexOptions.IgnoreCase)] private static partial Regex AssemblyName();

    // ------------------------------------------------------------------ listing
    private static string? DetectFramework(string source)
    {
        var package = Path.Combine(source, "ui", "package.json");
        if (!File.Exists(package)) return Directory.Exists(Path.Combine(source, "ui")) ? "static" : null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(package));
            var names = new HashSet<string>();
            foreach (var section in new[] { "dependencies", "devDependencies" })
                if (doc.RootElement.TryGetProperty(section, out var deps)) foreach (var dep in deps.EnumerateObject()) names.Add(dep.Name);
            return names.Contains("svelte") ? "svelte" : names.Contains("vue") ? "vue" : names.Contains("react") ? "react" : "other";
        }
        catch (Exception e) when (e is IOException or JsonException) { return "other"; }
    }

    public object List()
    {
        service.RequireEnabled();
        var registered = Registry.All();
        var names = registered.Select(p => p.Name).ToList();
        var ugc = BundleLayout.UgcRoot(Config);
        if (Directory.Exists(ugc))
            foreach (var directory in Directory.EnumerateDirectories(ugc))
            {
                var name = Path.GetFileName(directory);
                if (BundleLayout.ValidName(name) && !names.Contains(name) && Directory.Exists(Path.Combine(directory, BundleLayout.SourceFolder)))
                    registered.Add(new BundleProject { Name = name, Source = "adopted", Created = Directory.GetCreationTimeUtc(Path.Combine(directory, BundleLayout.SourceFolder)) });
            }
        return registered.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p =>
        {
            var source = BundleLayout.SourceDir(Config, p.Name);
            var bundle = BundleLayout.BundleDir(Config, p.Name);
            return new
            {
                p.Name,
                p.Source,
                p.Remote,
                p.Branch,
                p.AccountId,
                p.Build,
                hasSource = Directory.Exists(source),
                hasBuild = File.Exists(Path.Combine(bundle, "config.toml")),
                ui = DetectFramework(source) ?? p.UiFramework,
                packages = NuGetService.Read(source),
                hasBackup = BundleSwap.LatestBackup(BundleLayout.Backups(service.ConfigPath), p.Name) is not null,
            };
        }).ToArray();
    }

    // ------------------------------------------------------------------ create from template
    private sealed record CreateArgs(string Name, string? Author, string? Description, bool Build, UiArgs? Ui);
    private sealed record UiArgs(string Framework, bool TypeScript, bool Router, bool State, bool Tailwind);

    public BundleJob Create(string json)
    {
        service.RequireEnabled();
        if (!Config.BundleTools.SourcesEnabled) throw new InvalidOperationException("bundleTools.disabled");
        var args = JsonSerializer.Deserialize<CreateArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var name = BundleLayout.RequireName(args.Name);
        var author = (args.Author ?? "").Trim();
        var description = (args.Description ?? "").Trim();
        if (author.Length > 80 || description.Length > 300 || author.Any(char.IsControl) || description.Any(char.IsControl)) throw new InvalidOperationException("bundleTools.invalid");
        UiOptions? ui = null;
        if (args.Ui is { } u)
        {
            if (!BundleTemplates.Frameworks.Contains(u.Framework)) throw new InvalidOperationException("bundleTools.invalid");
            ui = new UiOptions(u.Framework, u.TypeScript, u.Router && u.Framework != "svelte", u.State, u.Tailwind);
        }
        var source = BundleLayout.SourceDir(Config, name);
        if (Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any()) throw new InvalidOperationException("bundleTools.exists");
        return service.Jobs.Start("project-create", name, async ctx =>
        {
            ctx.Phase("Resolving the newest package version");
            string version;
            try { version = (await nuget.VersionsAsync("replay.blackice.server", false, ctx.Token)).LastOrDefault() ?? "*"; }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException) { version = "*"; ctx.Log("Package versions could not be read (" + e.GetType().Name + "); using a floating version. Set a fixed version later in the project."); }
            ctx.Log("replay.blackice.* version: " + version);
            ctx.Phase("Writing the project files");
            var files = BundleTemplates.Render(new NewProject(name, author.Length > 0 ? author : "unknown", description.Length > 0 ? description : name, version, ui));
            foreach (var (relative, content) in files)
            {
                var target = Path.GetFullPath(Path.Combine(source, relative));
                if (!BundleLayout.IsPathInside(source, target)) throw new InvalidOperationException("bundleTools.path");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content, new System.Text.UTF8Encoding(false));
            }
            Registry.Update(name, p => { p.Source = "template"; p.UiFramework = ui?.Framework; }, () => new BundleProject { Name = name, Source = "template", UiFramework = ui?.Framework });
            ctx.Log($"Created {files.Count} files in {BundleLayout.SourceFolder}.");
            if (args.Build) await BuildCore(name, ctx);
            else ctx.Log("Use Build to compile the project into the bundle folder.");
            ctx.Result("Created " + name);
        });
    }

    // ------------------------------------------------------------------ build
    public BundleJob Build(string name)
    {
        service.RequireEnabled();
        if (!Config.BundleTools.SourcesEnabled) throw new InvalidOperationException("bundleTools.disabled");
        BundleLayout.RequireName(name);
        return service.Jobs.Start("build", name, ctx => BuildCore(name, ctx));
    }

    private static string? FindManifest(string source) => new[] { Path.Combine(source, "data", "config.toml"), Path.Combine(source, "config.toml") }.FirstOrDefault(File.Exists);

    private static IEnumerable<string> ProjectFiles(string source) =>
        Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    internal async Task BuildCore(string name, JobContext ctx)
    {
        var settings = Config.BundleTools;
        var source = BundleLayout.SourceDir(Config, name);
        var bundle = BundleLayout.BundleDir(Config, name);
        if (!Directory.Exists(source)) throw new InvalidOperationException("bundleTools.noSource");
        var tools = service.Tools;
        if (tools.DotnetExe is null) throw new InvalidOperationException("bundleTools.needDotnet");
        var manifest = FindManifest(source) ?? throw new InvalidOperationException("bundleTools.noManifest");
        var doc = TomlDoc.Parse(File.ReadAllText(manifest));
        var serverBlob = doc.String("Scripting", "server_blob");
        var clientBlob = doc.String("Scripting", "client_blob");
        var stage = Path.Combine(BundleLayout.Stage(service.ConfigPath), ctx.Id);
        var env = tools.BuildEnvironment();
        var nugetConfig = Path.Combine(BundleLayout.Toolchain(service.ConfigPath), "nuget.config");
        Directory.CreateDirectory(Path.GetDirectoryName(nugetConfig)!);
        File.WriteAllText(nugetConfig, BundleTemplates.PanelNuGetConfig);
        try
        {
            Directory.CreateDirectory(stage);
            // ---- C# components
            var projects = ProjectFiles(source).Select(f => (File: f, Text: File.ReadAllText(f))).ToList();
            foreach (var (kind, blob) in new[] { ("server", serverBlob), ("client", clientBlob) })
            {
                if (string.IsNullOrEmpty(blob)) continue;
                var project = projects.FirstOrDefault(p => ReplayKind().Match(p.Text) is { Success: true } m && m.Groups[1].Value.Equals(kind, StringComparison.OrdinalIgnoreCase));
                if (project.File is null) throw new InvalidOperationException("bundleTools.noProject");
                ctx.Phase($"Building the {kind} component");
                var directory = Path.GetDirectoryName(project.File)!;
                var assembly = AssemblyName().Match(project.Text) is { Success: true } a ? a.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(project.File);
                var result = await ProcessRunner.Run(tools.DotnetExe, ["build", project.File, "-c", "Release", "-nologo", "-v:minimal", "-p:RestoreConfigFile=" + nugetConfig, "-p:WasiSdkRoot=" + tools.WasiRoot], directory, env, ctx.Log, ctx.Token, TimeSpan.FromMinutes(40));
                if (result.TimedOut) throw new InvalidOperationException("bundleTools.timeout");
                if (result.ExitCode != 0) throw new InvalidOperationException("bundleTools.buildFailed");
                var wasm = Directory.Exists(Path.Combine(directory, "bin", "Release"))
                    ? Directory.EnumerateFiles(Path.Combine(directory, "bin", "Release"), assembly + ".wasm", SearchOption.AllDirectories).Where(f => f.Contains(Path.DirectorySeparatorChar + "native" + Path.DirectorySeparatorChar)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                    : null;
                if (wasm is null) throw new InvalidOperationException("bundleTools.noOutput");
                var destination = Path.GetFullPath(Path.Combine(stage, blob));
                if (!BundleLayout.IsPathInside(stage, destination)) throw new InvalidOperationException("bundleTools.path");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(wasm, destination, true);
                ctx.Log($"{blob}: {new FileInfo(destination).Length / 1024} KB");
            }
            // ---- web UI
            var uiSource = Path.Combine(source, "ui");
            if (Directory.Exists(uiSource))
            {
                if (File.Exists(Path.Combine(uiSource, "package.json")))
                {
                    if (!settings.BuildUi) ctx.Log("The project has a web UI but UI builds are switched off in the bundle tools settings; the UI is not part of this build.");
                    else await BuildUi(uiSource, stage, tools, env, ctx);
                }
                else { ctx.Log("Static UI folder: copying ui/ as it is."); BundleSwap.CopyDirectory(uiSource, Path.Combine(stage, "ui")); }
            }
            // ---- manifest and seed data
            File.Copy(manifest, Path.Combine(stage, "config.toml"), true);
            var vfs = Path.Combine(source, "vfs");
            if (Directory.Exists(vfs)) BundleSwap.CopyDirectory(vfs, Path.Combine(stage, "vfs"));
            // ---- install
            ctx.Phase("Installing into the bundle folder");
            await service.WithBundleStoppedAsync(name, ctx.Log, ctx.Token, () =>
            {
                BundleSwap.Backup(bundle, BundleLayout.Backups(service.ConfigPath), name, settings.KeepBackups, ctx.Log);
                BundleSwap.Apply(stage, bundle, ctx.Log);
                return Task.CompletedTask;
            });
            Registry.Update(name, p => p.Build = new LastBuild(DateTimeOffset.UtcNow, true, null, null), () => new BundleProject { Name = name, Source = "adopted" });
            ctx.Result("Built " + name);
            ctx.Log("Build finished. Nothing was started or updated automatically.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var code = e is InvalidOperationException && Regex.IsMatch(e.Message, @"\A[a-z][a-zA-Z]+\.[a-zA-Z]+\z") ? e.Message : "bundleTools.failed";
            Registry.Update(name, p => p.Build = new LastBuild(DateTimeOffset.UtcNow, false, null, code));
            throw;
        }
        finally { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { } }
    }

    private async Task BuildUi(string uiSource, string stage, Toolchain tools, Dictionary<string, string> env, JobContext ctx)
    {
        if (tools.NodeExe is null || tools.NpmCli is null) throw new InvalidOperationException("bundleTools.needNode");
        ctx.Phase("Installing web UI packages");
        var install = File.Exists(Path.Combine(uiSource, "package-lock.json")) ? "ci" : "install";
        var first = await ProcessRunner.Run(tools.NodeExe, [tools.NpmCli, install, "--ignore-scripts", "--no-audit", "--no-fund"], uiSource, env, ctx.Log, ctx.Token, TimeSpan.FromMinutes(15));
        if (first.TimedOut) throw new InvalidOperationException("bundleTools.timeout");
        if (first.ExitCode != 0) throw new InvalidOperationException("bundleTools.uiFailed");
        ctx.Phase("Building the web UI");
        var build = await ProcessRunner.Run(tools.NodeExe, [tools.NpmCli, "run", "build"], uiSource, env, ctx.Log, ctx.Token, TimeSpan.FromMinutes(15));
        if (build.TimedOut) throw new InvalidOperationException("bundleTools.timeout");
        if (build.ExitCode != 0) throw new InvalidOperationException("bundleTools.uiFailed");
        var output = new[] { "dist", "build" }.Select(d => Path.Combine(uiSource, d)).FirstOrDefault(d => File.Exists(Path.Combine(d, "index.html"))) ?? throw new InvalidOperationException("bundleTools.noOutput");
        BundleSwap.CopyDirectory(output, Path.Combine(stage, "ui"));
        ctx.Log("Web UI output: " + Path.GetFileName(output) + "/");
    }

    // ------------------------------------------------------------------ rollback / remove
    public BundleJob Rollback(string name)
    {
        service.RequireEnabled();
        BundleLayout.RequireName(name);
        var backups = BundleLayout.Backups(service.ConfigPath);
        var latest = BundleSwap.LatestBackup(backups, name) ?? throw new InvalidOperationException("bundleTools.noBackup");
        var bundle = BundleLayout.BundleDir(Config, name);
        return service.Jobs.Start("rollback", name, async ctx =>
        {
            var stage = Path.Combine(BundleLayout.Stage(service.ConfigPath), ctx.Id);
            try
            {
                ctx.Phase("Restoring " + Path.GetFileName(latest));
                BundleSwap.CopyDirectory(latest, stage);
                await service.WithBundleStoppedAsync(name, ctx.Log, ctx.Token, () =>
                {
                    BundleSwap.Backup(bundle, backups, name, Config.BundleTools.KeepBackups + 1, ctx.Log);
                    BundleSwap.Apply(stage, bundle, ctx.Log);
                    return Task.CompletedTask;
                });
                ctx.Result("Restored " + name);
            }
            finally { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { } }
        });
    }

    public object RemoveSource(string name)
    {
        service.RequireEnabled();
        BundleLayout.RequireName(name);
        if (service.Jobs.Busy) throw new InvalidOperationException("bundleTools.busy");
        var source = BundleLayout.SourceDir(Config, name);
        if (Directory.Exists(source) && !BundleLayout.IsPathInside(BundleLayout.BundleDir(Config, name), source)) throw new InvalidOperationException("bundleTools.path");
        if (Directory.Exists(source)) ForceDelete(source);
        Registry.Remove(name);
        return List();
    }

    internal static void ForceDelete(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { }
        Directory.Delete(directory, true);
    }

    // ------------------------------------------------------------------ package versions (never automatic)
    private sealed record PackageArgs(string Name, string? Version, bool IncludePrerelease);

    public async Task<object> PackagesInfo(string json, CancellationToken ct)
    {
        service.RequireEnabled();
        var args = JsonSerializer.Deserialize<PackageArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var source = BundleLayout.SourceDir(Config, BundleLayout.RequireName(args.Name));
        var current = NuGetService.Read(source);
        if (current.Length == 0) return new { current, versions = Array.Empty<string>(), latest = (string?)null };
        var versions = await nuget.CommonVersionsAsync(current.Select(c => c.Id), args.IncludePrerelease, ct);
        return new { current, versions, latest = versions.LastOrDefault() };
    }

    public async Task<object> PackagesSet(string json, CancellationToken ct)
    {
        service.RequireEnabled();
        if (service.Jobs.Busy) throw new InvalidOperationException("bundleTools.busy");
        var args = JsonSerializer.Deserialize<PackageArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var source = BundleLayout.SourceDir(Config, BundleLayout.RequireName(args.Name));
        var version = args.Version ?? throw new InvalidOperationException("bundleTools.package");
        var current = NuGetService.Read(source);
        if (current.Length == 0) throw new InvalidOperationException("bundleTools.noPackages");
        var available = await nuget.CommonVersionsAsync(current.Select(c => c.Id), true, ct);
        if (!available.Contains(version, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("bundleTools.package");
        var pins = current.Any(c => c.Id.Equals("replay.blackice.server", StringComparison.OrdinalIgnoreCase)) ? await nuget.CompilerPinsAsync(version, ct) : [];
        NuGetService.Apply(source, version, pins);
        return new { current = NuGetService.Read(source) };
    }
}
