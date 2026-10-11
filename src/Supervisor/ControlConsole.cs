using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

// The visible controller never owns the game process. Closing it delegates graceful stack shutdown
// to an independent worker, because Windows gives console-close handlers only a short deadline.
public static class ControlConsole
{
    private delegate bool ConsoleHandler(uint signal);
    private static ConsoleHandler? closeHandler;
    private static int delegated;
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleHandler? handler,bool add);
    public static async Task<Reply> Call(PanelConfig config,Request request,Action<int>? peer=null)
    {
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(config.StopTimeoutSeconds+15));
        using var stream=await Transport.Connect(config.PipeName,ct.Token);
        if(Transport.PeerId(stream) is {} id)peer?.Invoke(id);
        using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
        await writer.WriteLineAsync(JsonSerializer.Serialize(request,PanelConfig.Json));
        var line=await reader.ReadLineAsync(ct.Token);
        return JsonSerializer.Deserialize<Reply>(line??"",PanelConfig.Json)??new(false,"connection.invalid");
    }
    private static string Label(PanelConfig config,string en,string de)=>config.Locale=="de"?de:en;
    public static async Task<int> StopAll(string path,bool force=false)
    {
        if(!File.Exists(path)){Console.Error.WriteLine("Configuration not found: "+path);return 1;}
        var config=PanelConfig.Load(path);
        Console.WriteLine(Label(config,"Stopping BlackICE Server Webpanel…","BlackICE Server Webpanel wird beendet…"));
        Process? supervisor=null,web=null;
        try {
            Reply status;
            try {status=await Call(config,new("status"),id=>supervisor=Track(id));}
            catch(Exception e) when(e is IOException or TimeoutException or System.Net.Sockets.SocketException or OperationCanceledException){status=new(false,"connection.unavailable");}
            if(!status.Ok && OperatingSystem.IsWindows() && config.ServerRoot.Length>0) {
                var exe=Path.Combine(config.ServerRoot,"REServer.exe");
                foreach(var process in Process.GetProcessesByName("REServer")) {
                    using(process) {
                        try {if(string.Equals(process.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase)){Console.Error.WriteLine(Label(config,"Supervisor unavailable; the running server cannot be stopped safely.","Supervisor fehlt; der laufende Server kann nicht sicher gestoppt werden."));return 1;}}
                        catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                    }
                }
            }
            if(status.Status?.State=="external"){Console.Error.WriteLine(Label(config,"An external server must be stopped in its original console.","Externen Server bitte in seiner bisherigen Konsole stoppen."));return 1;}
            if(status.Ok && status.Status?.State is not ("running" or "stopped" or "unconfigured" or "external")) {Console.Error.WriteLine(Label(config,"Server state could not be confirmed.","Serverzustand konnte nicht bestätigt werden."));return 1;}
            if(status.Status?.State=="running") {
                Console.WriteLine(Label(config,"Stopping game server…","Spielserver wird gestoppt…"));
                var stopped=await Call(config,new(force?"force-stop":"stop"));
                if(!stopped.Ok){Console.Error.WriteLine(stopped.Error);return 1;}
            }
            try {
                using var token=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var stream=await Transport.Connect(config.PipeName+".web",token.Token);
                if(Transport.PeerId(stream) is {} peerId)web=Track(peerId);
                using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
                await writer.WriteLineAsync("shutdown");
                if(await reader.ReadLineAsync(token.Token)!="ok")throw new IOException("connection.invalid");
            }catch(Exception e) when(e is IOException or TimeoutException or System.Net.Sockets.SocketException or OperationCanceledException){ /* Already stopped is fine. */ }
            if(status.Ok){var reply=await Call(config,new("shutdown"));if(!reply.Ok){Console.Error.WriteLine(reply.Error);return 1;}}
            // Wait for both listeners to disappear, not only their acknowledgement.
            for(var i=0;i<100;i++){
                bool alive=false;
                foreach(var name in new[]{config.PipeName,config.PipeName+".web"}){
                    using var timeout=new CancellationTokenSource(150);
                    try{using var connection=await Transport.Connect(name,timeout.Token);alive=true;}
                    catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException){}
                }
                if(!alive){
                    foreach(var process in new[]{supervisor,web})if(process is not null&&!process.HasExited)await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(config.StopTimeoutSeconds+15));
                    foreach(var name in new[]{config.PipeName+".controller",config.PipeName+".server-console",config.PipeName+".launcher"}){try{using var token=new CancellationTokenSource(1000);using var endpoint=await Transport.Connect(name,token.Token);using var writer=new StreamWriter(endpoint,leaveOpen:true){AutoFlush=true};
                        using var client=Transport.PeerId(endpoint) is {} pid && pid!=Environment.ProcessId?Track(pid):null;
                        await writer.WriteLineAsync("close");
                        if(client is not null&&!client.HasExited)await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException){}}
                    Console.WriteLine(Label(config,"Game server, Webhost and Supervisor are stopped.","Spielserver, Webhost und Supervisor sind beendet."));return 0;}
                await Task.Delay(100);
            }
            Console.Error.WriteLine(Label(config,"Shutdown is still in progress. Check the process list and logs.","Beenden dauert noch an. Prozessliste und Logs prüfen."));return 1;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}finally{supervisor?.Dispose();web?.Dispose();}
    }
    private static Process? Track(int pid){try{var process=Process.GetProcessById(pid);_=process.Handle;return process;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}}
    public static async Task Run(string path,CancellationToken stopping=default)
    {
        var config=PanelConfig.Load(path);
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(stopping);
        using FileStream single=OpenLock(path);
        if(OperatingSystem.IsWindows()&&!Console.IsInputRedirected){
            Console.Title=config.DashboardName+" — "+Label(config,"Control","Steuerung");
            closeHandler=signal=>{
                if(signal is 0 or 1){cancel.Cancel();return true;}
                if(signal is 2 or 5 or 6){
                    if(Interlocked.Exchange(ref delegated,1)==0){
                        try{NativeConsole.StopWorker(path,true);}catch{}
                    }
                    return true;
                }
                return false;
            };
            if(!SetConsoleCtrlHandler(closeHandler,true))throw new System.ComponentModel.Win32Exception();
        }
        using var hup=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGHUP,c=>{c.Cancel=true;if(Interlocked.Exchange(ref delegated,1)==0)NativeConsole.StopWorker(path,true);cancel.Cancel();});
        using var term=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,c=>{c.Cancel=true;if(Interlocked.Exchange(ref delegated,1)==0)NativeConsole.StopWorker(path,true);cancel.Cancel();});
        ConsoleCancelEventHandler interrupt=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        Console.CancelKeyPress+=interrupt;
        bool keep=false,completed=false;
        var controlListener=Transport.Listen(config.PipeName+".controller",async(stream,ct)=>{
            using var reader=new StreamReader(stream,leaveOpen:true);
            if(await reader.ReadLineAsync(ct)=="close"){keep=true;cancel.Cancel();}
        },cancel.Token);
        Console.WriteLine(config.DashboardName+" — "+config.Url);
        Console.WriteLine(Label(config,"Closing this window or Ctrl+C stops the entire stack. A browser tab does not.","Fenster schließen oder Strg+C beendet den gesamten Stack. Ein Browser-Tab tut das nicht."));
        try{
            while(!cancel.IsCancellationRequested){
                config=PanelConfig.Load(path);
                try{var state=(await Call(config,new("status"))).Status;Console.WriteLine(Label(config,"Server: ","Server: ")+(config.Locale=="de"?(state?.State switch{"running"=>"gestartet","stopped"=>"gestoppt","external"=>"extern gestartet","unconfigured"=>"Einrichtung ausstehend",_=>"unbekannt"}):state?.State??"unknown")+" | PID: "+state?.Pid);}
                catch{Console.WriteLine(Label(config,"Supervisor disconnected.","Supervisor nicht verbunden."));}
                Console.WriteLine(Label(config,
                    "[R] Refresh  [C] Console  [Q] Stop everything  [W] Stop Webhost only  [K] Close window, keep services",
                    "[R] Aktualisieren  [C] Konsole  [Q] Alles beenden  [W] Nur Webhost beenden  [K] Fenster zu, Dienste weiter"));
                Console.Write("> ");
                var input=Task.Run(()=>Console.ReadLine());
                var winner=await Task.WhenAny(input,Task.Delay(Timeout.Infinite,cancel.Token));
                if(winner!=input)break;
                var command=(await input)?.Trim().ToUpperInvariant();
                if(command is null)break;
                if(command=="K"){keep=true;break;}
                if(command=="Q"){if(await StopAll(path)==0){completed=true;break;}continue;}
                if(command=="C"){
                    try{
                        if(!OperatingSystem.IsWindows()&&!NativeConsole.HasDesktop&&config.Backend!="docker"&&!Console.IsInputRedirected){
                            var info=new ProcessStartInfo("tmux"){UseShellExecute=false};info.ArgumentList.Add("attach-session");info.ArgumentList.Add("-t");info.ArgumentList.Add("replay-server-"+config.InstanceId[..8]);
                            using var terminal=Process.Start(info)!;await terminal.WaitForExitAsync();
                        }else foreach(var line in (await Call(config,new("console"))).Lines??[])Console.WriteLine(line);
                    }catch(Exception e){Console.WriteLine(e.Message);}
                }
                if(command=="W"){
                    try{using var token=new CancellationTokenSource(5000);using var stream=await Transport.Connect(config.PipeName+".web",token.Token);using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};await writer.WriteLineAsync("shutdown");await reader.ReadLineAsync(token.Token);Console.WriteLine(Label(config,"Webhost stop requested; server policy applies.","Webhost-Stop angefordert; der eingestellte Servermodus gilt."));}catch(Exception e){Console.WriteLine(e.Message);}
                }
            }
        }finally{
            cancel.Cancel();try{await controlListener;}catch(OperationCanceledException){}
            Console.CancelKeyPress-=interrupt;
            if(OperatingSystem.IsWindows()&&closeHandler is not null)SetConsoleCtrlHandler(closeHandler,false);
            if(!keep&&!completed&&!stopping.IsCancellationRequested&&Interlocked.CompareExchange(ref delegated,0,0)==0)await StopAll(path);
        }
    }
    private static FileStream OpenLock(string path)=>new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"controller.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
}
