using System.Diagnostics;
using System.IO.Compression;
using System.Formats.Tar;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed record PanelRelease(string Version,string Url,string Asset,string Download,string Digest,long Size,bool Prerelease=false);
public sealed record UpdateJob(string Id,string State,string Phase,long Bytes=0,long Total=0,string? Error=null);
public sealed record UpdateInfo(string Current,string? Latest,bool Available,bool CanInstall,string? ReleaseUrl,UpdateJob? Job,string Channel="stable",bool Prerelease=false);
/// <summary>Panel versions: MAJOR.MINOR.PATCH with an optional -experimental.N suffix; a pre-release sorts below the same stable version.</summary>
public static class PanelVersion {
 static readonly Regex Pattern=new(@"^([0-9]{1,6})[.]([0-9]{1,6})[.]([0-9]{1,6})(?:-experimental[.]([0-9]{1,6}))?$",RegexOptions.Compiled);
 public static bool TryParse(string? value,out (int Major,int Minor,int Patch,int? Pre) version){version=default;if(value is null)return false;var m=Pattern.Match(value);if(!m.Success)return false;version=(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value),int.Parse(m.Groups[3].Value),m.Groups[4].Success?int.Parse(m.Groups[4].Value):null);return true;}
 public static bool IsPrerelease(string value)=>value.Contains('-');
 public static int Compare(string a,string b){
  if(!TryParse(a,out var x)||!TryParse(b,out var y))throw new FormatException("version");
  var core=x.Major.CompareTo(y.Major);if(core!=0)return core;core=x.Minor.CompareTo(y.Minor);if(core!=0)return core;core=x.Patch.CompareTo(y.Patch);if(core!=0)return core;
  if(x.Pre is null&&y.Pre is null)return 0;if(x.Pre is null)return 1;if(y.Pre is null)return -1;return x.Pre.Value.CompareTo(y.Pre.Value);
 }
 public static bool IsNewer(string candidate,string running)=>TryParse(candidate,out _)&&TryParse(running,out _)&&Compare(candidate,running)>0;
}
public sealed record UpdateManifest(string Root,string Config,string Stage,string Target,string Previous);
public sealed class PanelUpdates : IDisposable {
 public const string Repository="LandminenTester/blackice-server-panel";
 public static string RunningVersion=>typeof(PanelUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
 readonly string root,configPath,stateFile;readonly HttpClient http;readonly SemaphoreSlim gate=new(1);readonly object stateGate=new();CancellationTokenSource? cancel;Task? downloadTask;PanelRelease? release;UpdateJob? job;DateTimeOffset checkedAt;string releaseChannel="stable";
 public PanelUpdates(string configPath,HttpClient? client=null,string? packageRoot=null){this.configPath=Path.GetFullPath(configPath);root=Path.GetFullPath(packageRoot??Path.Combine(AppContext.BaseDirectory,"..",".."));stateFile=Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")=="true"?Path.Combine(Path.GetDirectoryName(this.configPath)!,"panel-update.json"):Path.Combine(root,".local","panel-update.json");http=client??OutboundHttp.Create(TimeSpan.FromMinutes(15));http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackICE-Panel/"+RunningVersion);if(File.Exists(stateFile))try{job=JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(stateFile),PanelConfig.Json);if(job?.State is "downloading" or "verifying" or "ready")job=job with{State="failed",Error="update.interrupted"};}catch(JsonException){} }
 bool HasController {get {try{using var probe=new FileStream(Path.Combine(Path.GetDirectoryName(configPath)!,"controller.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);return false;}catch(IOException e){return File.Exists(Path.Combine(Path.GetDirectoryName(configPath)!,"controller.lock"))&&e is not FileNotFoundException;}}}
 public bool CanInstall=>DockerPanelUpdater.Enabled||HasController&&Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")!="true"&&File.Exists(Path.Combine(root,OperatingSystem.IsWindows()?"Start.exe":"Start"));
 public UpdateInfo Status(){lock(stateGate){if(job?.State=="applying"&&File.Exists(stateFile)){try{job=JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(stateFile),PanelConfig.Json)??job;}catch(IOException){}catch(JsonException){}}return new(RunningVersion,release?.Version,release is not null&&PanelVersion.IsNewer(release.Version,RunningVersion),CanInstall,release?.Url,job,releaseChannel,release?.Prerelease==true);}}
 public static PanelRelease ParseRelease(string json,bool windows,bool allowPrerelease=false){
  using var doc=JsonDocument.Parse(json);var value=doc.RootElement;
  var tag=value.GetProperty("tag_name").GetString()!;var version=tag.TrimStart('v');
  var prerelease=value.GetProperty("prerelease").GetBoolean();
  if(!PanelVersion.TryParse(version,out _)||value.GetProperty("draft").GetBoolean()||(prerelease&&!allowPrerelease)||prerelease!=PanelVersion.IsPrerelease(version))throw new InvalidOperationException("update.release");
  var name=windows?"blackice-server-panel-win-x64.zip":"blackice-server-panel-linux-x64.tar.gz";
  var asset=value.GetProperty("assets").EnumerateArray().SingleOrDefault(a=>a.GetProperty("name").GetString()==name);
  if(asset.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException("update.asset");
  var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
  if(digest is null||digest.Length!=71||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidOperationException("update.integrity");
  var url="https://github.com/"+Repository+"/releases/tag/"+tag;
  var download=asset.GetProperty("browser_download_url").GetString()!;
  if(download!="https://github.com/"+Repository+"/releases/download/"+tag+"/"+name)throw new InvalidOperationException("update.release");
  var size=asset.GetProperty("size").GetInt64();if(size<1||size>512L*1024*1024)throw new InvalidOperationException("update.size");
  return new(version,url,name,download,digest[7..],size,prerelease);
 }
 public async Task<UpdateInfo> Check(CancellationToken ct=default){await gate.WaitAsync(ct);try{
  var channel=PanelConfig.Load(configPath).UpdateChannel;
  if(release is not null&&releaseChannel==channel&&DateTimeOffset.UtcNow-checkedAt<TimeSpan.FromSeconds(60))return Status();
  PanelRelease? selected;
  if(channel=="stable"){
   using var response=await http.GetAsync("https://api.github.com/repos/"+Repository+"/releases/latest",ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("update.network");
   var text=await response.Content.ReadAsStringAsync(ct);if(text.Length>1024*1024)throw new InvalidOperationException("update.release");
   selected=ParseRelease(text,OperatingSystem.IsWindows());
  }else{
   // Experimental channel: newest valid release of any kind (pre-releases and stable) by version order.
   using var response=await http.GetAsync("https://api.github.com/repos/"+Repository+"/releases?per_page=30",ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("update.network");
   var text=await response.Content.ReadAsStringAsync(ct);if(text.Length>8*1024*1024)throw new InvalidOperationException("update.release");
   selected=SelectNewest(text,OperatingSystem.IsWindows())??throw new InvalidOperationException("update.release");
  }
  release=selected;releaseChannel=channel;checkedAt=DateTimeOffset.UtcNow;return Status();
 }catch(HttpRequestException){throw new InvalidOperationException("update.network");}finally{gate.Release();}}
 public static PanelRelease? SelectNewest(string json,bool windows){
  using var doc=JsonDocument.Parse(json);if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("update.release");
  PanelRelease? best=null;
  foreach(var item in doc.RootElement.EnumerateArray()){
   try{var candidate=ParseRelease(item.GetRawText(),windows,true);if(best is null||PanelVersion.Compare(candidate.Version,best.Version)>0)best=candidate;}
   catch(Exception e)when(e is InvalidOperationException or KeyNotFoundException or JsonException or InvalidCastException or FormatException){}
  }
  return best;
 }
 void Save(UpdateJob value){lock(stateGate){job=value;Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);var tmp=stateFile+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(value,PanelConfig.Json));File.Move(tmp,stateFile,true);}}
 public async Task<UpdateInfo> Download(CancellationToken ct=default){await Check(ct);await gate.WaitAsync(ct);try{
  if(!CanInstall)throw new InvalidOperationException("update.deployment");
  if(job?.State is "downloading" or "verifying" or "ready" or "applying")throw new InvalidOperationException("update.busy");
  if(!Status().Available)throw new InvalidOperationException("update.current");
  var selected=release!;cancel=new();var id=Guid.NewGuid().ToString("N");Save(new(id,"downloading","download",Total:selected.Size));downloadTask=Prepare(id,selected,cancel.Token);return Status();
 }finally{gate.Release();}}
 string UpdateDirectory(string id)=>DockerPanelUpdater.Enabled?Path.Combine(Path.GetDirectoryName(configPath)!,"panel-updates",id):Path.Combine(root,".local","updates",id);
 async Task Prepare(string id,PanelRelease selected,CancellationToken ct){try{
  var directory=UpdateDirectory(id);Directory.CreateDirectory(directory);
  var archive=Path.Combine(directory,selected.Asset);using(var response=await http.GetAsync(selected.Download,HttpCompletionOption.ResponseHeadersRead,ct)){
   response.EnsureSuccessStatusCode();using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write);byte[] buffer=new byte[65536];long bytes=0;var timer=Stopwatch.StartNew();int count;
   while((count=await input.ReadAsync(buffer,ct))>0){bytes+=count;if(bytes>selected.Size)throw new InvalidOperationException("update.size");await output.WriteAsync(buffer.AsMemory(0,count),ct);if(timer.ElapsedMilliseconds>400){Save(new(id,"downloading","download",bytes,selected.Size));timer.Restart();}}
   if(bytes!=selected.Size)throw new InvalidOperationException("update.integrity");
  }
  Save(new(id,"verifying","verify",selected.Size,selected.Size));using(var file=File.OpenRead(archive)){var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,ct));if(!hash.Equals(selected.Digest,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("update.integrity");}
  var stage=Path.Combine(directory,"stage");Extract(archive,stage,OperatingSystem.IsWindows(),ct);ValidatePackage(stage,selected.Version,OperatingSystem.IsWindows());
  var manifest=new UpdateManifest(root,configPath,stage,selected.Version,RunningVersion);File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(manifest,PanelConfig.Json));Save(new(id,"ready","ready",selected.Size,selected.Size));
 }catch(OperationCanceledException){Save(new(id,"cancelled","cancelled"));}catch(Exception e){Save(new(id,"failed","failed",Error:e is InvalidOperationException?e.Message:"update.download"));}}
 public async Task Cancel(){await gate.WaitAsync();try{if(job?.State is "applying")throw new InvalidOperationException("update.busy");cancel?.Cancel();if(downloadTask is not null)await downloadTask;if(job?.State=="ready")Save(job with{State="cancelled",Phase="cancelled"});}finally{gate.Release();}}
 public async Task<UpdateInfo> Apply(){await gate.WaitAsync();try{
  if(job?.State!="ready")throw new InvalidOperationException("update.notReady");if(!CanInstall)throw new InvalidOperationException("update.deployment");
  if(DockerPanelUpdater.Enabled){Save(job with{State="applying",Phase="restart"});try{await new DockerPanelUpdater(PanelConfig.Load(configPath),configPath).Apply(job.Id,release!.Version);}catch{Save(job with{State="ready",Phase="ready"});throw;}return Status();}
  var configured=PanelConfig.Load(configPath);var artifacts=Path.Combine(root,"artifacts")+Path.DirectorySeparatorChar;
  if(configPath.StartsWith(artifacts,StringComparison.OrdinalIgnoreCase)||configured.ServerRoot.StartsWith(artifacts,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("update.deployment");
  for(var current=new DirectoryInfo(root);current is not null;current=current.Parent)if(current.LinkTarget is not null)throw new InvalidOperationException("update.deployment");
  var directory=Path.Combine(root,".local","updates",job.Id);var worker=Path.Combine(directory,"worker");Directory.CreateDirectory(worker);
  foreach(var source in Directory.EnumerateFiles(AppContext.BaseDirectory,"*",SearchOption.AllDirectories)){var relative=Path.GetRelativePath(AppContext.BaseDirectory,source);var target=Path.Combine(worker,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target,true);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(target,File.GetUnixFileMode(source));}
  var executable=Path.Combine(worker,OperatingSystem.IsWindows()?"Supervisor.exe":"Supervisor");var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=worker};info.ArgumentList.Add("--apply-panel-update");info.ArgumentList.Add(Path.Combine(directory,"manifest.json"));
  Save(job with{State="applying",Phase="restart"});try{Process.Start(info)?.Dispose();}catch{Save(job with{State="ready",Phase="ready"});throw;}return Status();
 }finally{gate.Release();}}
 public void Dispose(){cancel?.Cancel();http.Dispose();}
 public static void Extract(string archive,string stage,bool windows,CancellationToken ct=default){
  Directory.CreateDirectory(stage);long bytes=0;int files=0;
  string? Target(string name,long length,bool directory){ct.ThrowIfCancellationRequested();if(++files>30000||(bytes+=length)>4L*1024*1024*1024)throw new InvalidOperationException("update.size");name=name.Replace('\\','/');while(name.StartsWith("./"))name=name[2..];if(name is "" or ".")return null;
   var path=Path.GetFullPath(Path.Combine(stage,name));if(!path.StartsWith(Path.GetFullPath(stage)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||name.Split('/').Any(x=>x=="..")||name.Contains(':'))throw new InvalidOperationException("update.archive");
   if(!(name=="VERSION"||name==(windows?"Start.exe":"Start")||name.StartsWith("artifacts/")))return null;
   if(directory){Directory.CreateDirectory(path);return null;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);return path;
  }
  if(windows){using var zip=ZipFile.OpenRead(archive);foreach(var entry in zip.Entries){if((entry.ExternalAttributes>>16&0xF000)==0xA000)throw new InvalidOperationException("update.archive");var path=Target(entry.FullName,entry.Length,entry.Name.Length==0);if(path is not null)entry.ExtractToFile(path,false);}}
  else{using var input=File.OpenRead(archive);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var reader=new TarReader(gzip);TarEntry? entry;while((entry=reader.GetNextEntry()) is not null){if(entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))throw new InvalidOperationException("update.archive");var path=Target(entry.Name,entry.Length,entry.EntryType==TarEntryType.Directory);if(path is not null){using var file=new FileStream(path,FileMode.CreateNew);entry.DataStream?.CopyTo(file);}}}
 }
 public static void ValidatePackage(string stage,string version,bool windows){
  if(!File.Exists(Path.Combine(stage,"VERSION"))||File.ReadAllText(Path.Combine(stage,"VERSION")).Trim()!=version)throw new InvalidOperationException("update.version");
  foreach(var relative in new[]{windows?"Start.exe":"Start","artifacts/supervisor/"+(windows?"Supervisor.exe":"Supervisor"),"artifacts/web/"+(windows?"Web.exe":"Web"),"artifacts/web/wwwroot/index.html","artifacts/supervisor/Shared.dll","artifacts/web/Shared.dll","artifacts/web/Web.dll","artifacts/supervisor/Supervisor.dll"})if(!File.Exists(Path.Combine(stage,relative)))throw new InvalidOperationException("update.archive");
  foreach(var relative in new[]{windows?"Start.exe":"Start","artifacts/supervisor/"+(windows?"Supervisor.exe":"Supervisor"),"artifacts/web/"+(windows?"Web.exe":"Web")}.Concat(windows?[]:new[]{"artifacts/supervisor/replay-terminal"})){
   using var file=File.OpenRead(Path.Combine(stage,relative));byte[] magic=new byte[4];if(file.Read(magic)<4||(windows?magic[0]!=0x4d||magic[1]!=0x5a:magic[0]!=0x7f||magic[1]!=0x45||magic[2]!=0x4c||magic[3]!=0x46))throw new InvalidOperationException("update.archive");if(!OperatingSystem.IsWindows()&&!windows)File.SetUnixFileMode(Path.Combine(stage,relative),UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  }
 }
}
