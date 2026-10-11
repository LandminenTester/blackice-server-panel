using System.IO.Pipes;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This release implements the Windows backend. Docker adapter is a later milestone.");
var configPath = Path.GetFullPath(args.FirstOrDefault() ?? ".local/panel.json");
var config = PanelConfig.Load(configPath);
Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
using var single = new FileStream(Path.Combine(Path.GetDirectoryName(configPath)!, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
await using var backend = new WindowsServerBackend(config);
backend.BeginMonitoring();
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
Console.WriteLine("Replay supervisor ready. Webhost can be stopped independently.");
while (!shutdown.IsCancellationRequested)
{
    using var pipe = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try
    {
        await pipe.WaitForConnectionAsync(shutdown.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.StopTimeoutSeconds + 10));
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(timeout.Token);
        Reply reply;
        try
        {
            if (line is null || line.Length > 8192) throw new InvalidOperationException("Invalid request.");
            var request = JsonSerializer.Deserialize<Request>(line, PanelConfig.Json) ?? throw new InvalidOperationException("Invalid request.");
            switch (request.Action)
            {
                case "status": reply = new(true, Status: backend.Status()); break;
                case "console": reply = new(true, Lines: backend.ConsoleSnapshot()); break;
                case "logs": reply = new(true, Lines: backend.LogSnapshot()); break;
                case "start": await backend.StartAsync(); reply = new(true, Status: backend.Status()); break;
                case "stop": await backend.StopAsync(); reply = new(true, Status: backend.Status()); break;
                case "force-stop": await backend.StopAsync(true); reply = new(true, Status: backend.Status()); break;
                case "restart": await backend.StopAsync(); await backend.StartAsync(); reply = new(true, Status: backend.Status()); break;
                case "command": await backend.SendAsync(request.Command ?? ""); reply = new(true); break;
                case "settings":
                    if (request.ShutdownMode is not ("keep-running" or "stop-server")) throw new InvalidOperationException("Invalid shutdown mode.");
                    config.ShutdownMode = request.ShutdownMode;
                    var temporary = configPath + ".tmp";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(config, PanelConfig.Json), timeout.Token);
                    File.Move(temporary, configPath, overwrite: true);
                    reply = new(true, Status: backend.Status()); break;
                case "webhost-stopping":
                    if (config.ShutdownMode == "stop-server" && backend.Status().State == "running") await backend.StopAsync();
                    reply = new(true, Status: backend.Status()); break;
                case "shutdown":
                    if (backend.Status().State == "running") throw new InvalidOperationException("Stop the managed game server before stopping its supervisor.");
                    reply = new(true, Status: backend.Status()); shutdown.Cancel(); break;
                default: throw new InvalidOperationException("Unknown supervisor action.");
            }
        }
        catch (Exception e) { reply = new(false, Error: e.Message); }
        await writer.WriteLineAsync(JsonSerializer.Serialize(reply, PanelConfig.Json));
    }
    catch (OperationCanceledException) { if (shutdown.IsCancellationRequested) break; }
    catch (IOException) { /* client disconnected; keep the server running */ }
}
