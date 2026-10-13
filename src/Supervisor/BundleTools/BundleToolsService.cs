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

    public object JobInfo(string id)
    {
        var job = Jobs.Get(id) ?? throw new InvalidOperationException("bundleTools.job");
        return new { job, log = Jobs.Tail(id) };
    }

    public void Dispose() => Jobs.Cancel();
}
