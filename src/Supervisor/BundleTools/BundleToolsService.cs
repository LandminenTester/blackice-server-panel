using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record BundleToolsPatch(bool? Enabled, bool? UploadEnabled, bool? SourcesEnabled, bool? RestartAfterBuild, bool? BuildUi, int? KeepBackups, int? MaxUploadMb, string? CommitName, string? CommitEmail,string? Workflow=null,string? DevelopmentBranch=null,string? ProductionBranch=null);

/// <summary>Facade for the EXPERIMENTAL bundle tools. Every operation except reading the status requires the master switch.</summary>
public sealed class BundleToolsService : IDisposable
{
    private readonly PanelConfig config;
    private readonly string configPath;
    private readonly Func<BundleManager?> bundles;

    public BundleToolsService(PanelConfig config, string configPath, Func<BundleManager?> bundles, HttpClient? http = null, bool useSystemTools = true)
    {
        this.config = config;
        this.configPath = Path.GetFullPath(configPath);
        this.bundles = bundles;
        var client = http ?? OutboundHttp.Create(TimeSpan.FromMinutes(45));
        if (http is null) client.DefaultRequestHeaders.UserAgent.ParseAdd("BlackICE-Panel/" + PanelUpdates.RunningVersion);
        Http = client;
        ProviderHttp = http ?? OutboundHttp.Create(TimeSpan.FromSeconds(30),false);
        Tools = new Toolchain(BundleLayout.Toolchain(this.configPath), client, useSystemTools);
        Secrets = new SecretStore(BundleLayout.StateDir(this.configPath));
        Projects = new BundleProjects(this);
        Git = new GitService(this);
        Redactor = new Redactor();
        Jobs = new BundleJobs(BundleLayout.Jobs(this.configPath), Redactor);
        foreach (var account in config.BundleTools.Accounts)
            try { if (account.TokenProtected.Length > 0) Redactor.Add(Secrets.Unprotect(account.TokenProtected)); } catch (Exception e) when (e is InvalidOperationException or FormatException) { }
    }

    public Toolchain Tools { get; }
    public HttpClient Http { get; }
    public HttpClient ProviderHttp {get;}
    public BundleProjects Projects { get; }
    public GitService Git { get; }
    public SecretStore Secrets { get; }
    public Redactor Redactor { get; }
    public BundleJobs Jobs { get; }
    public string ConfigPath => configPath;
    public PanelConfig Config => config;
    public BundleManager? Bundles => bundles();

    public BundleToolsSettings Settings => config.BundleTools;

    public void RequireEnabled()
    {
        if (!config.BundleTools.Enabled) throw new InvalidOperationException("bundleTools.disabled");
        if (!config.Configured) throw new InvalidOperationException("setup.required");
    }

    public object Status() => new
    {
        experimental = true,
        enabled = config.BundleTools.Enabled,
        settings = new
        {
            config.BundleTools.UploadEnabled,
            config.BundleTools.SourcesEnabled,
            config.BundleTools.RestartAfterBuild,
            config.BundleTools.BuildUi,
            config.BundleTools.KeepBackups,
            config.BundleTools.MaxUploadMb,
            config.BundleTools.CommitName,
            config.BundleTools.CommitEmail,
            config.BundleTools.Workflow,config.BundleTools.DevelopmentBranch,config.BundleTools.ProductionBranch,
        },
        toolchain = Tools.Status(),
        accounts = PublicAccounts(),
        sourceFolder = BundleLayout.SourceFolder,
        uploadMaxMb = config.BundleTools.MaxUploadMb,
        job = Jobs.Current,
        jobs = Jobs.Recent(),
    };

