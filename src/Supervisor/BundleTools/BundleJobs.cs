using System.Text;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor.BundleTools;

/// <summary>Removes tokens and credentials from anything that is logged or returned.</summary>
public sealed partial class Redactor
{
    private readonly object sync = new();
    private readonly HashSet<string> secrets = [];

    public void Add(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < 4) return;
        lock (sync) secrets.Add(secret);
    }

    public string Redact(string text)
    {
        string[] snapshot;
        lock (sync) snapshot = [.. secrets];
        foreach (var secret in snapshot) text = text.Replace(secret, "***", StringComparison.Ordinal);
        text = Header().Replace(text, "$1***");
        text = UrlCredentials().Replace(text, "$1***@");
        return text;
    }

    [GeneratedRegex(@"((?:authorization|private-token)\s*[:=]\s*(?:basic|bearer|token)?\s*)[A-Za-z0-9+/=._~-]{8,}", RegexOptions.IgnoreCase)] private static partial Regex Header();
    [GeneratedRegex(@"(https?://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase)] private static partial Regex UrlCredentials();
}

public sealed record BundleJob(string Id, string Kind, string Target, string State, string Phase, DateTimeOffset Started, DateTimeOffset? Ended, string? Error, long Bytes, long Total, string? Result);

public sealed class JobContext(string id, string logFile, Redactor redactor, Action<Func<BundleJob, BundleJob>> update, CancellationToken token)
{
    public string Id { get; } = id;
    public CancellationToken Token { get; } = token;
    private readonly object sync = new();
    public void Log(string line)
    {
        var text = redactor.Redact(line.TrimEnd());
        lock (sync) File.AppendAllText(logFile, text + Environment.NewLine, Encoding.UTF8);
    }
    public void Phase(string phase) { Log("== " + phase); update(j => j with { Phase = phase }); }
    public void Progress(long bytes, long total) => update(j => j with { Bytes = bytes, Total = total });
    public void Result(string result) => update(j => j with { Result = result });
}

/// <summary>One long-running bundle operation at a time (download, build, pull, ...). Logs are kept as files in the state folder.</summary>
public sealed class BundleJobs(string jobsDir, Redactor redactor)
{
    private readonly object sync = new();
    private readonly List<BundleJob> history = [];
    private CancellationTokenSource? cancellation;
    private BundleJob? current;

    public bool Busy { get { lock (sync) return current is { State: "running" }; } }

    public BundleJob Start(string kind, string target, Func<JobContext, Task> work)
    {
        BundleJob job;
        lock (sync)
        {
            if (current is { State: "running" }) throw new InvalidOperationException("bundleTools.busy");
            Directory.CreateDirectory(jobsDir);
            job = new BundleJob(Guid.NewGuid().ToString("N"), kind, target, "running", "start", DateTimeOffset.UtcNow, null, null, 0, 0, null);
            current = job;
            history.Insert(0, job);
            if (history.Count > 25) history.RemoveRange(25, history.Count - 25);
            cancellation = new CancellationTokenSource();
        }
        var token = cancellation!.Token;
        var context = new JobContext(job.Id, Path.Combine(jobsDir, job.Id + ".log"), redactor, Update(job.Id), token);
        context.Log($"{kind} {target}");
        _ = Task.Run(async () =>
        {
            try
            {
                await work(context);
                Finish(job.Id, "succeeded", null);
            }
            catch (OperationCanceledException) { Finish(job.Id, "cancelled", null); }
            catch (Exception e)
            {
                var code = e is InvalidOperationException && Regex.IsMatch(e.Message, @"\A[a-z][a-zA-Z]+\.[a-zA-Z]+\z") ? e.Message : "bundleTools.failed";
                context.Log("ERROR: " + (e is InvalidOperationException ? e.Message : e.GetType().Name + ": " + e.Message));
                Finish(job.Id, "failed", code);
            }
        });
        return Get(job.Id)!;
    }

    private Action<Func<BundleJob, BundleJob>> Update(string id) => change =>
    {
        lock (sync)
        {
            var index = history.FindIndex(j => j.Id == id);
            if (index < 0) return;
            history[index] = change(history[index]);
            if (current?.Id == id) current = history[index];
        }
    };

    private void Finish(string id, string state, string? error) => Update(id)(j => j with { State = state, Phase = state, Ended = DateTimeOffset.UtcNow, Error = error });

    public BundleJob? Get(string id) { lock (sync) return history.FirstOrDefault(j => j.Id == id); }
    public BundleJob? Current { get { lock (sync) return current; } }
    public BundleJob[] Recent() { lock (sync) return [.. history.Take(10)]; }
    public void Cancel() { lock (sync) cancellation?.Cancel(); }

    public string[] Tail(string id, int lines = 200)
    {
        if (!Regex.IsMatch(id, "\\A[0-9a-f]{32}\\z")) return [];
        var file = Path.Combine(jobsDir, id + ".log");
        if (!File.Exists(file)) return [];
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var all = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. all.Skip(Math.Max(0, all.Length - lines)).Select(x => x.TrimEnd('\r'))];
    }
}
