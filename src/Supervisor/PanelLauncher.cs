using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public static class PanelLauncher
{
    public static async Task Run(string[] args)
    {
        var root=AppContext.BaseDirectory;
        var configIndex=Array.IndexOf(args,"--config");
        var path=Path.GetFullPath((configIndex>=0&&configIndex+1<args.Length?args[configIndex+1]:Environment.GetEnvironmentVariable("REPLAY_PANEL_CONFIG"))??Path.Combine(root,".local","panel.json"));
        var supervisor=Path.Combine(root,"artifacts","supervisor",OperatingSystem.IsWindows()?"Supervisor.exe":"Supervisor");
        var web=Path.Combine(root,"artifacts","web",OperatingSystem.IsWindows()?"Web.exe":"Web");
        if(!File.Exists(supervisor)||!File.Exists(web))throw new InvalidOperationException("Release files are missing. Keep Start together with the artifacts directory.");
        if(!OperatingSystem.IsWindows()&&!args.Contains("--in-terminal")&&Console.IsInputRedirected&&NativeConsole.HasDesktop){
            NativeConsole.Open(Environment.ProcessPath!,["--in-terminal",..args],"replay-panel");
            return;
        }
        var state=Path.GetDirectoryName(path)!;Directory.CreateDirectory(state);
        if(OperatingSystem.IsWindows()){
            var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
            var user=WindowsIdentity.GetCurrent().User!;
            foreach(var sid in new[]{user,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(state).SetAccessControl(security);
        }else File.SetUnixFileMode(state,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var config=PanelConfig.Load(path);
        NativeConsole.ConfigPath=path;
        FileStream instance;try{instance=new FileStream(Path.Combine(state,"launcher.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){Console.WriteLine("A Start terminal is already open.");return;}
        using(instance) {
        using var startup=new CancellationTokenSource();
        var launcherEndpoint=Transport.Listen(config.PipeName+".launcher",async(stream,ct)=>{using var reader=new StreamReader(stream,leaveOpen:true);if(await reader.ReadLineAsync(ct)=="close")startup.Cancel();},startup.Token);
        try {
        if(!await Available(config.PipeName)){
            Start(supervisor,[path],Path.GetDirectoryName(supervisor)!,state,"supervisor",path);
            for(var i=0;i<100&&!await Available(config.PipeName);i++)await Task.Delay(100,startup.Token);
            if(!await Available(config.PipeName))throw new InvalidOperationException("Supervisor failed to start. Read .local/supervisor.err.log.");
        }
        config=PanelConfig.Load(path);
        if(!await Available(config.PipeName+".web")){
            Start(web,[],Path.GetDirectoryName(web)!,state,"web",path);
            for(var i=0;i<100&&!await Available(config.PipeName+".web");i++)await Task.Delay(100);
            if(!await Available(config.PipeName+".web"))throw new InvalidOperationException("Webhost failed to start. Read .local/web.err.log.");
        }
        Console.WriteLine(config.Url);
        if(!config.Configured) {
            var log=Path.Combine(state,"supervisor.log");
            if(File.Exists(log)) {using var file=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(file);string? latest=null;while(reader.ReadLine() is {} line)if(line.StartsWith("Setup key:"))latest=line;if(latest is not null)Console.WriteLine(latest);}
        }
        if(config.Configured&&config.AutoStartServer){
            var status=await ControlConsole.Call(config,new("status"));
            if(status.Status?.State=="stopped"){var reply=await ControlConsole.Call(config,new("start"));if(!reply.Ok)Console.WriteLine(reply.Error);}
        }
        if(config.Configured && (await ControlConsole.Call(config,new("status"))).Status?.State=="running")NativeConsole.Server(config);
        startup.Token.ThrowIfCancellationRequested();
        try {await ControlConsole.Run(path,startup.Token);}catch(IOException){Console.WriteLine("A control terminal is already open.");}
        }finally{startup.Cancel();try{await launcherEndpoint;}catch(OperationCanceledException){}}
        }
    }
    private static async Task<bool> Available(string name)
    {
        using var token=new CancellationTokenSource(200);
        try{using var stream=await Transport.Connect(name,token.Token);return true;}catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException){return false;}
    }
    private static void Start(string executable,string[] arguments,string workingDirectory,string state,string name,string path)
    {
        var info=new ProcessStartInfo(executable){WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        info.Environment["REPLAY_PANEL_CONFIG"]=path;info.Environment["REPLAY_PANEL_LOG_DIR"]=state;
        Process.Start(info)?.Dispose();
    }
}

public static class NativeConsole
{
    public static string ConfigPath {get;set;}="";
    public static bool HasDesktop=>!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public static void Server(PanelConfig config)
    {
        if(config.Backend=="docker"||Environment.GetEnvironmentVariable("REPLAY_PANEL_NO_TERMINAL")=="1")return;
        var path=ConfigPath;
        if(string.IsNullOrEmpty(path))return;
        var lockFile=Path.Combine(Path.GetDirectoryName(path)!,"server-console.lock");
        try{using var probe=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){return;}
        try{Open(Environment.ProcessPath!,["--server-console",path],"replay-server-"+config.InstanceId[..8]);}catch(Exception e){Console.Error.WriteLine("Native server terminal: "+e.Message);}
    }
    public static async Task CloseServer(PanelConfig config)
    {
        using var token=new CancellationTokenSource(200);
        try{using var stream=await Transport.Connect(config.PipeName+".server-console",token.Token);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};await writer.WriteLineAsync("close");}
        catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException){}
    }
    public static void Open(string executable,string[] arguments,string session)
    {
        ProcessStartInfo info;
        if(OperatingSystem.IsWindows())info=new(executable){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal};
        else if(HasDesktop){info=new("x-terminal-emulator"){UseShellExecute=false};info.ArgumentList.Add("-e");info.ArgumentList.Add(executable);}
        else{info=new("tmux"){UseShellExecute=false};foreach(var value in new[]{"new-session","-d","-s",session,executable})info.ArgumentList.Add(value);}
        foreach(var value in arguments)info.ArgumentList.Add(value);
        Process.Start(info)?.Dispose();
    }
    public static void StopWorker(string path,bool all)
    {
        var executable=Environment.ProcessPath!;
        var info=OperatingSystem.IsWindows()?new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true}:new ProcessStartInfo("setsid"){UseShellExecute=false};
        if(!OperatingSystem.IsWindows()){info.ArgumentList.Add("--fork");info.ArgumentList.Add(executable);}
        info.ArgumentList.Add(all?"--stop-stack-detached":"--stop-server-detached");info.ArgumentList.Add(path);
        Process.Start(info)?.Dispose();
    }
}
