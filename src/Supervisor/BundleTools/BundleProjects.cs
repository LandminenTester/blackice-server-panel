using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record LastBuild(DateTimeOffset Time, bool Ok, string? Commit, string? Error);

public sealed class BundleProject
{
    public string Name { get; set; } = "";
    /// <summary>template, import or adopted (a folder in panel_src that was already there)</summary>
    public string Source { get; set; } = "template";
    public string? Remote { get; set; }
    public string? AccountId { get; set; }
    public string? Branch { get; set; }
    public string? Workflow {get;set;}
    public string? DevelopmentBranch {get;set;}
    public string? ProductionBranch {get;set;}
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
        service.RequireSources();
        var registered = Registry.All();
        var names = registered.Select(p => p.Name).ToList();
        var sources = BundleLayout.SourceRoot(Config);
        if (Directory.Exists(sources))
            foreach (var directory in Directory.EnumerateDirectories(sources))
            {
                var name = Path.GetFileName(directory);
                if (BundleLayout.ValidName(name) && !names.Contains(name))
                    registered.Add(new BundleProject { Name = name, Source = "adopted", Created = Directory.GetCreationTimeUtc(directory) });
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
                workflow=p.Workflow??Config.BundleTools.Workflow,workflowOverride=p.Workflow,developmentBranch=p.DevelopmentBranch??Config.BundleTools.DevelopmentBranch,productionBranch=p.ProductionBranch??Config.BundleTools.ProductionBranch,
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
    private sealed record CreateArgs(string Name, string? Author, string? Description, bool Build, UiArgs? Ui, RepoArgs? Repo);
    private sealed record RepoArgs(string AccountId, string? Visibility,string? Owner=null);
    private sealed record UiArgs(string Framework, bool TypeScript, bool Router, bool State, bool Tailwind,string[]? Packages=null,string? StateLibrary=null);

    public BundleJob Create(string json,bool useAccounts=true)
    {
        service.RequireSources();
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
            ui = new UiOptions(u.Framework, u.TypeScript, u.Router && u.Framework != "svelte", u.State, u.Tailwind,u.Packages,u.StateLibrary);
            StateTemplates.Validate(ui);
        }
        var source = BundleLayout.SourceDir(Config, name);
        if (Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any()) throw new InvalidOperationException("bundleTools.exists");
        GitAccount? repoAccount = null;
        if (args.Repo is { } repoArgs)
        {
            if(!useAccounts)throw new InvalidOperationException("auth.forbidden");
            repoAccount = Config.BundleTools.Accounts.FirstOrDefault(a => a.Id == repoArgs.AccountId) ?? throw new InvalidOperationException("bundleTools.account");
            if (repoAccount.TokenProtected.Length == 0) throw new InvalidOperationException("bundleTools.tokenRejected");
            if (service.Tools.GitExe is null) throw new InvalidOperationException("bundleTools.needGit");
        }
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
            ctx.Log($"Created {files.Count} files in {BundleLayout.SourceFolder}/{name}.");
            if (repoAccount is not null && args.Repo is { } repo)
            {
                ctx.Phase("Creating the " + (repo.Visibility == "public" ? "public" : "private") + " repository");
                var token = service.Secrets.Unprotect(repoAccount.TokenProtected);
                service.Redactor.Add(token);
                RepoInfo info;
                try { info = await GitProviders.CreateRepositoryAsync(service.ProviderHttp, repoAccount, token, name, description.Length > 0 ? description : name, repo.Visibility != "public", ctx.Token,repo.Owner); }
                catch (HttpRequestException) { throw new InvalidOperationException("bundleTools.providerFailed"); }
                ctx.Log("Repository: " + info.WebUrl);
                await service.Git.InitAndPushAsync(source, info.CloneUrl, repoAccount, ctx);
                Registry.Update(name, p => { p.Remote = info.CloneUrl; p.AccountId = repoAccount.Id; p.Branch = "main"; });
            }
            if (args.Build) await BuildCore(name, ctx);
            else ctx.Log("Use Build to compile the project into the bundle folder.");
            ctx.Result("Created " + name);
        });
    }

    // ------------------------------------------------------------------ git: import, check, pull, versions
    private sealed record ImportArgs(string Name, string Url, string? Branch, string? AccountId, bool Build);
    private sealed record PullArgs(string Name, bool Build, bool Discard);
    private sealed record CheckoutArgs(string Name, string Kind, string Ref, bool Build);

    private GitAccount? AccountOf(BundleProject project,bool useAccounts=true) => useAccounts?service.Git.AccountFor(project.AccountId, project.Remote ?? ""):null;

    public async Task<object> ListAsync(CancellationToken ct)
    {
        var projects = ((System.Collections.IEnumerable)List()).Cast<object>().ToArray();
        var result = new List<object>();
        foreach (var item in projects)
        {
            var name = (string)item.GetType().GetProperty("Name")!.GetValue(item)!;
            var source = BundleLayout.SourceDir(Config, name);
            GitInfo? git = null;
            if (service.Tools.GitExe is not null && Directory.Exists(Path.Combine(source, ".git")))
                try { git = await service.Git.InfoAsync(source, null, ct); } catch (InvalidOperationException) { }
            result.Add(new { project = item, git });
        }
        return result;
    }

    public BundleJob Import(string json,bool useAccounts=true)
    {
        service.RequireSources();
        if (!Config.BundleTools.SourcesEnabled) throw new InvalidOperationException("bundleTools.disabled");
        var args = JsonSerializer.Deserialize<ImportArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var name = BundleLayout.RequireName(args.Name);
        var url = GitService.ValidateRemote(args.Url);
        var branch = string.IsNullOrWhiteSpace(args.Branch) ? null : GitService.ValidateRef(args.Branch.Trim());
        if(!useAccounts&&!string.IsNullOrEmpty(args.AccountId))throw new InvalidOperationException("auth.forbidden");
        var account = useAccounts?service.Git.AccountFor(args.AccountId,url):null;
        var source = BundleLayout.SourceDir(Config, name);
        if (Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any()) throw new InvalidOperationException("bundleTools.exists");
        if (service.Tools.GitExe is null) throw new InvalidOperationException("bundleTools.needGit");
        return service.Jobs.Start("project-import", name, async ctx =>
        {
            ctx.Phase("Cloning " + url);
            try { await service.Git.CloneAsync(url, branch, source, account, ctx); }
            catch { try { if (Directory.Exists(source)) ForceDelete(source); } catch (IOException) { } throw; }
            var framework = DetectFramework(source);
            Registry.Update(name, p => { p.Source = "import"; p.Remote = url; p.AccountId = account?.Id; p.Branch = branch; p.UiFramework = framework; }, () => new BundleProject { Name = name, Source = "import", Remote = url, AccountId = account?.Id, Branch = branch, UiFramework = framework });
            if (FindManifest(source) is null) ctx.Log("Warning: no config.toml found (data/config.toml or config.toml). A build needs one.");
            ctx.Log("Imported. Nothing was built or started.");
            if (args.Build) await BuildCore(name, ctx);
            ctx.Result("Imported " + name);
        });
    }

    public async Task<object> Check(string name, CancellationToken ct,bool useAccounts=true)
    {
        service.RequireSources();
        var project = Registry.Get(BundleLayout.RequireName(name)) ?? throw new InvalidOperationException("bundleTools.noSource");
        var source = BundleLayout.SourceDir(Config, name);
        var account = AccountOf(project,useAccounts);
        var url = project.Remote ?? "";
        if (service.Tools.GitExe is null || !Directory.Exists(Path.Combine(source, ".git"))) throw new InvalidOperationException("bundleTools.notGit");
        var (exit, _) = await service.Git.RunAsync(["fetch", "--prune", "--tags", "--quiet", "origin"], source, account, url, ct);
        if (exit != 0) throw new InvalidOperationException("bundleTools.gitFailed");
        return await service.Git.InfoAsync(source, account, ct);
    }

    public BundleJob Pull(string json,bool useAccounts=true)
    {
        service.RequireSources();
        var args = JsonSerializer.Deserialize<PullArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var project = Registry.Get(BundleLayout.RequireName(args.Name)) ?? throw new InvalidOperationException("bundleTools.noSource");
        var source = BundleLayout.SourceDir(Config, args.Name);
        if (service.Tools.GitExe is null) throw new InvalidOperationException("bundleTools.needGit");
        if (!Directory.Exists(Path.Combine(source, ".git"))) throw new InvalidOperationException("bundleTools.notGit");
        var account = AccountOf(project,useAccounts);
        return service.Jobs.Start(args.Build ? "pull-build" : "pull", args.Name, async ctx =>
        {
            ctx.Phase("Pulling the latest sources");
            await service.Git.PullAsync(source, args.Discard, account, project.Remote ?? "", ctx);
            var info = await service.Git.InfoAsync(source, account, ctx.Token);
            ctx.Log($"Now at {info.Branch} {info.Commit}.");
            if (args.Build) await BuildCore(args.Name, ctx);
            ctx.Result("Pulled " + args.Name);
        });
    }

    public async Task<object> Refs(string name, CancellationToken ct,bool useAccounts=true)
    {
        service.RequireSources();
        var source = BundleLayout.SourceDir(Config, BundleLayout.RequireName(name));
        if (service.Tools.GitExe is null || !Directory.Exists(Path.Combine(source, ".git"))) throw new InvalidOperationException("bundleTools.notGit");
        var project=Registry.Get(name);
        if(project?.Remote is {} remote)await service.Git.FetchAsync(source,useAccounts?AccountOf(project):null,GitService.ValidateRemote(remote),null,ct);
        return await service.Git.RefsAsync(source, ct);
    }

    public BundleJob Checkout(string json)
    {
        service.RequireSources();
        var args = JsonSerializer.Deserialize<CheckoutArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        BundleLayout.RequireName(args.Name);
        if (args.Kind is not ("branch" or "tag")) throw new InvalidOperationException("bundleTools.ref");
        GitService.ValidateRef(args.Ref);
        var source = BundleLayout.SourceDir(Config, args.Name);
        if (service.Tools.GitExe is null || !Directory.Exists(Path.Combine(source, ".git"))) throw new InvalidOperationException("bundleTools.notGit");
        return service.Jobs.Start("checkout", args.Name, async ctx =>
        {
            ctx.Phase("Switching to " + args.Kind + " " + args.Ref);
            await service.Git.CheckoutAsync(source, args.Kind, args.Ref, ctx);
            Registry.Update(args.Name, p => p.Branch = args.Kind == "branch" ? args.Ref : p.Branch);
            if (args.Build) await BuildCore(args.Name, ctx);
            ctx.Result("Switched " + args.Name);
        });
    }

    private sealed record WorkflowArgs(string Name,string Mode,string? DevelopmentBranch,string? ProductionBranch);
    public object SetWorkflow(string json){
        service.RequireSources();if(service.Jobs.Busy)throw new InvalidOperationException("bundleTools.busy");var args=JsonSerializer.Deserialize<WorkflowArgs>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        var name=BundleLayout.RequireName(args.Name);if(Registry.Get(name) is null)throw new InvalidOperationException("bundleTools.noSource");
        if(args.Mode is not ("inherit" or "review" or "direct" or "branches"))throw new InvalidOperationException("bundleTools.invalid");
        if(!string.IsNullOrEmpty(args.DevelopmentBranch))GitService.ValidateRef(args.DevelopmentBranch);if(!string.IsNullOrEmpty(args.ProductionBranch))GitService.ValidateRef(args.ProductionBranch);
        Registry.Update(name,p=>{p.Workflow=args.Mode=="inherit"?null:args.Mode;p.DevelopmentBranch=string.IsNullOrEmpty(args.DevelopmentBranch)?null:args.DevelopmentBranch;p.ProductionBranch=string.IsNullOrEmpty(args.ProductionBranch)?null:args.ProductionBranch;});return List();
    }
    private sealed record DeliveryArgs(string Name,string Action,string? Branch,string? Target,string? Message,string? Title,bool ConfirmMain=false);
    public BundleJob Delivery(string json){
        service.RequireSources();var args=JsonSerializer.Deserialize<DeliveryArgs>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        var project=Registry.Get(BundleLayout.RequireName(args.Name))??throw new InvalidOperationException("bundleTools.noSource");
        var source=BundleLayout.SourceDir(Config,args.Name);var remote=GitService.ValidateRemote(project.Remote);var account=AccountOf(project)??throw new InvalidOperationException("bundleTools.account");
        if(args.Action is not ("branch" or "push" or "review"))throw new InvalidOperationException("bundleTools.invalid");
        if(args.Action is "branch" or "review")GitService.ValidateRef(args.Branch);
        if(args.Action=="branch"&&(args.Branch is "main" or "master"||args.Branch==(project.ProductionBranch??Config.BundleTools.ProductionBranch)))throw new InvalidOperationException("bundleTools.protectedBranch");
        if(args.Action=="review"){GitService.ValidateRef(args.Target);if((project.Workflow??Config.BundleTools.Workflow)!="review")throw new InvalidOperationException("bundleTools.workflow");}
        if((args.Message??"").Length>200||(args.Title??"").Length>200)throw new InvalidOperationException("bundleTools.invalid");
        return service.Jobs.Start("git-"+args.Action,args.Name,async ctx=>{
            var info=await service.Git.InfoAsync(source,null,ctx.Token);
            async Task Step(params string[] commands){var result=await service.Git.RunAsync(commands,source,account,remote,ctx.Token,ctx.Log);if(result.ExitCode!=0)throw new InvalidOperationException("bundleTools.gitFailed");}
            if(args.Action=="review"){
                ctx.Phase("Creating a review request");var token=service.Secrets.Unprotect(account.TokenProtected);service.Redactor.Add(token);
                var url=await GitProviders.ReviewAsync(service.ProviderHttp,account,token,remote,args.Branch!,args.Target!,args.Title??"Merge "+args.Branch+" into "+args.Target,ctx.Token);ctx.Result(url);ctx.Log(url);
            }else if(args.Action=="branch"){
                if(info.Dirty)throw new InvalidOperationException("bundleTools.gitDirty");ctx.Phase("Creating and pushing a development branch");await Step("checkout","-b",args.Branch!);await Step("push","-u","origin",args.Branch!);Registry.Update(args.Name,p=>p.Branch=args.Branch);ctx.Result("Created "+args.Branch);
            }else{
                var branch=GitService.ValidateRef(info.Branch);if(branch=="HEAD"||((branch is "main" or "master"||branch==(project.ProductionBranch??Config.BundleTools.ProductionBranch))&&!args.ConfirmMain))throw new InvalidOperationException("bundleTools.protectedBranch");
                if(string.IsNullOrWhiteSpace(args.Message)||args.Message.Any(char.IsControl))throw new InvalidOperationException("bundleTools.invalid");
                ctx.Phase("Committing and pushing changes");await Step("add","-A");
                var changes=await service.Git.RunAsync(["diff","--cached","--quiet"],source,null,"",ctx.Token);
                if(changes.ExitCode==1)await Step("-c","user.name="+Config.BundleTools.CommitName,"-c","user.email="+Config.BundleTools.CommitEmail,"commit","-m",args.Message!,"--quiet");else if(changes.ExitCode!=0)throw new InvalidOperationException("bundleTools.gitFailed");
                await Step("push","origin","HEAD:refs/heads/"+branch);ctx.Result("Pushed "+branch);
            }
        });
    }

    // ------------------------------------------------------------------ build
    public BundleJob Build(string name)
    {
        service.RequireSources();
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
            if (Directory.EnumerateFiles(stage, "config.toml", SearchOption.AllDirectories).Any(f => !Path.GetFullPath(f).Equals(Path.GetFullPath(Path.Combine(stage, "config.toml")), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("bundleTools.nestedManifest");
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
        service.RequireSources();
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
        service.RequireSources();
        BundleLayout.RequireName(name);
        if (service.Jobs.Busy) throw new InvalidOperationException("bundleTools.busy");
        var source = BundleLayout.SourceDir(Config, name);
        if (Directory.Exists(source) && !BundleLayout.IsPathInside(BundleLayout.SourceRoot(Config), source)) throw new InvalidOperationException("bundleTools.path");
        if (Directory.Exists(source)) ForceDelete(source);
        Registry.Remove(name);
        return List();
    }

    internal static void ForceDelete(string directory)
    {
        BundlePaths.Require(Path.GetDirectoryName(directory)!);
        BundlePaths.Delete(directory);
    }

    // ------------------------------------------------------------------ package versions (never automatic)
    private sealed record PackageArgs(string Name, string? Version, bool IncludePrerelease);

    public async Task<object> PackagesInfo(string json, CancellationToken ct)
    {
        service.RequireSources();
        var args = JsonSerializer.Deserialize<PackageArgs>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var source = BundleLayout.SourceDir(Config, BundleLayout.RequireName(args.Name));
        var current = NuGetService.Read(source);
        if (current.Length == 0) return new { current, versions = Array.Empty<string>(), latest = (string?)null };
        var versions = await nuget.CommonVersionsAsync(current.Select(c => c.Id), args.IncludePrerelease, ct);
        return new { current, versions, latest = versions.LastOrDefault() };
    }

    public async Task<object> PackagesSet(string json, CancellationToken ct)
    {
        service.RequireSources();
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
