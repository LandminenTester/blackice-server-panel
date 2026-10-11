Console.WriteLine("Console fixture ready");
var loaded = new HashSet<string>(StringComparer.Ordinal) { "fixture-bundle" };
var available = new HashSet<string>(StringComparer.Ordinal) { "fixture-bundle", "parked-bundle", "broken-bundle" };
foreach (var bundle in available) Console.WriteLine("[server_ugc] Discovered new UgcBundle '" + bundle + "'");
Console.WriteLine("[server_ugc] Started bundle fixture-bundle");
var malformed = false;
while (Console.ReadLine() is { } line)
{
    if (line == "exit") break;
    if (line.StartsWith("echo ")) Console.WriteLine(line[5..]);
    else if (line == "fixture-malformed-bundles") malformed = true;
    else if (line == "bundles")
    {
        if (malformed) { Console.WriteLine("Unrecognized bundle format"); malformed = false; }
        else { Console.WriteLine("Loaded bundles:"); foreach (var name in available.Order()) Console.WriteLine(name); }
    }
    else if (line.Split(' ', 2) is [var action, var name] && action is "start" or "stop" or "restart")
    {
        if (!File.Exists(Path.Combine("data", "ugc", name, "config.toml")) || name == "broken-bundle")
            Console.WriteLine("[server_ugc] Failed to load bundle " + name);
        else
        {
            if (action == "stop") loaded.Remove(name); else loaded.Add(name);
            // Replay confirms lifecycle work asynchronously, after later commands may have answered.
            _ = Task.Run(async () => { await Task.Delay(250); Console.WriteLine("[server_ugc] " + (action == "stop" ? "Stopped" : action == "restart" ? "Restarted" : "Started") + " bundle " + name); });
        }
    }
    else Console.WriteLine("Unknown fixture command");
}