    public object ApplySettings(string json)
    {
        var patch = JsonSerializer.Deserialize<BundleToolsPatch>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        static bool Text(string? value, int max) => value is not null && value.Length is > 0 && value.Length <= max && !value.Any(c => char.IsControl(c) || c is '<' or '>');
        if (patch.CommitName is not null && !Text(patch.CommitName, 100)) throw new InvalidOperationException("bundleTools.invalid");
        if (patch.CommitEmail is not null && (!Text(patch.CommitEmail, 150) || !patch.CommitEmail.Contains('@'))) throw new InvalidOperationException("bundleTools.invalid");
        if (patch.KeepBackups is < 1 or > 20 || patch.MaxUploadMb is < 1 or > 2048) throw new InvalidOperationException("bundleTools.invalid");
        if(patch.Workflow is not null&&patch.Workflow is not ("review" or "direct" or "branches"))throw new InvalidOperationException("bundleTools.invalid");
        if(patch.DevelopmentBranch is not null)GitService.ValidateRef(patch.DevelopmentBranch);
        if(patch.ProductionBranch is not null)GitService.ValidateRef(patch.ProductionBranch);
        if(Jobs.Busy&&(patch.Enabled==false||patch.SourcesEnabled==false))throw new InvalidOperationException("bundleTools.busy");
        lock (config)
        {
            var s = config.BundleTools;
            if(patch.Workflow is not null)s.Workflow=patch.Workflow;
            if(patch.DevelopmentBranch is not null)s.DevelopmentBranch=patch.DevelopmentBranch;
            if(patch.ProductionBranch is not null)s.ProductionBranch=patch.ProductionBranch;
            if (patch.Enabled is { } enabled) s.Enabled = enabled;
            if (patch.UploadEnabled is { } upload) s.UploadEnabled = upload;
            if (patch.SourcesEnabled is { } sources) s.SourcesEnabled = sources;
            if (patch.RestartAfterBuild is { } restart) s.RestartAfterBuild = restart;
            if (patch.BuildUi is { } ui) s.BuildUi = ui;
            if (patch.KeepBackups is { } keep) s.KeepBackups = keep;
            if (patch.MaxUploadMb is { } max) s.MaxUploadMb = max;
            if (patch.CommitName is not null) s.CommitName = patch.CommitName.Trim();
            if (patch.CommitEmail is not null) s.CommitEmail = patch.CommitEmail.Trim();
            PanelConfig.Save(configPath, config);
        }
        return Status();
    }


    // ---------------------------------------------------------------- bundle lifecycle helpers
    public async Task<string?> BundleStateAsync(string name, CancellationToken ct)
    {
        var manager = Bundles;
        if (manager is null) return null;
        try
        {
            var list = await manager.List(ct);
            return list.Ok ? list.Bundles.FirstOrDefault(b => b.Name == name)?.State ?? (list.CanControl || list.Bundles.All(b=>b.State=="stopped") ? "stopped" : null) : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Stops a running bundle (when the restart option is on), runs the file work, then starts it again.</summary>
    public async Task WithBundleStoppedAsync(string name, Action<string> log, CancellationToken ct, Func<Task> work)
    {
        var restart = await BundleStateAsync(name, ct);
        if (Bundles is not null && restart is null) throw new InvalidOperationException("bundle.changed");
        var stopped = false;
        if (restart == "running" && Bundles is { } manager)
        {
            log("Stopping bundle " + name + "...");
            await manager.Action(name, "stop", ct);
            stopped = true;
        }
        try { await work(); }
        finally
        {
            if (stopped && config.BundleTools.RestartAfterBuild && !new BundleAdministration(config,configPath,this).Disabled(name) && Bundles is { } again)
            {
                try { log("Starting bundle " + name + "..."); await again.Action(name, "start", CancellationToken.None); }
                catch (Exception e) { log("Could not start the bundle: " + e.Message); }
            }
        }
    }

    // ---------------------------------------------------------------- ZIP upload
    private sealed record UploadRef(string File, string? OriginalName, string? Name, bool Replace);

    private void RequireUpload()
    {
        RequireEnabled();
        if (!config.BundleTools.UploadEnabled) throw new InvalidOperationException("bundleTools.disabled");
    }

    private string UploadPath(string file)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(file ?? "", @"\A[0-9a-f]{32}\.zip\z")) throw new InvalidOperationException("bundleTools.upload");
        var path = Path.Combine(BundleLayout.Uploads(configPath), file!);
        if (!File.Exists(path)) throw new InvalidOperationException("bundleTools.upload");
        return path;
    }

