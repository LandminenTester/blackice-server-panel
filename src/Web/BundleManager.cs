using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;

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
            throw new InvalidOperationException("Ungültige Bundle-Aktion.");
        await gate.WaitAsync(ct);
        try
        {
            var current = await ListCore(ct);
            if (!current.CanControl) throw new InvalidOperationException(current.Error ?? "Keine verwaltete Konsole verbunden.");
            var bundle = current.Bundles.SingleOrDefault(x => x.Name == name) ?? throw new InvalidOperationException("Bundle nicht gefunden. Liste aktualisieren.");
            if ((action == "start" && bundle.State != "stopped") || (action != "start" && bundle.State != "running"))
                throw new InvalidOperationException("Der Bundle-Status hat sich geändert. Liste aktualisieren.");
            await ConfirmAction(name, action, ct);
            var result = await ListCore(ct);
            if (!result.Ok || result.Bundles.SingleOrDefault(x => x.Name == name)?.State != (action == "stop" ? "stopped" : "running"))
                throw new InvalidOperationException(result.Error ?? "Replay hat die Aktion nicht bestätigt. Liste aktualisieren und Konsole prüfen.");
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
                status.Error ?? (status.Status?.State == "external" ? "Extern gestarteter Server: Bundle-Status und Aktionen benötigen die verwaltete Konsole." : "Starte den Server, um Bundles zu steuern."));
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
                    if (failure is not null) return new(false, [], false, DateTimeOffset.UtcNow, "Replay: " + failure.Trim());
                    var header = Array.FindIndex(response, x => x.Trim().TrimStart('>').Trim() == "Loaded bundles:");
                    if (header < 0) return new(false, [], false, DateTimeOffset.UtcNow, "Bundle-Ausgabe nicht erkannt. Konsole prüfen und Liste aktualisieren.");
                    var loaded = response[(header + 1)..].Select(x => x.Trim().TrimStart('>').Trim()).Where(x => Name().IsMatch(x) && x != "bundles").ToHashSet(StringComparer.Ordinal);
                    await Observe(status.Status!.Pid, lines, ct);
                    return new(true, known.Union(loaded).Order(StringComparer.Ordinal).Select(x => new BundleInfo(x, states.GetValueOrDefault(x, loaded.Contains(x) ? "unknown" : "stopped"))).ToArray(), true, DateTimeOffset.UtcNow);
                }
                await Task.Delay(100, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, [], false, DateTimeOffset.UtcNow, "Replay antwortet nicht auf die Bundle-Abfrage. Konsole prüfen und Liste aktualisieren."); }
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
                if (failed is not null) throw new InvalidOperationException("Replay: " + failed.Trim());
                if (lines.Any(x => Lifecycle().Match(x) is { Success: true } match && match.Groups[1].Value == expected && match.Groups[2].Value == name)) return;
                await Task.Delay(100, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("Replay hat die Bundle-Aktion nicht bestätigt. Liste aktualisieren und Konsole prüfen."); }
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
            // Recover events older than the supervisor's bounded console buffer after a webhost restart.
            var directory = Path.Combine(config.ServerRoot, "logs");
            if (pid is not null && Directory.Exists(directory))
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid.Value);
                var log = Directory.EnumerateFiles(directory, "server_*.txt")
                    .Where(x => File.GetLastWriteTimeUtc(x) >= process.StartTime.ToUniversalTime()).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (log is not null)
                {
                    using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(file);
                    while (await reader.ReadLineAsync(ct) is { } line) ObserveLine(line);
                }
            }
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
