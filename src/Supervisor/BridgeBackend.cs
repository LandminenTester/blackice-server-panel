using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

public interface IBridgeLaunchPolicy {string[] Arguments(string endpoint,string token);}
public sealed class LanBridgeLaunchPolicy : IBridgeLaunchPolicy {
 public string[] Arguments(string endpoint,string token)=>["--mcp","--mcp-addr",endpoint,"--mcp-token",token];
}
public sealed record BridgeDiagnosis(bool Enabled,bool Active,bool Connected,string Transport,string Health,string Phase,string PhaseDetail,string? BuildCommit,long? Tick,DateTimeOffset? LastReply,DateTimeOffset? StartedAt,string? LastExit,int? LastExitCode,BridgeSettings Settings,bool RestartRequired);
public sealed class BridgeBackend : IServerBackend {
 readonly PanelConfig config;readonly string statePath;readonly string configPath;readonly string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 readonly IServerBackend inner;readonly DiagnosticStore store;readonly CancellationTokenSource stop=new();readonly Task collecting;readonly SemaphoreSlim lifecycle=new(1);
 BridgeHub? hub;BridgeSettings active;string endpoint="";string? error;DateTimeOffset? started;string? lastExit;int? observedPid;
 string epoch=Guid.NewGuid().ToString("N");string bridgeEpoch="";long sourceCursor;DateTimeOffset lastPing=DateTimeOffset.MinValue;
 int? lastExitCode;
 string[] oldConsole=[];string[] oldLogs=[];readonly object captureGate=new();
 public BridgeBackend(PanelConfig config,string configPath){this.config=config;this.configPath=Path.GetFullPath(configPath);statePath=Path.GetDirectoryName(this.configPath)!;active=config.Bridge;
  store=new(Path.Combine(statePath,"diagnostics"),()=>config.Bridge);
  inner=config.Backend=="windows"?new WindowsServerBackend(config,LaunchArguments):new PosixServerBackend(config,LaunchArguments);
  if(inner is WindowsServerBackend windows)windows.BeginMonitoring();collecting=Collect();
 }
 public static void ValidateSettings(BridgeSettings s){if(s.Port is <1024 or >65535||s.RetentionDays is <1 or >30||s.MaxMegabytes is <10 or >1000)throw new InvalidOperationException("bridge.settings");}
 string[] LaunchArguments(){if(config.ServerArguments.Any(x=>x=="--mcp"||x.StartsWith("--mcp-")))throw new InvalidOperationException("bridge.arguments");return active.Enabled?[..config.ServerArguments,..new LanBridgeLaunchPolicy().Arguments(endpoint,token)]:config.ServerArguments;}
 public bool Connected=>hub?.Current is {Closed:false};
 public string? BuildVersion(){if(Connected)return hub!.Current!.Identity.BuildCommit;return inner.ConsoleSnapshot().Select(x=>System.Text.RegularExpressions.Regex.Match(x,@"Running version ([a-f0-9]{8,40})")).LastOrDefault(x=>x.Success)?.Groups[1].Value;}
 public BridgeDiagnosis Diagnosis(){var agent=hub?.Current;return new(config.Bridge.Enabled,active.Enabled,Connected,Connected?"bridge":"pty",agent?.Health??(inner.Status().State=="stopped"?"dead":"unknown"),agent?.Phase??"unknown",Redact(error??agent?.Detail??""),agent?.Identity.BuildCommit,agent?.Tick,agent?.LastSeen,started,lastExit,lastExitCode,config.Bridge,config.Bridge.Enabled!=active.Enabled||config.Bridge.Port!=active.Port);}
 public DiagnosticPage Read(long cursor,string source,string? module,string? search,int limit,bool history){var scope=Connected?new[]{epoch,hub!.Current!.SessionId}:new[]{epoch};return store.Read(cursor,source,module,search,limit,history,history?null:scope) with{StreamId=history?"history":string.Join(':',scope)};}
 public async Task<BridgeStream> Execute(string command,CancellationToken ct=default){ValidateCommand(command);var agent=hub?.Current;if(agent is null||agent.Closed)throw new InvalidOperationException("bridge.disconnected");
  var result=BridgeConnection.ParseStream(await agent.Call("console.exec",new{command,wait_ms=1500},ct));return result with{Lines=result.Lines.Select(x=>x with{Text=Redact(x.Text)}).ToArray()};
 }
 public Task<BridgeStream> Stream(long cursor,CancellationToken ct)=>StreamCore(cursor,ct);
 async Task<BridgeStream> StreamCore(long cursor,CancellationToken ct){var agent=hub?.Current??throw new InvalidOperationException("bridge.disconnected");return BridgeConnection.ParseStream(await agent.Call("stream.read",new{cursor},ct));}
 public static void ValidateCommand(string command){if(string.IsNullOrWhiteSpace(command)||command.Length>1024||command.Any(char.IsControl)||command.Contains(';')||command.Split(' ',StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() is "exit" or "update")throw new InvalidOperationException("command.invalid");}
 public async Task SendAsync(string command){ValidateCommand(command);if(Connected)await Execute(command);else await inner.SendAsync(command);}
 public ServerStatus Status()=>inner.Status();
 public async Task StartAsync(){await lifecycle.WaitAsync();try{
  if(inner.Status().State!="stopped")throw new InvalidOperationException("server.running");ValidateSettings(config.Bridge);
  if(hub is not null){await hub.DisposeAsync();hub=null;}active=config.Bridge;
  if(config.Backend=="docker"){var docker=new DockerEngine(config);var address=active.Enabled?await docker.PrepareBridgeNetwork(configPath):null;endpoint=address is null?"":address+":"+active.Port;
   if(active.Enabled){hub=new(token,Owns);hub.Start(IPAddress.Parse(address!),active.Port);}
   await docker.ConfigureBridge(active.Enabled,endpoint,token,statePath);PanelConfig.Save(configPath,config);
  }else if(active.Enabled){endpoint="127.0.0.1:"+active.Port;hub=new(token,Owns);try{hub.Start(IPAddress.Loopback,active.Port);}catch(System.Net.Sockets.SocketException){hub=null;throw new InvalidOperationException("bridge.port");}}
  await inner.StartAsync();started=DateTimeOffset.UtcNow;error=null;
 }finally{lifecycle.Release();}}
 async Task<bool> Owns(BridgeIdentity identity,IPAddress remote,CancellationToken ct){
  for(int n=0;n<30;n++){var status=inner.Status();if(status.State=="running"){
   if(config.Backend=="docker")return await new DockerEngine(config).OwnsBridge(identity,remote,ct);
   if(!IPAddress.IsLoopback(remote)||!string.Equals(Path.GetFullPath(identity.Workdir),Path.GetFullPath(config.ServerRoot),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return false;
   if(OperatingSystem.IsWindows())return identity.Pid==status.Pid;
   try{var children=File.ReadAllText($"/proc/{status.Pid}/task/{status.Pid}/children").Split(' ',StringSplitOptions.RemoveEmptyEntries);return children.Contains(identity.Pid.ToString())&&File.ResolveLinkTarget($"/proc/{identity.Pid}/exe",false)?.FullName==Path.Combine(config.ServerRoot,"REServer");}catch(IOException){return false;}
  }await Task.Delay(100,ct);}return false;
 }
 public Task StopAsync(bool force=false)=>inner.StopAsync(force);
 public string[] ConsoleSnapshot()=>Connected?store.Snapshot("console",hub!.Current!.SessionId):inner.ConsoleSnapshot().Select(Redact).ToArray();
 public string[] LogSnapshot()=>Connected?store.Snapshot("log",hub!.Current!.SessionId):inner.LogSnapshot().Select(Redact).ToArray();
 string Redact(string text)=>text.Replace(token,"[redacted]",StringComparison.Ordinal);
 void Capture(BridgeConnection agent,BridgeStream result){lock(captureGate){if(bridgeEpoch!=agent.SessionId){bridgeEpoch=agent.SessionId;sourceCursor=0;}
  if(result.Dropped>0)store.Append(agent.SessionId,null,"panel","bridge",$"stream gap: {result.Dropped}");
  foreach(var line in result.Lines.OrderBy(x=>x.Seq))if(line.Seq>sourceCursor){store.Append(agent.SessionId,line.Seq,line.Source,line.Module,Redact(line.Text));sourceCursor=line.Seq;}
  // console.exec may return a later subset; stream polling owns the global cursor independently.
 }}
 void CaptureSnapshot(string[] lines,ref string[] previous,string source){int overlap=Math.Min(previous.Length,lines.Length);while(overlap>0&&!previous.AsSpan(previous.Length-overlap).SequenceEqual(lines.AsSpan(0,overlap)))overlap--;
  foreach(var line in lines.Skip(overlap).Where(x=>!string.IsNullOrWhiteSpace(x)))store.Append(epoch,null,source,"",Redact(line));previous=lines;
 }
 async Task Collect(){long pollCursor=0;string session="";
  while(!stop.IsCancellationRequested){try{
   var status=inner.Status();if(status.State=="running"&&status.Pid!=observedPid){observedPid=status.Pid;started=DateTimeOffset.UtcNow;epoch=Guid.NewGuid().ToString("N");oldConsole=[];oldLogs=[];store.Append(epoch,null,"panel","process","started");}
   else if(status.State=="stopped"&&observedPid is not null){lastExit=DateTimeOffset.UtcNow.ToString("O");lastExitCode=inner.ExitCode();store.Append(epoch,null,"panel","process","exited: "+(lastExitCode?.ToString()??"unknown"));observedPid=null;}
   if(hub?.Current is {Closed:false} agent){
    if(session!=agent.SessionId){session=agent.SessionId;pollCursor=0;lastPing=DateTimeOffset.MinValue;}
    if(DateTimeOffset.UtcNow-lastPing>TimeSpan.FromSeconds(10)){lastPing=DateTimeOffset.UtcNow;await agent.Ping(stop.Token);}
    var data=BridgeConnection.ParseStream(await agent.Call("stream.read",new{cursor=pollCursor},stop.Token));Capture(agent,data);pollCursor=data.Cursor;error=null;
   }else{CaptureSnapshot(inner.ConsoleSnapshot(),ref oldConsole,"console");if(status.State=="running")CaptureSnapshot(inner.LogSnapshot(),ref oldLogs,"log");}
   store.Cleanup();
  }catch(Exception e) when(e is InvalidOperationException or IOException or System.Net.Sockets.SocketException or OperationCanceledException or JsonException or KeyNotFoundException or FormatException or UnauthorizedAccessException){error=e is KeyNotFoundException or FormatException or JsonException?"bridge.protocol":ErrorCodes.From(e);}
  try{await Task.Delay(1000,stop.Token);}catch(OperationCanceledException){break;}
 }}
 public async ValueTask DisposeAsync(){stop.Cancel();if(hub is not null)await hub.DisposeAsync();await collecting;await inner.DisposeAsync();stop.Dispose();lifecycle.Dispose();}
}
