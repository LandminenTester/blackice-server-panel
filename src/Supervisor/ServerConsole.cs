using System.Runtime.InteropServices;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public static class ServerConsole
{
    private delegate bool Handler(uint signal);
    private static Handler? handler;
    public static async Task<int> Stop(string path)
    {
        try{var reply=await ControlConsole.Call(PanelConfig.Load(path),new("stop"));if(!reply.Ok)Console.Error.WriteLine(reply.Error);return reply.Ok?0:1;}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleCtrlHandler(Handler? callback,bool add);
    public static async Task Run(string path)
    {
        var config=PanelConfig.Load(path);
        using var single=new FileStream(Path.Combine(Path.GetDirectoryName(path)!,"server-console.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using var cancel=new CancellationTokenSource();
        bool closedByStack=false;int delegated=0;
        void Close(){if(Interlocked.Exchange(ref delegated,1)==0)NativeConsole.StopWorker(path,false);cancel.Cancel();}
        if(OperatingSystem.IsWindows()&&!Console.IsInputRedirected){
            Console.Title="Replay — Server Console";
            handler=signal=>{if(signal is 0 or 1 or 2 or 5 or 6){Close();return true;}return false;};
            SetConsoleCtrlHandler(handler,true);
        }
        using var hup=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGHUP,c=>{c.Cancel=true;Close();});
        using var term=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,c=>{c.Cancel=true;Close();});
        Console.WriteLine(config.Locale=="de"?"Replay-Serverkonsole. Fenster schließen stoppt den Server.":"Replay server console. Closing this terminal stops the server.");
        var control=Transport.Listen(config.PipeName+".server-console",async(stream,ct)=>{
            using var reader=new StreamReader(stream,leaveOpen:true);if(await reader.ReadLineAsync(ct)=="close"){closedByStack=true;cancel.Cancel();}
        },cancel.Token);
        var output=Task.Run(async()=>{
            string[] previous=[];
            while(!cancel.IsCancellationRequested){
                try{
                    var reply=await ControlConsole.Call(config,new("console"));
                    var current=reply.Lines??[];
                    int prefix=0;while(prefix<Math.Min(current.Length,previous.Length)&&current[prefix]==previous[prefix])prefix++;
                    if(prefix==0&&previous.Length>0&&current.Length>0){
                        var start=Array.IndexOf(previous,current[0]);if(start>=0){var count=Math.Min(previous.Length-start,current.Length);while(prefix<count&&previous[start+prefix]==current[prefix])prefix++;}
                    }
                    foreach(var line in current.Skip(prefix).Where(x=>!x.Contains("panel-query-")))Console.WriteLine(line);
                    previous=current;
                }catch{if(!cancel.IsCancellationRequested)Console.WriteLine(config.Locale=="de"?"Supervisor nicht erreichbar.":"Supervisor unavailable.");}
                try{await Task.Delay(500,cancel.Token);}catch(OperationCanceledException){}
            }
        });
        try{
            while(!cancel.IsCancellationRequested){
                var input=Task.Run(()=>Console.ReadLine());var winner=await Task.WhenAny(input,Task.Delay(Timeout.Infinite,cancel.Token));if(winner!=input)break;
                var command=await input;if(command is null)break;
                if(string.IsNullOrWhiteSpace(command))continue;
                try{var exit=command.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase);var reply=await ControlConsole.Call(config,exit?new("stop"):new("command",command));if(!reply.Ok)Console.WriteLine(reply.Error);if(exit&&reply.Ok)break;}catch(Exception e){Console.WriteLine(e.Message);}
            }
        }finally{
            cancel.Cancel();try{await control;}catch(OperationCanceledException){}await output;
            if(OperatingSystem.IsWindows()&&handler is not null)SetConsoleCtrlHandler(handler,false);
            if(!closedByStack&&delegated==0)await Stop(path);
        }
    }
}
