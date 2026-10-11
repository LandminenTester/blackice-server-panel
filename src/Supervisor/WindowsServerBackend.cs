using System.Diagnostics;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed partial class WindowsServerBackend(PanelConfig config) : IServerBackend
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly object outputLock = new();
    private readonly Queue<string> output = new();
    private string remainder = "";
    private ConPtySession? session;
    private string? detail;
    private bool desiredRunning;
    private DateTimeOffset lastRestart;
    private readonly CancellationTokenSource monitorStop = new();
    private Task? monitor;
    private string Exe => Path.Combine(config.ServerRoot, "REServer.exe");
    public void BeginMonitoring() => monitor = Task.Run(async () => {
        while (!monitorStop.IsCancellationRequested)
        {
            try { await Task.Delay(2000, monitorStop.Token); } catch (OperationCanceledException) { break; }
            if (desiredRunning && config.AutoRestart && session is not null && session.Process.HasExited && DateTimeOffset.UtcNow - lastRestart > TimeSpan.FromSeconds(10))
            { try { lastRestart = DateTimeOffset.UtcNow; await StartAsync(); } catch (Exception e) { detail = e.Message; } }
        }
    });
    private Process? FindExternal()
    {
        foreach (var p in Process.GetProcessesByName("REServer"))
        {
            try { if (!p.HasExited && string.Equals(Path.GetFullPath(p.MainModule!.FileName), Exe, StringComparison.OrdinalIgnoreCase)) return p; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException) { }
            p.Dispose();
        }
        return null;
    }
    public ServerStatus Status()
    {
        if (session is not null && !session.Process.HasExited) return new("running", session.Process.Id, config.ServerRoot, config.ShutdownMode, detail);
        using var external = FindExternal();
        return external is not null ? new("external", external.Id, config.ServerRoot, config.ShutdownMode, "server.external")
            : new("stopped", null, config.ServerRoot, config.ShutdownMode, detail);
    }
    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (Status().State != "stopped") throw new InvalidOperationException("server.running");
            if (!File.Exists(Exe)) throw new FileNotFoundException("server.files");
            session?.Dispose(); session = null;
            var next = new ConPtySession();
            try { next.Start(Exe, config.ServerRoot, Append, config.ServerArguments); session = next; }
            catch { next.Dispose(); throw; }
            desiredRunning = true; detail = null;NativeConsole.Server(config);
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync(bool force = false)
    {
        await gate.WaitAsync();
        try
        {
            desiredRunning = false;
            if (session is null || session.Process.HasExited)
            {
                if (Status().State == "external") throw new InvalidOperationException("server.external");
                return;
            }
            // Check owned process identity before any destructive process operation.
            if (!string.Equals(session.ImagePath(), Exe, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("server.identity");
            if (force) session.Process.Kill(entireProcessTree: true);
            else await session.SendAsync("exit");
            try { await session.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(config.StopTimeoutSeconds)); detail = null; }
            catch (TimeoutException) { detail = "server.stopTimeout"; throw new InvalidOperationException(detail); }
            session.Dispose(); session = null;
        }
        finally { gate.Release(); }
    }
    public async Task SendAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 1024 || command.Any(char.IsControl) || command.Contains(';'))
            throw new InvalidOperationException("command.invalid");
        var verb = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (verb.Equals("exit", StringComparison.OrdinalIgnoreCase) || verb.Equals("update", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("command.invalid");
        await gate.WaitAsync();
        try
        {
            if (session is null || session.Process.HasExited) throw new InvalidOperationException("console.unavailable");
            await session.SendAsync(command);
        }
        finally { gate.Release(); }
    }
    private void Append(string chunk)
    {
        lock (outputLock)
        {
            // Terminal commands are removed; the browser renders text, never HTML.
            remainder = Ansi().Replace(remainder + chunk, "").Replace("\r", "\n");
            var lines = remainder.Split('\n');
            foreach (var line in lines[..^1]) if (!string.IsNullOrWhiteSpace(line)) output.Enqueue(line);
            remainder = lines[^1];
            if (remainder.Length > 8192) { output.Enqueue(remainder[..8192]); remainder = ""; }
            while (output.Count > 1500) output.Dequeue();
        }
    }
    public string[] ConsoleSnapshot() { lock (outputLock) return output.Append(remainder).Where(x => x.Length > 0).ToArray(); }
    public string[] LogSnapshot()
    {
        var dir = Path.Combine(config.ServerRoot, "logs");
        if (!Directory.Exists(dir)) return [];
        var file = Directory.EnumerateFiles(dir, "server_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (file is null) return [];
        try {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 256 * 1024) stream.Seek(-256 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').TakeLast(1000).Select(x => x.TrimEnd('\r')).ToArray();
        } catch (IOException) { return ["logs.changed"]; }
    }
    public async ValueTask DisposeAsync()
    {
        monitorStop.Cancel(); if (monitor is not null) await monitor;
        if (session is not null && !session.Process.HasExited) await StopAsync();
        session?.Dispose(); gate.Dispose(); monitorStop.Dispose();
    }
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))")] private static partial Regex Ansi();
}
