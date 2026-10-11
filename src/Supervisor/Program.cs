using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
if(args.FirstOrDefault()=="--apply-panel-update"){await PanelUpdateWorker.Run(args[1]);return;}
if(args.FirstOrDefault() is "--controller" or "--stop-stack" or "--force-stop-stack" or "--stop-stack-detached" or "--server-console" or "--stop-server-detached") {
    var mode=args[0];var path=Path.GetFullPath(args.Skip(1).FirstOrDefault()??".local/panel.json");
    if(mode is "--stop-stack-detached" or "--stop-server-detached") {
        var log=new StreamWriter(new FileStream(Path.Combine(Path.GetDirectoryName(path)!,mode=="--stop-server-detached"?"server-console-stop.log":"controller-stop.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite)){AutoFlush=true};
        Console.SetOut(log);Console.SetError(log);
    }
    if(mode=="--server-console"){try{await ServerConsole.Run(path);}catch(IOException){}return;}
    if(mode=="--stop-server-detached"){Environment.ExitCode=await ServerConsole.Stop(path);return;}
    if(mode=="--controller") {
        try {await ControlConsole.Run(path);}catch(IOException){Console.WriteLine("A control window is already open.");}
    }else Environment.ExitCode=await ControlConsole.StopAll(path,mode=="--force-stop-stack");
    return;
}
if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"Start",StringComparison.OrdinalIgnoreCase)){
    try{await PanelLauncher.Run(args);}catch(OperationCanceledException){}catch(Exception e){
        Console.Error.WriteLine(e.Message);
        var state=NativeConsole.ConfigPath;if(state.Length>0&&File.Exists(state))await ControlConsole.StopAll(state);
        Console.Error.WriteLine("Read .local/*.err.log. Press Enter to close.");
        if(!Console.IsInputRedirected&&!args.Contains("--update-restart"))Console.ReadLine();
        Environment.ExitCode=1;
    }
    return;
}
if(Environment.GetEnvironmentVariable("REPLAY_PANEL_LOG_DIR") is {} logDirectory){
    Directory.CreateDirectory(logDirectory);
    Console.SetOut(new StreamWriter(new FileStream(Path.Combine(logDirectory,"supervisor.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){AutoFlush=true});
    Console.SetError(new StreamWriter(new FileStream(Path.Combine(logDirectory,"supervisor.err.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){AutoFlush=true});
}
var configPath=Path.GetFullPath(args.FirstOrDefault()??".local/panel.json");
var config=PanelConfig.Load(configPath);
NativeConsole.ConfigPath=configPath;
using var updates=new PanelUpdates(configPath);
using var single=new FileStream(Path.Combine(Path.GetDirectoryName(configPath)!,"supervisor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var setup=new SetupService(config,configPath);setup.PrintKey();
IServerBackend? backend=null;BundleManager? bundles=null;PipeClient? adapter=null;
void Activate(){
    backend=config.Backend=="windows"?new WindowsServerBackend(config):new PosixServerBackend(config);
    if(backend is WindowsServerBackend windows)windows.BeginMonitoring();
    adapter=new PipeClient(backend);bundles=new BundleManager(adapter,config);
}
if(config.Configured){Activate();if(config.AutoStartServer&&backend!.Status().State=="stopped"){try{await backend.StartAsync();}catch(Exception e){Console.Error.WriteLine(e);}}}
using var shutdown=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
using var terminate=OperatingSystem.IsWindows()?null:System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,c=>{c.Cancel=true;shutdown.Cancel();});
Console.WriteLine("Replay supervisor ready.");
try {await Transport.Listen(config.PipeName,async(stream,ct)=>{
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(config.StopTimeoutSeconds+15));
    using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
    Reply reply;string? requestAction=null;
    try {
        var line=await reader.ReadLineAsync(timeout.Token);if(line is null||line.Length>16384)throw new InvalidOperationException("request.invalid");
        var request=JsonSerializer.Deserialize<Request>(line,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        requestAction=request.Action;
        string Enc(object value)=>JsonSerializer.Serialize(value,PanelConfig.Json);
        switch(request.Action) {
            case "bootstrap":reply=new(true,Lines:[Enc(new {configured=config.Configured,dashboardName=config.DashboardName,backend=config.Backend,locale=config.Locale,version=PanelUpdates.RunningVersion})]);break;
            case "setup-folders": {var input=JsonSerializer.Deserialize<Dictionary<string,string>>(request.Command!)!;setup.Authorize(input["key"]);reply=new(true,Lines:[Enc(setup.Folders(input["path"]))]);break;}
            case "setup-validate": {var input=JsonSerializer.Deserialize<InstallInput>(request.Command!,PanelConfig.Json)!;setup.Authorize(input.Key);SetupService.Validate(input.ServerRoot,input.Backend);reply=new(true);break;}
            case "setup-install":reply=new(true,Lines:[Enc(setup.Start(JsonSerializer.Deserialize<InstallInput>(request.Command!,PanelConfig.Json)!))]);break;
            case "setup-job":setup.Authorize(request.Command!);reply=new(true,Lines:[Enc(setup.Status()??new InstallJob("","idle","idle",0))]);break;
            case "setup-cancel":setup.Authorize(request.Command!);setup.Cancel();reply=new(true);break;
            case "setup-finish":await setup.Finish(JsonSerializer.Deserialize<SetupInput>(request.Command!,PanelConfig.Json)!);Activate();if(config.AutoStartServer){try{await backend!.StartAsync();}catch(Exception e){Console.Error.WriteLine(e);}}reply=new(true);break;
            case "authenticate": {var input=JsonSerializer.Deserialize<string[]>(request.Command!)!;var user=Accounts.Authenticate(config,input[0],input[1]);reply=user is null?new(false):new(true,Lines:[Enc(user)]);break;}
            case "account-session": {var input=JsonSerializer.Deserialize<string[]>(request.Command!)!;var user=Accounts.Session(config,input[0],input[1]);reply=user is null?new(false):new(true,Lines:[Enc(user)]);break;}
            case "users":reply=new(true,Lines:[Enc(Accounts.List(config))]);break;
            case "user-save":Accounts.Save(config,configPath,JsonSerializer.Deserialize<UserChange>(request.Command!,PanelConfig.Json)!);reply=new(true);break;
            case "user-delete":Accounts.Delete(config,configPath,request.Command!);reply=new(true);break;
            case "update-status":reply=new(true,Lines:[Enc(updates.Status())]);break;
            case "update-check":reply=new(true,Lines:[Enc(await updates.Check(timeout.Token))]);break;
            case "update-download":reply=new(true,Lines:[Enc(await updates.Download(timeout.Token))]);break;
            case "update-cancel":await updates.Cancel();reply=new(true,Lines:[Enc(updates.Status())]);break;
            case "update-apply":reply=new(true,Lines:[Enc(await updates.Apply())]);break;
            case "settings": { lock(config) {
                if(request.Locale is not null && request.Locale is not ("de" or "en"))throw new InvalidOperationException("settings.invalid");
                if(request.ShutdownMode is not null && request.ShutdownMode is not ("keep-running" or "stop-server"))throw new InvalidOperationException("settings.invalid");
                if(request.ShutdownMode is not null)config.ShutdownMode=request.ShutdownMode;if(request.AutoStartServer is not null)config.AutoStartServer=request.AutoStartServer.Value;if(request.Locale is not null)config.Locale=request.Locale;PanelConfig.Save(configPath,config);reply=new(true,Status:backend?.Status()); } break; }
            case "webhost-stopping":if(config.ShutdownMode=="stop-server"&&backend?.Status().State=="running")await backend.StopAsync();reply=new(true);break;
            case "shutdown":if(backend?.Status().State=="running")throw new InvalidOperationException("server.running");await setup.StopInstallation();reply=new(true);shutdown.Cancel();break;
            case "bundles":if(bundles is null)throw new InvalidOperationException("setup.required");reply=new(true,Lines:[Enc(await bundles.List(timeout.Token))]);break;
            case "bundle-action":if(bundles is null)throw new InvalidOperationException("setup.required");var action=JsonSerializer.Deserialize<string[]>(request.Command!)!;reply=new(true,Lines:[Enc(await bundles.Action(action[0],action[1],timeout.Token))]);break;
            default:if(adapter is null){reply=new(true,Status:new("unconfigured",null,"",config.ShutdownMode));break;}reply=await adapter.Call(request,timeout.Token);break;
        }
    }catch(Exception e){reply=new(false,Error:ErrorCodes.From(e));}
    if(reply.Ok && requestAction is "stop" or "force-stop")await NativeConsole.CloseServer(config);
    if(reply.Status is {} status)reply=reply with{Status=status with{AutoStartServer=config.AutoStartServer}};
    await writer.WriteLineAsync(JsonSerializer.Serialize(reply,PanelConfig.Json));
},shutdown.Token);}catch(OperationCanceledException){}
if(backend is not null)await backend.DisposeAsync();
