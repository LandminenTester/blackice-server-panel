using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record BundleToolsPatch(bool? Enabled, bool? UploadEnabled, bool? SourcesEnabled, bool? RestartAfterBuild, bool? BuildUi, int? KeepBackups, int? MaxUploadMb, string? CommitName, string? CommitEmail);

/// <summary>Facade for the EXPERIMENTAL bundle tools. Every operation except reading the status requires the master switch.</summary>
public sealed class BundleToolsService : IDisposable
{
    private readonly PanelConfig config;
    private readonly string configPath;
    private readonly Func<BundleManager?> bundles;

    public BundleToolsService(PanelConfig config, string configPath, Func<BundleManager?> bundles)
    {
        this.config = config;
        this.configPath = Path.GetFullPath(configPath);
        this.bundles = bundles;
        Secrets = new SecretStore(BundleLayout.StateDir(this.configPath));
        Redactor = new Redactor();
        Jobs = new BundleJobs(BundleLayout.Jobs(this.configPath), Redactor);
        foreach (var account in config.BundleTools.Accounts)
            try { if (account.TokenProtected.Length > 0) Redactor.Add(Secrets.Unprotect(account.TokenProtected)); } catch (Exception e) when (e is InvalidOperationException or FormatException) { }
    }

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
        },
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
        lock (config)
        {
            var s = config.BundleTools;
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
            return list.CanControl ? list.Bundles.FirstOrDefault(b => b.Name == name)?.State : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Stops a running bundle (when the restart option is on), runs the file work, then starts it again.</summary>
    public async Task WithBundleStoppedAsync(string name, Action<string> log, CancellationToken ct, Func<Task> work)
    {
        var restart = config.BundleTools.RestartAfterBuild ? await BundleStateAsync(name, ct) : null;
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
            if (stopped && Bundles is { } again)
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

    public object JobInfo(string id)
    {
        var job = Jobs.Get(id) ?? throw new InvalidOperationException("bundleTools.job");
        return new { job, log = Jobs.Tail(id) };
    }

    public void Dispose() => Jobs.Cancel();
}
