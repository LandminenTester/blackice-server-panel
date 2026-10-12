using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed class PosixServerBackend(PanelConfig config,Func<string[]>? launchArguments=null) : IServerBackend
{
    private readonly SemaphoreSlim gate=new(1);
    private readonly object outputLock=new();
    private readonly Queue<string> output=new();
    private string remainder="";
    private Process? process;
    private Stream? terminal;
    private CancellationTokenSource? reading;
    private Task? reader;
    private readonly DockerEngine? docker=config.Backend=="docker"?new(config):null;
    private readonly HashSet<string> bundleStates=new();
    public int? ExitCode(){if(docker is not null){try{return docker.Inspect().GetAwaiter().GetResult().GetProperty("State").GetProperty("ExitCode").GetInt32();}catch{return null;}}return process?.HasExited==true?process.ExitCode:null;}
    public ServerStatus Status()
    {
        if(docker is not null) {
            try {var data=docker.Inspect().GetAwaiter().GetResult();return new(data.GetProperty("State").GetProperty("Running").GetBoolean()?"running":"stopped", data.GetProperty("State").GetProperty("Pid").GetInt32(),config.ServerRoot,config.ShutdownMode);}
            catch{return new("unknown",null,config.ServerRoot,config.ShutdownMode,"docker.unavailable");}
        }
        if(process is not null && !process.HasExited)return new("running",process.Id,config.ServerRoot,config.ShutdownMode);
        foreach(var directory in Directory.EnumerateDirectories("/proc").Where(x=>int.TryParse(Path.GetFileName(x),out _))) {
            try {var exe=File.ResolveLinkTarget(Path.Combine(directory,"exe"),false)?.FullName;if(exe==Path.Combine(config.ServerRoot,"REServer"))return new("external",int.Parse(Path.GetFileName(directory)),config.ServerRoot,config.ShutdownMode,"server.external");} catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return new("stopped",null,config.ServerRoot,config.ShutdownMode);
    }
    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try {
            if(Status().State!="stopped")throw new InvalidOperationException("server.running");
            if(docker is not null) { await docker.Action("start"); await OpenDockerTerminal(); return; }
            var helper=Path.Combine(AppContext.BaseDirectory,"replay-terminal");
            if(!File.Exists(helper))throw new InvalidOperationException("server.helper");
            var info=new ProcessStartInfo(helper){WorkingDirectory=config.ServerRoot,RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false};
            info.Environment["LD_LIBRARY_PATH"]=config.ServerRoot; info.ArgumentList.Add(Path.Combine(config.ServerRoot,"REServer"));foreach(var arg in launchArguments?.Invoke()??config.ServerArguments)info.ArgumentList.Add(arg);
            process?.Dispose();process=Process.Start(info)!;terminal=process.StandardInput.BaseStream;reading=new();
            reader=Read(process.StandardOutput.BaseStream,reading.Token);NativeConsole.Server(config);
        } finally {gate.Release();}
    }
    public async Task Reconnect()
    {
        if(docker is not null && Status().State=="running" && terminal is null)await OpenDockerTerminal();
    }
    private async Task OpenDockerTerminal() {terminal=await docker!.Attach(CancellationToken.None);reading=new();reader=Read(terminal,reading.Token);}
    private async Task Read(Stream stream,CancellationToken ct)
    {
        var buffer=new byte[65536];var decoder=Encoding.UTF8.GetDecoder();var chars=new char[65536];
        try {int count;while((count=await stream.ReadAsync(buffer,ct))>0) {var n=decoder.GetChars(buffer,0,count,chars,0);Append(new string(chars,0,n));}}catch(OperationCanceledException){}catch(IOException){}
    }
    private void Append(string chunk)
    {
        lock(outputLock) {remainder=Regex.Replace(remainder+chunk,@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))","").Replace("\r","\n");var lines=remainder.Split('\n');foreach(var line in lines[..^1])if(!string.IsNullOrWhiteSpace(line))output.Enqueue(line);remainder=lines[^1];if(remainder.Length>8192){output.Enqueue(remainder[..8192]);remainder="";}while(output.Count>1500)output.Dequeue();}
    }
    public async Task SendAsync(string command)
    {
        if(string.IsNullOrWhiteSpace(command)||command.Length>1024||command.Any(char.IsControl)||command.Contains(';')||command.Split(' ',StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() is "exit" or "update")throw new InvalidOperationException("command.invalid");
        await gate.WaitAsync();try{await Reconnect();if(Status().State!="running"||terminal is null)throw new InvalidOperationException("console.unavailable");await terminal.WriteAsync(Encoding.UTF8.GetBytes(command+"\r"));await terminal.FlushAsync();}finally{gate.Release();}
    }
    public async Task StopAsync(bool force=false)
    {
        await gate.WaitAsync();
        try {
            var status=Status();if(status.State=="external")throw new InvalidOperationException("server.external");if(status.State!="running")return;
            if(docker is not null) {
                if(force)await docker.Action("kill");else {
                    await Reconnect();await terminal!.WriteAsync(Encoding.UTF8.GetBytes("exit\r"));await terminal.FlushAsync();
                    var deadline=DateTimeOffset.UtcNow.AddSeconds(config.StopTimeoutSeconds);
                    while(Status().State=="running"){if(DateTimeOffset.UtcNow>=deadline)throw new InvalidOperationException("server.stopTimeout");await Task.Delay(200);}
                }
            }
            else {
                if(process is null||process.HasExited)return;
                // The retained Process instance is the owned terminal helper; never use a cached naked PID.
                if(force)process.Kill(true);else {await terminal!.WriteAsync(Encoding.UTF8.GetBytes("exit\r"));await terminal.FlushAsync();}
                try {await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(config.StopTimeoutSeconds));}catch(TimeoutException){throw new InvalidOperationException("server.stopTimeout");}
            }
            reading?.Cancel();terminal?.Dispose();terminal=null;
        }finally{gate.Release();}
    }
    public string[] ConsoleSnapshot(){lock(outputLock)return output.Append(remainder).Where(x=>x.Length>0).ToArray();}
    public string[] LogSnapshot() => docker is not null?docker.Logs().GetAwaiter().GetResult():BackendFiles.Logs(config.ServerRoot);
    public async ValueTask DisposeAsync(){reading?.Cancel();if(Status().State=="running")await StopAsync();terminal?.Dispose();process?.Dispose();if(reader is not null)await reader;reading?.Dispose();gate.Dispose();}
}
public static class BackendFiles
{
    public static string[] Discover(string root){var path=Path.Combine(root,"data","ugc");return Directory.Exists(path)?Directory.EnumerateDirectories(path).Where(x=>File.Exists(Path.Combine(x,"config.toml"))).Select(Path.GetFileName).OfType<string>().ToArray():[];}
    public static string[] Logs(string root){var path=Path.Combine(root,"logs");if(!Directory.Exists(path))return [];var file=Directory.EnumerateFiles(path,"server_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(file is null)return [];using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);if(stream.Length>1024*1024)stream.Seek(-1024*1024,SeekOrigin.End);using var reader=new StreamReader(stream);return reader.ReadToEnd().Split('\n');}
}
