using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

public sealed record BundleInfo(string Name, string State);
public sealed record BundleList(bool Ok, BundleInfo[] Bundles, bool CanControl, DateTimeOffset CheckedAt, string? Error = null);

// Console queries and commands share a gate. Markers delimit real server output, not terminal input echo.
public sealed partial class BundleManager(PipeClient pipe, PanelConfig config)
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, string> states = new(StringComparer.Ordinal);
    private int? observedPid;
    public async Task<Reply> Command(Request request, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return await pipe.Call(request, ct); }
        finally { gate.Release(); }
    }
    public async Task<BundleList> List(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await ListCore(ct); }
        finally { gate.Release(); }
    }
    public async Task<BundleList> Action(string name, string action, CancellationToken ct)
    {
        if (!Name().IsMatch(name) || action is not ("start" or "restart" or "stop"))
            throw new InvalidOperationException("bundle.invalid");
        await gate.WaitAsync(ct);
        try
        {
            var current = await ListCore(ct);
            if (!current.CanControl) throw new InvalidOperationException(current.Error ?? "console.unavailable");
            var bundle = current.Bundles.SingleOrDefault(x => x.Name == name) ?? throw new InvalidOperationException("bundle.missing");
            if ((action == "start" && bundle.State != "stopped") || (action != "start" && bundle.State != "running"))
                throw new InvalidOperationException("bundle.changed");
            await ConfirmAction(name, action, ct);
            var result = await ListCore(ct);
            if (!result.Ok || result.Bundles.SingleOrDefault(x => x.Name == name)?.State != (action == "stop" ? "stopped" : "running"))
                throw new InvalidOperationException(result.Error ?? "bundle.timeout");
            return result;
        }
        finally { gate.Release(); }
    }
    private async Task<BundleList> ListCore(CancellationToken ct)
    {
        var status = await pipe.Call(new("status"), ct);
        var known = Discover();
        if (!status.Ok || status.Status?.State != "running")
            return new(status.Ok, known.Select(x => new BundleInfo(x, status.Status?.State == "stopped" ? "stopped" : "unknown")).ToArray(), false, DateTimeOffset.UtcNow,
                status.Error ?? (status.Status?.State == "external" ? "server.external" : "bundle.startServer"));
        var token = Guid.NewGuid().ToString("N")[..12];
        var begin = "panel-query-" + token + "-begin";
        var end = "panel-query-" + token + "-end";
        foreach (var text in new[] { "echo " + begin, "bundles", "echo " + end })
        {
            var reply = await pipe.Call(new("command", text), ct);
            if (!reply.Ok) return new(false, [], false, DateTimeOffset.UtcNow, reply.Error);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            while (true)
            {
                var snapshot = await pipe.Call(new("console"), timeout.Token);
                if (!snapshot.Ok) return new(false, [], false, DateTimeOffset.UtcNow, snapshot.Error);
                var lines = snapshot.Lines ?? [];
                var start = Array.FindLastIndex(lines, x => x.Trim().TrimStart('>').Trim() == begin);
                var stop = Array.FindLastIndex(lines, x => x.Trim().TrimStart('>').Trim() == end);
                if (start >= 0 && stop > start)
                {
                    var response = lines[(start + 1)..stop];
                    var failure = response.FirstOrDefault(x => x.Contains("[server_ugc]") && (x.Contains("Failed", StringComparison.OrdinalIgnoreCase) || x.Contains("not found", StringComparison.OrdinalIgnoreCase)));
                    if (failure is not null) return new(false, [], false, DateTimeOffset.UtcNow, "bundle.failed");
                    var header = Array.FindIndex(response, x => x.Trim().TrimStart('>').Trim() == "Loaded bundles:");
                    if (header < 0) {
                        if(!TryStructured(response,out var table))return new(false, [], false, DateTimeOffset.UtcNow, "bundle.output");
                        observedPid=status.Status!.Pid;states.Clear();foreach(var item in table)states[item.Key]=item.Value;
                        return new(true,known.Union(table.Keys).Order(StringComparer.Ordinal).Select(x=>new BundleInfo(x,table.GetValueOrDefault(x,"unknown"))).ToArray(),true,DateTimeOffset.UtcNow);
                    }
                    var loaded = response[(header + 1)..].Select(x => x.Trim().TrimStart('>').Trim()).Where(x => Name().IsMatch(x) && x != "bundles").ToHashSet(StringComparer.Ordinal);
                    await Observe(status.Status!.Pid, lines, ct);
                    return new(true, known.Union(loaded).Order(StringComparer.Ordinal).Select(x => new BundleInfo(x, states.GetValueOrDefault(x, loaded.Contains(x) ? "unknown" : "stopped"))).ToArray(), true, DateTimeOffset.UtcNow);
                }
                await Task.Delay(100, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, [], false, DateTimeOffset.UtcNow, "bundle.timeout"); }
    }
    public static bool TryStructured(string[] response,out Dictionary<string,string> states) {
        states=new(StringComparer.Ordinal);var lines=response.Select(x=>x.Trim().TrimStart('>').Trim()).Where(x=>x.Length>0).ToArray();int index=0;
        foreach(var section in new[]{("Running","running"),("Available, not running","stopped")}) {
            if(index>=lines.Length)return false;
            var header=Regex.Match(lines[index++],"^"+Regex.Escape(section.Item1)+@" \(([0-9]+)\):$");
            if(!header.Success||!int.TryParse(header.Groups[1].Value,out var count)||count>10000)return false;
            if(index>=lines.Length||!Regex.IsMatch(lines[index++],@"^NAME\s+VERSION\s+CLIENT\s+SERVER\s+UI$"))return false;
            for(var n=0;n<count;n++) {
                if(index>=lines.Length)return false;var columns=Regex.Split(lines[index++],@"\s+");
                if(columns.Length<5||!Name().IsMatch(columns[0])||!states.TryAdd(columns[0],section.Item2))return false;
            }
        }
        return index==lines.Length;
    }
    private async Task ConfirmAction(string name, string action, CancellationToken ct)
    {
        var marker = "panel-query-" + Guid.NewGuid().ToString("N")[..12] + "-begin";
        var reply = await pipe.Call(new("command", "echo " + marker), ct);
        if (!reply.Ok) throw new InvalidOperationException(reply.Error);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            // Do not accept historical events or terminal echo as action confirmation.
            await WaitLines(marker, timeout.Token);
            reply = await pipe.Call(new("command", action + " " + name), timeout.Token);
            if (!reply.Ok) throw new InvalidOperationException(reply.Error);
            var expected = action == "stop" ? "Stopped" : action == "restart" ? "Restarted" : "Started";
            while (true)
            {
                var lines = await WaitLines(marker, timeout.Token);
                var failed = lines.FirstOrDefault(x => x.Contains("[server_ugc]") && x.Contains("Failed", StringComparison.OrdinalIgnoreCase));
                if (failed is not null) throw new InvalidOperationException("bundle.failed");
                if (lines.Any(x => Lifecycle().Match(x) is { Success: true } match && match.Groups[1].Value == expected && match.Groups[2].Value == name)) return;
                await Task.Delay(100, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("bundle.timeout"); }
    }
    private async Task<string[]> WaitLines(string marker, CancellationToken ct)
    {
        while (true)
        {
            var snapshot = await pipe.Call(new("console"), ct);
            if (!snapshot.Ok) throw new InvalidOperationException(snapshot.Error);
            var lines = snapshot.Lines ?? [];
            var start = Array.FindLastIndex(lines, x => x.Trim().TrimStart('>').Trim() == marker);
            if (start >= 0) return lines[(start + 1)..];
            await Task.Delay(100, ct);
        }
    }
    private async Task Observe(int? pid, string[] console, CancellationToken ct)
    {
        if (pid != observedPid)
        {
            states.Clear(); observedPid = pid;
            foreach (var line in (await pipe.Call(new("logs"), ct)).Lines ?? []) ObserveLine(line);
        }
        var start = Array.FindLastIndex(console, x => x.Contains("[server_net] Starting server on") || x.Trim() == "Console fixture ready");
        foreach (var line in console.Skip(start < 0 ? 0 : start)) ObserveLine(line);
    }
    private void ObserveLine(string line)
    {
        var lifecycle = Lifecycle().Match(line);
        if (lifecycle.Success) { states[lifecycle.Groups[2].Value] = lifecycle.Groups[1].Value == "Stopped" ? "stopped" : "running"; return; }
        var discovered = Discovery().Match(line);
        if (discovered.Success) states[discovered.Groups[1].Value] = "stopped";
    }
    [GeneratedRegex(@"\[server_ugc\] (Started|Restarted|Stopped) bundle ([A-Za-z0-9][A-Za-z0-9_.-]{0,127})(?:\s|$)")] private static partial Regex Lifecycle();
    [GeneratedRegex(@"\[server_ugc\] (?:Discovered new|Removed old) UgcBundle\s*'([A-Za-z0-9][A-Za-z0-9_.-]{0,127})'")] private static partial Regex Discovery();
    private string[] Discover()
    {
        var directory = Path.Combine(config.ServerRoot, "data", "ugc");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateDirectories(directory).Where(x => File.Exists(Path.Combine(x, "config.toml")))
            .Select(Path.GetFileName).OfType<string>().Where(x => Name().IsMatch(x)).Order(StringComparer.Ordinal).ToArray();
    }
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z")] private static partial Regex Name();
}
