using System.Diagnostics;
using System.Text;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record ProcessResult(int ExitCode, bool TimedOut);

/// <summary>Runs external build tools with a scrubbed environment, capped output and kill-on-cancel. No shell is ever involved.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> Run(string file, IEnumerable<string> args, string workingDirectory, IDictionary<string, string> environment, Action<string> log, CancellationToken ct, TimeSpan timeout, long maxOutputBytes = 8 * 1024 * 1024)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        foreach (var (key, value) in environment) info.Environment[key] = value;

        using var process = new Process { StartInfo = info };
        long written = 0;
        var capped = false;
        void Line(string? line)
        {
            if (line is null) return;
            var size = Interlocked.Add(ref written, line.Length + 1);
            if (size > maxOutputBytes) { if (!capped) { capped = true; log("[output limit reached - further output is dropped]"); } return; }
            log(line);
        }
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        try { if (!process.Start()) throw new InvalidOperationException("bundleTools.process"); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("bundleTools.process"); }
        try { if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            process.WaitForExit(); // flush the asynchronous output readers
            return new ProcessResult(process.ExitCode, false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-1, true);
        }
    }
}
