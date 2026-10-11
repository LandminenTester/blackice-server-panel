using System.Diagnostics;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public static class PanelUpdateWorker {
 public static async Task Run(string manifestPath){
  manifestPath=Path.GetFullPath(manifestPath);var directory=Path.GetDirectoryName(manifestPath)!;
  var manifest=JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(manifestPath),PanelConfig.Json)??throw new InvalidOperationException("update.invalid");
  var root=Path.GetFullPath(manifest.Root);var stage=Path.GetFullPath(manifest.Stage);
  if(Path.GetFullPath(Path.Combine(directory,"..","..",".."))!=root||stage!=Path.Combine(directory,"stage")||!Path.GetFileName(directory).All(Uri.IsHexDigit)||Path.GetFileName(directory).Length!=32)throw new InvalidOperationException("update.invalid");
  foreach(var path in new[]{root,directory,stage,Path.Combine(root,"artifacts")}){for(var current=new DirectoryInfo(path);current is not null;current=current.Parent)if(current.LinkTarget is not null)throw new InvalidOperationException("update.archive");}
  using var single=new FileStream(Path.Combine(root,".local","update-worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  using var log=new StreamWriter(new FileStream(Path.Combine(directory,"apply.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite)){AutoFlush=true};Console.SetOut(log);Console.SetError(log);
  var id=Path.GetFileName(directory);var state=Path.Combine(root,".local","panel-update.json");
  void Save(string status,string phase,string? error=null){var tmp=state+".worker.tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(new UpdateJob(id,status,phase,Error:error),PanelConfig.Json));File.Move(tmp,state,true);}
  var launcher=OperatingSystem.IsWindows()?"Start.exe":"Start";var backup=Path.Combine(directory,"backup");Directory.CreateDirectory(backup);bool artifactsMoved=false,startMoved=false,newArtifacts=false,newStart=false;
  try{
   PanelUpdates.ValidatePackage(stage,manifest.Target,OperatingSystem.IsWindows());
   await Task.Delay(2000); // Let the initiating HTTP response reach the browser.
   if(await ControlConsole.StopAll(manifest.Config)!=0)throw new InvalidOperationException("update.stop");
   File.Copy(manifest.Config,Path.Combine(backup,"panel.json"),false);
   Save("applying","replace");
   Directory.Move(Path.Combine(root,"artifacts"),Path.Combine(backup,"artifacts"));artifactsMoved=true;
   File.Move(Path.Combine(root,launcher),Path.Combine(backup,launcher));startMoved=true;
   Directory.Move(Path.Combine(stage,"artifacts"),Path.Combine(root,"artifacts"));newArtifacts=true;
   File.Move(Path.Combine(stage,launcher),Path.Combine(root,launcher));newStart=true;
   if(File.Exists(Path.Combine(root,"VERSION")))File.Move(Path.Combine(root,"VERSION"),Path.Combine(backup,"VERSION"));File.Copy(Path.Combine(stage,"VERSION"),Path.Combine(root,"VERSION"),true);
   Save("applying","reconnect");Restart(root,manifest.Config);
   if(!await Healthy(manifest.Config,manifest.Target))throw new InvalidOperationException("update.health");
   Save("completed","completed");
  }catch(Exception e){
   Console.Error.WriteLine(e);
   if(artifactsMoved||startMoved){
    Save("applying","rollback");
    if(await ControlConsole.StopAll(manifest.Config)!=0){Save("failed","failed","update.rollback");return;}
    if(newArtifacts)Directory.Move(Path.Combine(root,"artifacts"),Path.Combine(directory,"failed-artifacts"));
    if(newStart)File.Move(Path.Combine(root,launcher),Path.Combine(directory,"failed-"+launcher));
    if(artifactsMoved)Directory.Move(Path.Combine(backup,"artifacts"),Path.Combine(root,"artifacts"));
    if(startMoved)File.Move(Path.Combine(backup,launcher),Path.Combine(root,launcher));
    if(File.Exists(Path.Combine(backup,"VERSION")))File.Copy(Path.Combine(backup,"VERSION"),Path.Combine(root,"VERSION"),true);
    File.Copy(Path.Combine(backup,"panel.json"),manifest.Config,true);
    Restart(root,manifest.Config);Save("failed","failed",await Healthy(manifest.Config,manifest.Previous)?"update.restored":"update.rollback");
   }else Save("failed","failed",e is InvalidOperationException?e.Message:"update.failed");
  }
 }
 static void Restart(string root,string config){
  var executable=Path.Combine(root,OperatingSystem.IsWindows()?"Start.exe":"Start");
  // A new real terminal owns the restarted stack; stdin must never be inherited from a detached worker.
  var previous=Environment.GetEnvironmentVariable("REPLAY_PANEL_CONFIG");Environment.SetEnvironmentVariable("REPLAY_PANEL_CONFIG",config);
  try{NativeConsole.Open(executable,["--config",config,"--update-restart"],"replay-panel-"+PanelConfig.Load(config).InstanceId[..8]);}finally{Environment.SetEnvironmentVariable("REPLAY_PANEL_CONFIG",previous);}
 }
 static async Task<bool> Healthy(string configPath,string version){
  var config=PanelConfig.Load(configPath);using var client=new HttpClient(){Timeout=TimeSpan.FromSeconds(2)};
  var seconds=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_PANEL_UPDATE_HEALTH_SECONDS"),out var requested)?Math.Clamp(requested,5,120):60;
  for(var i=0;i<seconds;i++){try{using var timeout=new CancellationTokenSource(2000);var boot=await ControlConsole.Call(config,new("bootstrap"));using var doc=JsonDocument.Parse(boot.Lines![0]);if(doc.RootElement.GetProperty("version").GetString()==version&&(await client.GetAsync(config.Url+"/api/session",timeout.Token)).IsSuccessStatusCode)return true;}catch(Exception e)when(e is IOException or HttpRequestException or System.Net.Sockets.SocketException or JsonException or KeyNotFoundException or OperationCanceledException or NullReferenceException){}await Task.Delay(1000);}
  return false;
 }
}
