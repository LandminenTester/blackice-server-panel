using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
var configPath=Path.GetFullPath(args.FirstOrDefault()??".local/panel.json");
var config=PanelConfig.Load(configPath);
using var single=new FileStream(Path.Combine(Path.GetDirectoryName(configPath)!,"supervisor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var setup=new SetupService(config,configPath);setup.PrintKey();
IServerBackend? backend=null;BundleManager? bundles=null;PipeClient? adapter=null;
void Activate(){
    backend=config.Backend=="windows"?new WindowsServerBackend(config):new PosixServerBackend(config);
    if(backend is WindowsServerBackend windows)windows.BeginMonitoring();
    adapter=new PipeClient(backend);bundles=new BundleManager(adapter,config);
}
if(config.Configured)Activate();
using var shutdown=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
using var terminate=OperatingSystem.IsWindows()?null:System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,c=>{c.Cancel=true;shutdown.Cancel();});
Console.WriteLine("Replay supervisor ready.");
try {await Transport.Listen(config.PipeName,async(stream,ct)=>{
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(config.StopTimeoutSeconds+15));
    using var reader=new StreamReader(stream,leaveOpen:true);using var writer=new StreamWriter(stream,leaveOpen:true){AutoFlush=true};
    Reply reply;
    try {
        var line=await reader.ReadLineAsync(timeout.Token);if(line is null||line.Length>16384)throw new InvalidOperationException("request.invalid");
        var request=JsonSerializer.Deserialize<Request>(line,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        string Enc(object value)=>JsonSerializer.Serialize(value,PanelConfig.Json);
        switch(request.Action) {
            case "bootstrap":reply=new(true,Lines:[Enc(new {configured=config.Configured,dashboardName=config.DashboardName,backend=config.Backend,locale=config.Locale})]);break;
            case "setup-folders": {var input=JsonSerializer.Deserialize<Dictionary<string,string>>(request.Command!)!;setup.Authorize(input["key"]);reply=new(true,Lines:[Enc(setup.Folders(input["path"]))]);break;}
            case "setup-validate": {var input=JsonSerializer.Deserialize<InstallInput>(request.Command!,PanelConfig.Json)!;setup.Authorize(input.Key);SetupService.Validate(input.ServerRoot,input.Backend);reply=new(true);break;}
            case "setup-install":reply=new(true,Lines:[Enc(setup.Start(JsonSerializer.Deserialize<InstallInput>(request.Command!,PanelConfig.Json)!))]);break;
            case "setup-job":setup.Authorize(request.Command!);reply=new(true,Lines:[Enc(setup.Status()??new InstallJob("","idle","idle",0))]);break;
            case "setup-cancel":setup.Authorize(request.Command!);setup.Cancel();reply=new(true);break;
            case "setup-finish":await setup.Finish(JsonSerializer.Deserialize<SetupInput>(request.Command!,PanelConfig.Json)!);Activate();reply=new(true);break;
            case "authenticate": {var input=JsonSerializer.Deserialize<string[]>(request.Command!)!;reply=new(input[0]==config.Username && config.VerifyPassword(input[1]));break;}
            case "settings":
                if(request.ShutdownMode is not ("keep-running" or "stop-server"))throw new InvalidOperationException("settings.invalid");
                config.ShutdownMode=request.ShutdownMode;PanelConfig.Save(configPath,config);reply=new(true,Status:backend?.Status());break;
            case "webhost-stopping":if(config.ShutdownMode=="stop-server"&&backend?.Status().State=="running")await backend.StopAsync();reply=new(true);break;
            case "shutdown":if(backend?.Status().State=="running")throw new InvalidOperationException("server.running");reply=new(true);shutdown.Cancel();break;
            case "bundles":if(bundles is null)throw new InvalidOperationException("setup.required");reply=new(true,Lines:[Enc(await bundles.List(timeout.Token))]);break;
            case "bundle-action":if(bundles is null)throw new InvalidOperationException("setup.required");var action=JsonSerializer.Deserialize<string[]>(request.Command!)!;reply=new(true,Lines:[Enc(await bundles.Action(action[0],action[1],timeout.Token))]);break;
            default:if(adapter is null){reply=new(true,Status:new("unconfigured",null,"",config.ShutdownMode));break;}reply=await adapter.Call(request,timeout.Token);break;
        }
    }catch(Exception e){reply=new(false,Error:ErrorCodes.From(e));}
    await writer.WriteLineAsync(JsonSerializer.Serialize(reply,PanelConfig.Json));
},shutdown.Token);}catch(OperationCanceledException){}
if(backend is not null)await backend.DisposeAsync();