    private void SweepUploads()
    {
        var directory = BundleLayout.Uploads(configPath);
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.zip"))
            try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(24)) File.Delete(file); } catch (IOException) { }
    }

    private bool BundleExists(string name)
    {
        var directory = BundleLayout.BundleDir(config, name);
        return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(e => !Path.GetFileName(e).Equals(BundleLayout.SourceFolder, StringComparison.Ordinal));
    }

    public object UploadInspect(string json)
    {
        RequireUpload();
        SweepUploads();
        var args = JsonSerializer.Deserialize<UploadRef>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var path = UploadPath(args.File);
        var report = BundleZip.Inspect(path, args.OriginalName, config.BundleTools.MaxUploadMb * 1024L * 1024L);
        return new { file = args.File, report, exists = report.Name.Length > 0 && BundleExists(report.Name) };
    }

    public object ExistsCheck(string name) => new { exists = BundleLayout.ValidName(name) && BundleExists(name) };

    public BundleJob UploadInstall(string json)
    {
        RequireUpload();
        var args = JsonSerializer.Deserialize<UploadRef>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid");
        var name = BundleLayout.RequireName(args.Name);
        var zip = UploadPath(args.File);
        var target = BundleLayout.BundleDir(config, name);
        var maxBytes = config.BundleTools.MaxUploadMb * 1024L * 1024L;
        return Jobs.Start("upload", name, async ctx =>
        {
            var stage = Path.Combine(BundleLayout.Stage(configPath), ctx.Id);
            try
            {
                ctx.Phase("Checking the archive");
                var report = BundleZip.Inspect(zip, args.OriginalName, maxBytes);
                foreach (var check in report.Checks) ctx.Log($"[{check.Status}] {check.Id} {check.Detail}");
                if (!report.Ok) throw new InvalidOperationException("bundleTools.zipInvalid");
                if (BundleExists(name) && !args.Replace) throw new InvalidOperationException("bundleTools.exists");
                ctx.Phase("Extracting");
                BundleZip.Extract(zip, report.Root, stage, ctx.Token);
                ctx.Phase("Installing");
                await WithBundleStoppedAsync(name, ctx.Log, ctx.Token, () =>
                {
                    BundleSwap.Backup(target, BundleLayout.Backups(configPath), name, config.BundleTools.KeepBackups, ctx.Log);
                    BundleSwap.Apply(stage, target, ctx.Log);
                    return Task.CompletedTask;
                });
                ctx.Result("Installed " + name);
                ctx.Log("Done. Start the bundle from the Bundles list.");
            }
            finally
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { }
                try { File.Delete(zip); } catch (IOException) { }
            }
        });
    }

    private sealed record ToolRef(string Id);

    public void RequireSources()
    {
        RequireEnabled();
        if (!config.BundleTools.SourcesEnabled) throw new InvalidOperationException("bundleTools.disabled");
    }

    public BundleJob ToolInstall(string json)
    {
        RequireSources();
        var id = (JsonSerializer.Deserialize<ToolRef>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid")).Id;
        if (id is not ("git" or "dotnet" or "node")) throw new InvalidOperationException("bundleTools.tool");
        return Jobs.Start("tool-install", id, ctx => Tools.InstallAsync(id, ctx));
    }

    public object ToolRemove(string json)
    {
        RequireSources();
        if (Jobs.Busy) throw new InvalidOperationException("bundleTools.busy");
        Tools.Remove((JsonSerializer.Deserialize<ToolRef>(json, PanelConfig.Json) ?? throw new InvalidOperationException("request.invalid")).Id);
        return Status();
    }

    public async Task<object> ToolLatest(CancellationToken ct)
    {
        RequireSources();
        return await Tools.LatestVersionsAsync(ct);
    }

    // ---------------------------------------------------------------- git accounts
    private sealed record AccountArgs(string? Id, string Type, string? BaseUrl, string? Label, string? Username, string? Token);

    public object PublicAccounts() => config.BundleTools.Accounts.Select(a => new { a.Id, a.Type, a.BaseUrl, a.Label, a.Username, hasToken = a.TokenProtected.Length > 0 }).ToArray();

    public object AccountSave(string json)
    {
        RequireSources();
        var args=JsonSerializer.Deserialize<AccountArgs>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        if(!GitProviders.Types.Contains(args.Type))throw new InvalidOperationException("bundleTools.account");
        var baseUrl=GitProviders.NormalizeBase(args.Type,string.IsNullOrWhiteSpace(args.BaseUrl)?GitProviders.DefaultBase(args.Type):args.BaseUrl);
        var label=(args.Label??"").Trim();var username=(args.Username??"").Trim();var token=(args.Token??"").Trim();
        if(label.Length>60||username.Length>100||label.Any(char.IsControl)||username.Any(char.IsControl)||username.Contains(':')||token.Length>500||token.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))throw new InvalidOperationException("bundleTools.account");
        lock(config){
            var old=args.Id is {Length:>0}?config.BundleTools.Accounts.FirstOrDefault(a=>a.Id==args.Id)??throw new InvalidOperationException("bundleTools.account"):null;
            if(old is null&&(config.BundleTools.Accounts.Count>=20||token.Length==0))throw new InvalidOperationException("bundleTools.account");
            if(old is not null&&(old.Type!=args.Type||old.BaseUrl!=baseUrl)&&token.Length==0)throw new InvalidOperationException("bundleTools.tokenDestination");
            var account=new GitAccount{Id=old?.Id??Guid.NewGuid().ToString("N")[..12],Type=args.Type,BaseUrl=baseUrl,Label=label.Length>0?label:new Uri(baseUrl).Host,Username=username,TokenProtected=token.Length>0?Secrets.Protect(token):old!.TokenProtected};
            var previous=config.BundleTools.Accounts.ToList();
            config.BundleTools.Accounts.RemoveAll(a=>a.Id==account.Id);config.BundleTools.Accounts.Add(account);
            try{PanelConfig.Save(configPath,config);}catch{config.BundleTools.Accounts=previous;throw;}
            Redactor.Add(token);
        }
        return PublicAccounts();
    }

    public object AccountDelete(string id)
    {
        RequireSources();
        lock (config)
        {
            if (config.BundleTools.Accounts.RemoveAll(a => a.Id == id) == 0) throw new InvalidOperationException("bundleTools.account");
            PanelConfig.Save(configPath, config);
        }
        return PublicAccounts();
    }

    public async Task<object> AccountTest(string id, CancellationToken ct)
    {
        RequireSources();
        var account = config.BundleTools.Accounts.FirstOrDefault(a => a.Id == id) ?? throw new InvalidOperationException("bundleTools.account");
        if (account.TokenProtected.Length == 0) throw new InvalidOperationException("bundleTools.tokenRejected");
        var token = Secrets.Unprotect(account.TokenProtected);
        Redactor.Add(token);
        var user = await GitProviders.TestAsync(ProviderHttp, account, token, ct);
        return new { ok = true, user };
    }

    public async Task<object> Remote(string action,string json,CancellationToken ct){
        RequireSources();var args=JsonSerializer.Deserialize<RemoteArgs>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        var account=config.BundleTools.Accounts.FirstOrDefault(a=>a.Id==args.AccountId)??throw new InvalidOperationException("bundleTools.account");
        var token=Secrets.Unprotect(account.TokenProtected);Redactor.Add(token);
        return action=="owners"?await GitProviders.OwnersAsync(ProviderHttp,account,token,ct,args.Write):await GitProviders.RepositoriesAsync(ProviderHttp,account,token,args.Query??"",args.Page,args.Owner??"",ct);
    }
    private sealed record RemoteArgs(string AccountId,string? Query=null,int Page=1,string? Owner=null,bool Write=false);

    public object JobInfo(string id)
    {
        var job = Jobs.Get(id) ?? throw new InvalidOperationException("bundleTools.job");
        return new { job, log = Jobs.Tail(id) };
    }

    public void Dispose() => Jobs.Cancel();
}
