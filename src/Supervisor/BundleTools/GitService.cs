using System.Text;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record GitInfo(string? Branch, string? Commit, bool Dirty, int Ahead, int Behind, string[] Incoming, string? Remote);
public sealed record GitRefs(string[] Branches, string[] Tags, string? Current);

/// <summary>
/// Thin wrapper around the git program. Credentials are handed over through git's environment configuration only (never in URLs, arguments or logs),
/// hooks and credential helpers are disabled and only http(s) remotes are allowed.
/// </summary>
public sealed partial class GitService(BundleToolsService service)
{
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._/-]{0,199}\z")] private static partial Regex RefName();

    public static bool AllowFileRemotes => Environment.GetEnvironmentVariable("PANEL_GIT_ALLOW_FILE") == "1";

    /// <summary>Accepts public http(s) clone URLs without embedded credentials.</summary>
    public static string ValidateRemote(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 300 || url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) throw new InvalidOperationException("bundleTools.remote");
        if (AllowFileRemotes && Directory.Exists(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Host.Length == 0 || url.Contains('"') || url.StartsWith('-')) throw new InvalidOperationException("bundleTools.remote");
        return uri.ToString();
    }

    public static string ValidateRef(string? name)
    {
        if (name is null || !RefName().IsMatch(name) || name.Contains("..") || name.EndsWith('/') || name.EndsWith(".lock", StringComparison.Ordinal)) throw new InvalidOperationException("bundleTools.ref");
        return name;
    }

    public GitAccount? AccountFor(string? id, string url)
    {
        var accounts = service.Config.BundleTools.Accounts;
        if (!string.IsNullOrEmpty(id)) return accounts.FirstOrDefault(a => a.Id == id) ?? throw new InvalidOperationException("bundleTools.account");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return accounts.FirstOrDefault(a => Uri.TryCreate(a.BaseUrl, UriKind.Absolute, out var b) && b.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) && a.TokenProtected.Length > 0);
    }

    private Dictionary<string, string> Environment_(GitAccount? account, string url)
    {
        var env = service.Tools.BuildEnvironment();
        var count = 0;
        void Config(string key, string value) { env[$"GIT_CONFIG_KEY_{count}"] = key; env[$"GIT_CONFIG_VALUE_{count}"] = value; count++; }
        Config("credential.helper", "");
        Config("core.hooksPath", Path.Combine(BundleLayout.Toolchain(service.ConfigPath), "no-hooks"));
        Config("core.fsmonitor", "false");
        Config("protocol.allow", AllowFileRemotes ? "always" : "never");
        Config("protocol.https.allow", "always");
        Config("protocol.http.allow", "always");
        Config("submodule.recurse", "false");
        Config("safe.directory", "*");
        Config("advice.detachedHead", "false");
        if (account is { TokenProtected.Length: > 0 } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var token = service.Secrets.Unprotect(account.TokenProtected);
            service.Redactor.Add(token);
            var user = account.Username.Length > 0 ? account.Username : account.Type == "gitlab" ? "oauth2" : account.Type == "github" ? "x-access-token" : "token";
            var header = "AUTHORIZATION: basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + token));
            service.Redactor.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + token)));
            Config($"http.{uri.Scheme}://{uri.Authority}/.extraheader", header);
        }
        env["GIT_CONFIG_COUNT"] = count.ToString();
        return env;
    }

    public async Task<(int ExitCode, string[] Lines)> RunAsync(string[] args, string workingDirectory, GitAccount? account, string url, CancellationToken ct, Action<string>? log = null, TimeSpan? timeout = null)
    {
        var git = service.Tools.GitExe ?? throw new InvalidOperationException("bundleTools.needGit");
        Directory.CreateDirectory(Path.Combine(BundleLayout.Toolchain(service.ConfigPath), "no-hooks"));
        var lines = new List<string>();
        var result = await ProcessRunner.Run(git, args, workingDirectory, Environment_(account, url), line => { lines.Add(line); log?.Invoke(line); }, ct, timeout ?? TimeSpan.FromMinutes(20));
        if (result.TimedOut) throw new InvalidOperationException("bundleTools.timeout");
        return (result.ExitCode, [.. lines]);
    }

    private static string First(string[] lines) => lines.FirstOrDefault()?.Trim() ?? "";

    public async Task CloneAsync(string url, string? branch, string destination, GitAccount? account, JobContext ctx)
    {
        Directory.CreateDirectory(destination);
        var args = new List<string> { "clone", "--no-tags", "--origin", "origin" };
        if (!string.IsNullOrEmpty(branch)) { args.Add("--branch"); args.Add(ValidateRef(branch)); }
        args.Add("--"); args.Add(url); args.Add(destination);
        var (exit, _) = await RunAsync([.. args], Path.GetDirectoryName(destination)!, account, url, ctx.Token, ctx.Log);
        if (exit != 0) throw new InvalidOperationException("bundleTools.gitFailed");
        // tags are fetched separately so a branch clone still offers them in the version picker
        await RunAsync(["fetch", "--tags", "--quiet", "origin"], destination, account, url, ctx.Token, ctx.Log);
    }

    public async Task<GitInfo> InfoAsync(string source, GitAccount? account, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(source, ".git"))) return new(null, null, false, 0, 0, [], null);
        var (_, branch) = await RunAsync(["rev-parse", "--abbrev-ref", "HEAD"], source, account, "", ct);
        var (_, commit) = await RunAsync(["rev-parse", "--short", "HEAD"], source, account, "", ct);
        var (_, dirty) = await RunAsync(["status", "--porcelain", "--untracked-files=no"], source, account, "", ct);
        var (_, remote) = await RunAsync(["remote", "get-url", "origin"], source, account, "", ct);
        var (counts, count) = await RunAsync(["rev-list", "--left-right", "--count", "HEAD...@{u}"], source, account, "", ct);
        int ahead = 0, behind = 0;
        if (counts == 0 && First(count).Split('\t', ' ') is { Length: >= 2 } parts) { int.TryParse(parts[0], out ahead); int.TryParse(parts[^1], out behind); }
        var incoming = Array.Empty<string>();
        if (behind > 0) { var (_, diff) = await RunAsync(["diff", "--name-status", "HEAD..@{u}"], source, account, "", ct); incoming = [.. diff.Take(200)]; }
        return new(First(branch), First(commit), dirty.Any(l => l.Trim().Length > 0), ahead, behind, incoming, First(remote));
    }

    public async Task FetchAsync(string source, GitAccount? account, string url, JobContext? ctx, CancellationToken ct)
    {
        var (exit, _) = await RunAsync(["fetch", "--prune", "--tags", "--quiet", "origin"], source, account, url, ct, ctx is null ? null : ctx.Log);
        if (exit != 0) throw new InvalidOperationException("bundleTools.gitFailed");
    }

    public async Task PullAsync(string source, bool discardLocal, GitAccount? account, string url, JobContext ctx)
    {
        var info = await InfoAsync(source, account, ctx.Token);
        if (info.Dirty && !discardLocal) throw new InvalidOperationException("bundleTools.gitDirty");
        if (info.Dirty) { ctx.Log("Discarding local changes as requested."); await RunAsync(["reset", "--hard"], source, account, url, ctx.Token, ctx.Log); }
        await FetchAsync(source, account, url, ctx, ctx.Token);
        var (exit, _) = await RunAsync(["merge", "--ff-only", "@{u}"], source, account, url, ctx.Token, ctx.Log);
        if (exit != 0) throw new InvalidOperationException("bundleTools.gitDiverged");
    }

    public async Task<GitRefs> RefsAsync(string source, CancellationToken ct)
    {
        var (_, branches) = await RunAsync(["for-each-ref", "--format=%(refname:short)", "refs/remotes/origin"], source, null, "", ct);
        var (_, tags) = await RunAsync(["for-each-ref", "--sort=-creatordate", "--format=%(refname:short)", "refs/tags"], source, null, "", ct);
        var (_, current) = await RunAsync(["rev-parse", "--abbrev-ref", "HEAD"], source, null, "", ct);
        return new([.. branches.Select(b => b.Trim()).Where(b => b.StartsWith("origin/", StringComparison.Ordinal) && b != "origin/HEAD").Select(b => b[7..]).Where(b => RefName().IsMatch(b)).Take(100)],
            [.. tags.Select(t => t.Trim()).Where(t => RefName().IsMatch(t)).Take(100)], First(current));
    }

    public async Task CheckoutAsync(string source, string kind, string name, JobContext ctx)
    {
        ValidateRef(name);
        var info = await InfoAsync(source, null, ctx.Token);
        if (info.Dirty) throw new InvalidOperationException("bundleTools.gitDirty");
        var args = kind == "tag" ? new[] { "checkout", "--detach", "refs/tags/" + name } : ["checkout", "-B", name, "--track", "origin/" + name];
        var (exit, _) = await RunAsync(args, source, null, "", ctx.Token, ctx.Log);
        if (exit != 0) throw new InvalidOperationException("bundleTools.gitFailed");
    }

    public async Task InitAndPushAsync(string source, string remoteUrl, GitAccount account, JobContext ctx)
    {
        var settings = service.Config.BundleTools;
        async Task Step(params string[] args) { var (exit, _) = await RunAsync(args, source, account, remoteUrl, ctx.Token, ctx.Log); if (exit != 0) throw new InvalidOperationException("bundleTools.gitFailed"); }
        await Step("init", "-b", "main");
        await Step("add", "-A");
        await Step("-c", "user.name=" + settings.CommitName, "-c", "user.email=" + settings.CommitEmail, "commit", "-m", "Initial commit from the BlackICE Server Panel", "--quiet");
        await Step("remote", "add", "origin", remoteUrl);
        await Step("push", "-u", "origin", "main");
    }
}
