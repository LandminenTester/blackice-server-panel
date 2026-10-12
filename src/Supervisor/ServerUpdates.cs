using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

public sealed record ServerVersion(string Commit,string Source);
public sealed record ServerUpdateJob(string Id,string State,string Phase,long Bytes=0,string? Target=null,string? Error=null);
public sealed record ServerUpdateInfo(string? Current,string? Latest,string Channel,ServerVersion[] Versions,ServerUpdateJob? Job);
public sealed record ServerManifest(string Commit,Dictionary<string,string> Files);
public sealed class ServerUpdates : IAsyncDisposable {
 readonly PanelConfig config;readonly Func<IServerBackend?> backend;readonly string state,record;readonly HttpClient http;readonly SemaphoreSlim gate=new(1);
 readonly CancellationTokenSource stopped=new();CancellationTokenSource? cancel;Task? work;ServerManifest? latest;ServerManifest? staged;string? stage;volatile ServerUpdateJob? job;readonly object saving=new();
 int disposed;
 public bool Applying=>job?.State=="applying";
 string Platform=>config.Backend=="windows"?"server_windows":"server_linux";
 public ServerUpdates(PanelConfig config,string configPath,Func<IServerBackend?> backend,HttpClient? http=null){this.config=config;this.backend=backend;state=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!,"server-updates");record=Path.Combine(state,"job.json");this.http=http??new(){Timeout=TimeSpan.FromMinutes(20)};Directory.CreateDirectory(state);
  if(!OperatingSystem.IsWindows())File.SetUnixFileMode(state,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  if(File.Exists(record))try{job=JsonSerializer.Deserialize<ServerUpdateJob>(File.ReadAllText(record),PanelConfig.Json);if(job?.State is "downloading" or "ready" or "applying")job=job with{State="failed",Error="serverUpdate.interrupted"};}catch(JsonException){}
 }
 public static bool ManagedPath(string relative){if(relative.Contains('\\')||relative.StartsWith('/')||relative.Split('/').Any(x=>x is "" or "." or "..")||relative.Contains(':'))return false;
  return relative is "contents.json" or "version.json" or "cfg/server.cfg.example" or "data/cacert.pem"||relative.StartsWith("data/licenses/",StringComparison.Ordinal)||Regex.IsMatch(relative,@"\A(?:REServer(?:\.exe)?|[A-Za-z0-9_+.-]+\.(?:dll|so(?:\.[0-9]+)*)|modules_[A-Za-z0-9_]+\.json)\z");
 }
 public static void Validate(ServerManifest manifest){if(!Regex.IsMatch(manifest.Commit,"^[a-f0-9]{8,40}$")||manifest.Files.Count is <1 or >5000||manifest.Files.Any(x=>!ManagedPath(x.Key)||!Regex.IsMatch(x.Value,"^[a-fA-F0-9]{64}$"))||manifest.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Files.Count||!manifest.Files.Keys.Any(x=>x is "REServer" or "REServer.exe"))throw new InvalidOperationException("serverUpdate.manifest");}
 string Safe(string root,string relative){if(!ManagedPath(relative))throw new InvalidOperationException("serverUpdate.manifest");var result=Path.GetFullPath(Path.Combine(root,relative));var parent=Path.GetDirectoryName(result);
  while(parent is not null&&parent.Length>=Path.GetFullPath(root).Length){if(Directory.Exists(parent)&&(File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("serverUpdate.path");parent=Path.GetDirectoryName(parent);}if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("serverUpdate.path");return result;
 }
 ServerVersion[] Versions()=> (latest is null?Array.Empty<ServerVersion>():[new ServerVersion(latest.Commit,"official-alpha")]).Concat(Directory.GetFiles(state,"backup.json",SearchOption.AllDirectories).Select(file=>{try{return new ServerVersion(JsonSerializer.Deserialize<ServerManifest>(File.ReadAllText(file),PanelConfig.Json)!.Commit,"local-backup");}catch{return null;}}).OfType<ServerVersion>().Where(x=>Regex.IsMatch(x.Commit,"^[a-f0-9]{8,40}$"))).DistinctBy(x=>x.Commit).ToArray();
 public ServerUpdateInfo Status()=>new((backend() as BridgeBackend)?.BuildVersion()??RecordedVersion(),latest?.Commit,"alpha",Versions(),job);
 string? RecordedVersion(){var path=Path.Combine(state,"installed.json");if(!File.Exists(path))return null;try{var m=JsonSerializer.Deserialize<ServerManifest>(File.ReadAllText(path),PanelConfig.Json)!;var exe=config.Backend=="windows"?"REServer.exe":"REServer";return m.Files.TryGetValue(exe,out var digest)&&Hash(Safe(config.ServerRoot,exe))==digest?m.Commit:null;}catch(IOException){return null;}catch(JsonException){return null;}}
 static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
 public async Task<ServerUpdateInfo> Check(CancellationToken ct){await gate.WaitAsync(ct);try{
  if(!config.Configured)throw new InvalidOperationException("setup.required");
  var prefix=$"https://artifacts.replay.re/{Platform}/alpha/";
  using var version=JsonDocument.Parse(await http.GetStringAsync(prefix+"version.json",ct));var commit=version.RootElement.GetProperty("commit").GetString()!;
  var files=JsonSerializer.Deserialize<Dictionary<string,string>>(await http.GetStringAsync(prefix+"contents.json",ct))!;
  files.Remove("data/ugc/.gitkeep"); // Official empty-directory marker; never manage the user's bundle directory.
  latest=new(commit,files);Validate(latest);
  var exe=config.Backend=="windows"?"REServer.exe":"REServer";if(File.Exists(Safe(config.ServerRoot,exe))&&Hash(Safe(config.ServerRoot,exe))==latest.Files[exe])await WriteInstalled(latest);
  return Status();
 }catch(HttpRequestException){throw new InvalidOperationException("serverUpdate.network");}finally{gate.Release();}}
 void SaveJob(ServerUpdateJob value){lock(saving){var temp=record+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value,PanelConfig.Json));File.Move(temp,record,true);job=value;}}
 async Task WriteInstalled(ServerManifest value){await File.WriteAllTextAsync(Path.Combine(state,"installed.json"),JsonSerializer.Serialize(value,PanelConfig.Json));}
 public async Task<ServerUpdateInfo> Download(string? target,CancellationToken ct){await gate.WaitAsync(ct);try{
  if(job?.State is "downloading" or "applying" or "ready")throw new InvalidOperationException("serverUpdate.busy");if(latest is null)throw new InvalidOperationException("serverUpdate.check");
  target=string.IsNullOrEmpty(target)?latest.Commit:target;if(!Regex.IsMatch(target,"^[a-f0-9]{8,40}$"))throw new InvalidOperationException("serverUpdate.versionUnavailable");
  var id=Guid.NewGuid().ToString("N");stage=Path.Combine(state,id,"stage");Directory.CreateDirectory(stage);cancel=CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
  SaveJob(new(id,"downloading","download",Target:target));work=Prepare(target,stage,cancel.Token);return Status();
 }finally{gate.Release();}}
 async Task Prepare(string target,string directory,CancellationToken ct){try{
  string? local=null;
  if(target==latest!.Commit)staged=latest;
  else {var file=Directory.GetFiles(state,"backup.json",SearchOption.AllDirectories).FirstOrDefault(x=>JsonSerializer.Deserialize<ServerManifest>(File.ReadAllText(x),PanelConfig.Json)?.Commit==target)??throw new InvalidOperationException("serverUpdate.versionUnavailable");staged=JsonSerializer.Deserialize<ServerManifest>(File.ReadAllText(file),PanelConfig.Json)!;local=Path.Combine(Path.GetDirectoryName(file)!,"backup");}
  Validate(staged);long total=0;
  foreach(var file in staged.Files){ct.ThrowIfCancellationRequested();var path=Safe(directory,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   if(local is not null){File.Copy(Safe(local,file.Key),path,false);total+=new FileInfo(path).Length;}
   else {using var response=await http.GetAsync($"https://artifacts.replay.re/{Platform}/alpha/"+string.Join('/',file.Key.Split('/').Select(Uri.EscapeDataString)),HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
    await using var input=await response.Content.ReadAsStreamAsync(ct);await using var output=File.Create(path);var buffer=new byte[65536];long bytes=0;int n;
    while((n=await input.ReadAsync(buffer,ct))>0){bytes+=n;total+=n;if(bytes>512L*1024*1024||total>2L*1024*1024*1024)throw new InvalidOperationException("serverUpdate.size");await output.WriteAsync(buffer.AsMemory(0,n),ct);}
   }
   if(Hash(path)!=file.Value.ToLowerInvariant())throw new InvalidOperationException("serverUpdate.integrity");
   if(!OperatingSystem.IsWindows()&&(file.Key=="REServer"||file.Key.Contains(".so")))File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
   SaveJob(job! with{Bytes=total});
  }
  SaveJob(job! with{State="ready",Phase="verified"});
 }catch(OperationCanceledException){SaveJob(job! with{State="cancelled",Phase="cancelled"});}catch(Exception e){SaveJob(job! with{State="failed",Error=e.Message.StartsWith("serverUpdate.")?e.Message:"serverUpdate.network"});}}
 public async Task Cancel(){if(Applying)throw new InvalidOperationException("serverUpdate.busy");cancel?.Cancel();if(work is not null)await work;staged=null;if(job is not null)SaveJob(job with{State="cancelled",Phase="cancelled"});}
 public async Task<ServerUpdateInfo> Apply(){await gate.WaitAsync();try{if(job?.State!="ready"||staged is null||stage is null)throw new InvalidOperationException("serverUpdate.check");
  SaveJob(job with{State="applying",Phase="stopping"});work=Install(staged,stage);return Status();
 }finally{gate.Release();}}
 async Task Install(ServerManifest target,string directory){var engine=backend()??throw new InvalidOperationException("setup.required");var wasRunning=engine.Status().State=="running";var previous=Status().Current;var changed=new List<(string,string,bool)>();var backupDir=Path.Combine(Path.GetDirectoryName(directory)!,"backup");
  try {if(engine.Status().State=="external")throw new InvalidOperationException("server.external");if(wasRunning)await engine.StopAsync();
   if(target.Commit!=latest?.Commit){var settings=Path.Combine(config.ServerRoot,"settings","server.settings.json");if(!File.Exists(settings))throw new InvalidOperationException("serverUpdate.autoUpdate");using var data=JsonDocument.Parse(await File.ReadAllTextAsync(settings));if(!data.RootElement.TryGetProperty("auto_update",out var automatic)||automatic.ValueKind!=JsonValueKind.False)throw new InvalidOperationException("serverUpdate.autoUpdate");}
   // Revalidate staged bytes immediately before touching the live installation.
   foreach(var file in target.Files)if(Hash(Safe(directory,file.Key))!=file.Value.ToLowerInvariant())throw new InvalidOperationException("serverUpdate.integrity");
   var oldFiles=new Dictionary<string,string>();var localManifest=Path.Combine(config.ServerRoot,"contents.json");var names=File.Exists(localManifest)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(localManifest))!.Keys.Where(ManagedPath).ToArray():target.Files.Keys.ToArray();
   foreach(var name in names.Union(target.Files.Keys).Union(["contents.json","version.json"])){var path=Safe(config.ServerRoot,name);if(File.Exists(path)){var backup=Safe(backupDir,name);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(path,backup,false);oldFiles[name]=Hash(backup);}}
   if(previous is not null)await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(directory)!,"backup.json"),JsonSerializer.Serialize(new ServerManifest(previous,oldFiles),PanelConfig.Json));
   SaveJob(job! with{Phase="installing"});
   var replacements=target.Files.Keys.ToDictionary(x=>x,x=>Safe(directory,x));
   foreach(var metadata in new[]{("contents.json",JsonSerializer.Serialize(target.Files)),("version.json",JsonSerializer.Serialize(new{commit=target.Commit}))})if(!replacements.ContainsKey(metadata.Item1)){var generated=Safe(directory,metadata.Item1);await File.WriteAllTextAsync(generated,metadata.Item2);replacements[metadata.Item1]=generated;}
   foreach(var file in replacements){var path=Safe(config.ServerRoot,file.Key);changed.Add((path,Safe(backupDir,file.Key),File.Exists(path)));}
   if(config.Backend=="docker")await new DockerEngine(config).InstallRuntime(replacements,Path.GetDirectoryName(directory)!);
   else foreach(var file in replacements){var path=Safe(config.ServerRoot,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(file.Value,path,true);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,File.GetUnixFileMode(file.Value));}
   if(wasRunning){await engine.StartAsync();await Task.Delay(2500);if(engine.Status().State!="running")throw new InvalidOperationException("serverUpdate.startup");var actual=(engine as BridgeBackend)?.BuildVersion();if(actual is not null&&!actual.StartsWith(target.Commit,StringComparison.Ordinal)&&!target.Commit.StartsWith(actual,StringComparison.Ordinal))throw new InvalidOperationException("serverUpdate.startup");}
   await WriteInstalled(target);SaveJob(job! with{State="completed",Phase="completed"});
  }catch(Exception e){try{if(engine.Status().State=="running")await engine.StopAsync();
   if(config.Backend=="docker"&&changed.Count>0){var docker=new DockerEngine(config);await docker.InstallRuntime(changed.Where(x=>x.Item3).ToDictionary(x=>Path.GetRelativePath(config.ServerRoot,x.Item1).Replace('\\','/'),x=>x.Item2),Path.GetDirectoryName(directory)!);await docker.RemoveRuntime(changed.Where(x=>!x.Item3).Select(x=>Path.GetRelativePath(config.ServerRoot,x.Item1).Replace('\\','/')).ToArray());}
   else foreach(var file in changed.AsEnumerable().Reverse()){if(file.Item3)File.Copy(file.Item2,file.Item1,true);else File.Delete(file.Item1);}
   if(wasRunning&&engine.Status().State=="stopped")await engine.StartAsync();SaveJob(job! with{State="failed",Phase="rolled-back",Error=e.Message.StartsWith("serverUpdate.")?e.Message:"serverUpdate.failed"});}catch{SaveJob(job! with{State="failed",Phase="recovery",Error="serverUpdate.recovery"});}}
 }
 public async ValueTask DisposeAsync(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stopped.Cancel();cancel?.Cancel();if(work is not null)await work;http.Dispose();gate.Dispose();stopped.Dispose();cancel?.Dispose();}
}
