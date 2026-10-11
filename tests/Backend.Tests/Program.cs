using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ConPTY tests require Windows.");
var root = Path.GetFullPath(args.FirstOrDefault() ?? throw new ArgumentException("Pass an isolated fixture or test server directory."));
await using var backend = new WindowsServerBackend(new PanelConfig { ServerRoot = root, StopTimeoutSeconds = 10, ServerArguments = args.Skip(1).ToArray() });
Assert(backend.Status().State == "stopped", "initial stopped state");
await backend.StartAsync();
Assert(backend.Status().State == "running", "start owns live process");
await Reject(() => backend.StartAsync(), "duplicate start rejected");
await Reject(() => backend.SendAsync("echo test;exit"), "command chaining rejected");
await Reject(() => backend.SendAsync("exit"), "raw exit rejected");
await backend.SendAsync("echo panel-smoke-verified");
for (var i = 0; i < 40 && !backend.ConsoleSnapshot().Any(x => x.Contains("panel-smoke-verified") && !x.Contains("echo ")); i++) await Task.Delay(250);
Assert(backend.ConsoleSnapshot().Any(x => x.Contains("panel-smoke-verified") && !x.Contains("echo ")), "console round trip (server response, not terminal input echo)");
await backend.StopAsync();
Assert(backend.Status().State == "stopped", "graceful stop");
await backend.StartAsync();
await backend.StopAsync(true);
Assert(backend.Status().State == "stopped", "force stop of owned process");
Console.WriteLine("8 backend checks passed.");
static void Assert(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); }
static async Task Reject(Func<Task> action, string description) { try { await action(); } catch (InvalidOperationException) { Console.WriteLine("PASS " + description); return; } throw new Exception(description); }
