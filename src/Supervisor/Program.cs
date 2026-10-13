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
        var state=NativeConsole.ConfigPath;if(!args.Contains("--update-restart")&&state.Length>0&&File.Exists(state))await ControlConsole.StopAll(state);
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
using var bundleTools=new ReplayPanel.Supervisor.BundleTools.BundleToolsService(config,configPath,()=>bundles);
await using var serverUpdates=new ServerUpdates(config,configPath,()=>backend);
void Activate(){
    backend=new BridgeBackend(config,configPath);
    adapter=new PipeClient(backend);bundles=new BundleManager(adapter,config);
}
if(config.Configured){Activate();if(config.AutoStartServer&&backend!.Status().State=="stopped"){try{await backend.StartAsync();}catch(Exception e){Console.Error.WriteLine(e);}}}
using var shutdown=new CancellationTokenSource();
using var locationGate=new SemaphoreSlim(1);
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
using var terminate=OperatingSystem.IsWindows()?null:System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,c=>{c.Cancel=true;shutdown.Cancel();});
Console.WriteLine("BlackICE supervisor ready.");
try {await Transport.Listen(config.PipeName,async(stream,ct)=>{
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(config.StopTimeoutSeconds+15));
    using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
    Reply reply;string? requestAction=null;bool locationLease=false;
    try {
        var line=await reader.ReadLineAsync(timeout.Token);if(line is null||line.Length>16384)throw new InvalidOperationException("request.invalid");
        var request=JsonSerializer.Deserialize<Request>(line,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        requestAction=request.Action;
        if(request.Action is not ("authenticate" or "account-session" or "bootstrap" or "users" or "setup-banner" or "update-status")){await locationGate.WaitAsync(timeout.Token);locationLease=true;}
        if(ServerLocation.Issue(config) is {} issue && request.Action is "start" or "restart" or "command" or "bundle-action" or "server-config-save" or "server-update-download" or "server-update-apply")throw new InvalidOperationException(issue);
        if(serverUpdates.Applying&&request.Action is "start" or "stop" or "restart" or "force-stop" or "command" or "bundle-action" or "bridge-settings" or "update-apply" or "server-path" or "shutdown")throw new InvalidOperationException("serverUpdate.busy");
        string Enc(object value)=>JsonSerializer.Serialize(value,PanelConfig.Json);
        switch(request.Action) {
            case "bootstrap":reply=new(true,Lines:[Enc(new {configured=config.Configured,dashboardName=config.DashboardName,backend=config.Backend,locale=config.Locale,version=PanelUpdates.RunningVersion,defaultServerRoot=config.Configured?null:setup.DefaultRoot()})]);break;
            case "setup-banner":reply=new(true,Lines:[Enc(setup.Banner(request.Command=="local"))]);break;
            case "setup-pick": {var input=JsonSerializer.Deserialize<Dictionary<string,string>>(request.Command!)!;setup.Authorize(input["key"]);setup.PickStart(input.GetValueOrDefault("path")??"");reply=new(true);break;}
            case "setup-pick-status":setup.Authorize(request.Command!);reply=new(true,Lines:[Enc(setup.PickStatus())]);break;
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
            case "server-update-status":reply=new(true,Lines:[Enc(serverUpdates.Status())]);break;
            case "server-update-check":reply=new(true,Lines:[Enc(await serverUpdates.Check(timeout.Token))]);break;
            case "server-update-download":reply=new(true,Lines:[Enc(await serverUpdates.Download(request.Command,timeout.Token))]);break;
            case "server-update-cancel":await serverUpdates.Cancel();reply=new(true,Lines:[Enc(serverUpdates.Status())]);break;
            case "server-update-apply":reply=new(true,Lines:[Enc(await serverUpdates.Apply())]);break;
            case "bridge-status":reply=new(true,Lines:[Enc((backend as BridgeBackend)?.Diagnosis()??throw new InvalidOperationException("setup.required"))]);break;
            case "bridge-settings": {
                var value=JsonSerializer.Deserialize<BridgeSettings>(request.Command!,PanelConfig.Json)??throw new InvalidOperationException("bridge.settings");BridgeBackend.ValidateSettings(value);
                if(value.Port==config.GamePort||value.Port==config.VoicePort||value.Port==new Uri(config.Url).Port)throw new InvalidOperationException("bridge.settings");
                var previous=config.Bridge;config.Bridge=value;try{PanelConfig.Save(configPath,config);}catch{config.Bridge=previous;throw;}reply=new(true);break;
            }
            case "diagnostics": {
                var query=JsonSerializer.Deserialize<DiagnosticQuery>(request.Command!,PanelConfig.Json)!;
                reply=new(true,Lines:[Enc((backend as BridgeBackend)?.Read(query.Cursor,query.Source,query.Module,query.Search,query.Limit,query.History)??throw new InvalidOperationException("setup.required"))]);break;
            }
            case "update-check":reply=new(true,Lines:[Enc(await updates.Check(timeout.Token))]);break;
            case "update-download":reply=new(true,Lines:[Enc(await updates.Download(timeout.Token))]);break;
            case "update-cancel":await updates.Cancel();reply=new(true,Lines:[Enc(updates.Status())]);break;
            case "update-apply":reply=new(true,Lines:[Enc(await updates.Apply())]);break;
            case "server-path": {
                if(!config.Configured||backend is null)throw new InvalidOperationException("setup.required");
                if(serverUpdates.Status().Job?.State is "downloading" or "ready" or "applying")throw new InvalidOperationException("serverUpdate.busy");
                var next=ServerLocation.Validate(config,request.Command??"",backend.Status());
                var previous=config.ServerRoot;await backend.DisposeAsync();
                var persisted=false;config.ServerRoot=next;
                try{PanelConfig.Save(configPath,config);persisted=true;Activate();}
                catch{config.ServerRoot=previous;if(persisted)PanelConfig.Save(configPath,config);Activate();throw;}
                reply=new(true,Status:backend!.Status());break;
            }
            case "server-config":if(!config.Configured)throw new InvalidOperationException("setup.required");reply=new(true,Lines:[Enc(ServerConfig.Read(config.ServerRoot,backend?.Status().State=="running"))]);break;
            case "server-config-save":{if(!config.Configured)throw new InvalidOperationException("setup.required");ServerConfig.Write(config.ServerRoot,JsonSerializer.Deserialize<ServerConfigValues>(request.Command!,PanelConfig.Json)!);reply=new(true,Lines:[Enc(ServerConfig.Read(config.ServerRoot,backend?.Status().State=="running"))]);break;}
            case "settings": { lock(config) {
                if(request.Locale is not null && request.Locale is not ("de" or "en"))throw new InvalidOperationException("settings.invalid");
                if(request.UpdateChannel is not null && request.UpdateChannel is not ("stable" or "experimental"))throw new InvalidOperationException("settings.invalid");
                if(request.ShutdownMode is not null && request.ShutdownMode is not ("keep-running" or "stop-server"))throw new InvalidOperationException("settings.invalid");
                if(request.ShutdownMode is not null)config.ShutdownMode=request.ShutdownMode;if(request.AutoStartServer is not null)config.AutoStartServer=request.AutoStartServer.Value;if(request.Locale is not null)config.Locale=request.Locale;if(request.AutoUpdateChecks is not null)config.AutoUpdateChecks=request.AutoUpdateChecks.Value;if(request.UpdateChannel is not null)config.UpdateChannel=request.UpdateChannel;PanelConfig.Save(configPath,config);reply=new(true,Status:backend?.Status()); } break; }
            case "webhost-stopping":if(config.ShutdownMode=="stop-server"&&backend?.Status().State=="running")await backend.StopAsync();reply=new(true);break;
            case "shutdown":if(backend?.Status().State=="running")throw new InvalidOperationException("server.running");await setup.StopInstallation();reply=new(true);shutdown.Cancel();break;
            case "bundles":if(bundles is null)throw new InvalidOperationException("setup.required");reply=new(true,Lines:[Enc(await bundles.List(timeout.Token))]);break;
            case "bundle-tools-status":reply=new(true,Lines:[Enc(bundleTools.Status())]);break;
            case "bundle-tools-settings":reply=new(true,Lines:[Enc(bundleTools.ApplySettings(request.Command!))]);break;
            case "bundle-tools-upload-inspect":reply=new(true,Lines:[Enc(bundleTools.UploadInspect(request.Command!))]);break;
            case "bundle-tools-upload-install":reply=new(true,Lines:[Enc(bundleTools.UploadInstall(request.Command!))]);break;
            case "bundle-tools-tool-install":reply=new(true,Lines:[Enc(bundleTools.ToolInstall(request.Command!))]);break;
            case "bundle-tools-tool-remove":reply=new(true,Lines:[Enc(bundleTools.ToolRemove(request.Command!))]);break;
            case "bundle-tools-tool-latest":reply=new(true,Lines:[Enc(await bundleTools.ToolLatest(timeout.Token))]);break;
            case "bundle-tools-projects":reply=new(true,Lines:[Enc(await bundleTools.Projects.ListAsync(timeout.Token))]);break;
            case "bundle-tools-project-import":reply=new(true,Lines:[Enc(bundleTools.Projects.Import(request.Command!))]);break;
            case "bundle-tools-project-check":reply=new(true,Lines:[Enc(await bundleTools.Projects.Check(request.Command!,timeout.Token))]);break;
            case "bundle-tools-project-pull":reply=new(true,Lines:[Enc(bundleTools.Projects.Pull(request.Command!))]);break;
            case "bundle-tools-project-refs":reply=new(true,Lines:[Enc(await bundleTools.Projects.Refs(request.Command!,timeout.Token))]);break;
            case "bundle-tools-project-checkout":reply=new(true,Lines:[Enc(bundleTools.Projects.Checkout(request.Command!))]);break;
            case "bundle-tools-account-save":reply=new(true,Lines:[Enc(bundleTools.AccountSave(request.Command!))]);break;
            case "bundle-tools-account-delete":reply=new(true,Lines:[Enc(bundleTools.AccountDelete(request.Command!))]);break;
            case "bundle-tools-account-test":reply=new(true,Lines:[Enc(await bundleTools.AccountTest(request.Command!,timeout.Token))]);break;
            case "bundle-tools-project-create":reply=new(true,Lines:[Enc(bundleTools.Projects.Create(request.Command!))]);break;
            case "bundle-tools-project-build":reply=new(true,Lines:[Enc(bundleTools.Projects.Build(request.Command!))]);break;
            case "bundle-tools-project-rollback":reply=new(true,Lines:[Enc(bundleTools.Projects.Rollback(request.Command!))]);break;
            case "bundle-tools-project-remove":reply=new(true,Lines:[Enc(bundleTools.Projects.RemoveSource(request.Command!))]);break;
            case "bundle-tools-packages-info":reply=new(true,Lines:[Enc(await bundleTools.Projects.PackagesInfo(request.Command!,timeout.Token))]);break;
            case "bundle-tools-packages-set":reply=new(true,Lines:[Enc(await bundleTools.Projects.PackagesSet(request.Command!,timeout.Token))]);break;
            case "bundle-tools-job":reply=new(true,Lines:[Enc(bundleTools.JobInfo(request.Command!))]);break;
            case "bundle-action":if(bundles is null)throw new InvalidOperationException("setup.required");var action=JsonSerializer.Deserialize<string[]>(request.Command!)!;reply=new(true,Lines:[Enc(await bundles.Action(action[0],action[1],timeout.Token))]);break;
            default:if(adapter is null){reply=new(true,Status:new("unconfigured",null,"",config.ShutdownMode));break;}reply=await adapter.Call(request,timeout.Token);break;
        }
    }catch(Exception e){Console.Error.WriteLine($"{requestAction}: {e.GetType().Name}: {ErrorCodes.From(e)}");if(Environment.GetEnvironmentVariable("REPLAY_PANEL_TRACE_ERRORS")=="1")Console.Error.WriteLine(e);reply=new(false,Error:ErrorCodes.From(e));}
    if(reply.Ok && requestAction is "stop" or "force-stop")await NativeConsole.CloseServer(config);
    if(reply.Status is {} status)reply=reply with{Status=status with{AutoStartServer=config.AutoStartServer,AutoUpdateChecks=config.AutoUpdateChecks,UpdateChannel=config.UpdateChannel,PathIssue=ServerLocation.Issue(config)}};
    if(locationLease)locationGate.Release();
    await writer.WriteLineAsync(JsonSerializer.Serialize(reply,PanelConfig.Json));
},shutdown.Token);}catch(OperationCanceledException){}
await serverUpdates.DisposeAsync();if(backend is not null)await backend.DisposeAsync();
