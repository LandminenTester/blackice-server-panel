using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
var count=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
void Invalid(Action action,string label){try{action();throw new Exception("accepted: "+label);}catch(InvalidOperationException){Check(true,label);}}
var root=Path.Combine(Path.GetTempPath(),"blackice-server-update-"+Guid.NewGuid().ToString("N"));var game=Path.Combine(root,"server");Directory.CreateDirectory(game);Directory.CreateDirectory(Path.Combine(game,"settings"));File.WriteAllText(Path.Combine(game,"settings","server.settings.json"),"{\"auto_update\":false}");Directory.CreateDirectory(Path.Combine(game,"cfg"));Directory.CreateDirectory(Path.Combine(game,"data","ugc","mine"));
var bytes=Encoding.UTF8.GetBytes("new-runtime");var old=Encoding.UTF8.GetBytes("old-runtime");var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();var oldHash=Convert.ToHexString(SHA256.HashData(old)).ToLowerInvariant();
await File.WriteAllBytesAsync(Path.Combine(game,"REServer.exe"),old);await File.WriteAllTextAsync(Path.Combine(game,"cfg","server.cfg"),"private configuration");await File.WriteAllTextAsync(Path.Combine(game,"data","ugc","mine","config.toml"),"private bundle");
var config=new PanelConfig{Backend="windows",ServerRoot=game,PasswordHash="configured"};var configPath=Path.Combine(root,"panel.json");PanelConfig.Save(configPath,config);
Directory.CreateDirectory(Path.Combine(root,"server-updates"));await File.WriteAllTextAsync(Path.Combine(root,"server-updates","installed.json"),JsonSerializer.Serialize(new ServerManifest("aaaaaaaa",new(){{"REServer.exe",oldHash}}),PanelConfig.Json));
var backend=new Backend();var handler=new Handler(bytes,hash);await using var updates=new ServerUpdates(config,configPath,()=>backend,new HttpClient(handler));
Check(updates.Status().Current=="aaaaaaaa","installed metadata verified against executable hash");
var info=await updates.Check(CancellationToken.None);Check(info.Latest=="bbbbbbbb","official alpha version discovered");
Invalid(()=>ServerUpdates.Validate(new("bbbbbbbb",new(){{"../REServer.exe",hash}})),"manifest traversal rejected");
Invalid(()=>ServerUpdates.Validate(new("bbbbbbbb",new(){{"REServer.exe",hash},{"cfg/server.cfg",hash}})),"private configuration cannot be a managed update file");
Invalid(()=>ServerUpdates.Validate(new("bbbbbbbb",new(){{"REServer.exe","invalid"}})),"invalid digest rejected");
Invalid(()=>ServerUpdates.Validate(new("bbbbbbbb",new(){{"REServer.exe",hash},{"reserver.exe",hash}})),"case collisions rejected");
async Task Wait(string state){for(var n=0;n<200;n++){var j=updates.Status().Job!;if(j.State==state)return;if(j.State=="failed")throw new Exception(JsonSerializer.Serialize(j));await Task.Delay(30);}throw new Exception("timeout "+state);}
await updates.Download("bbbbbbbb",CancellationToken.None);await Wait("ready");Check(File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(old),"preparation leaves live files unchanged");
await updates.Apply();await Wait("completed");Check(File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(bytes),"verified files installed");
Check(updates.Status().Current=="bbbbbbbb"&&backend.Running,"version changes and running server restarts");
Check(File.ReadAllText(Path.Combine(game,"cfg","server.cfg"))=="private configuration"&&File.ReadAllText(Path.Combine(game,"data","ugc","mine","config.toml"))=="private bundle","configuration and bundles retained");
Check(updates.Status().Versions.Any(x=>x.Commit=="aaaaaaaa"&&x.Source=="local-backup"),"previous build becomes selectable local backup");
await updates.Download("aaaaaaaa",CancellationToken.None);await Wait("ready");await updates.Apply();await Wait("completed");Check(updates.Status().Current=="aaaaaaaa"&&File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(old),"specific backed-up build can be restored");
handler.Corrupt=true;await updates.Download("bbbbbbbb",CancellationToken.None);for(var n=0;updates.Status().Job?.State=="downloading"&&n<100;n++)await Task.Delay(30);Check(updates.Status().Job?.Error=="serverUpdate.integrity"&&File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(old),"corrupt download cannot replace files");handler.Corrupt=false;
await updates.Download("bbbbbbbb",CancellationToken.None);await Wait("ready");await updates.Cancel();Check(updates.Status().Job?.State=="cancelled"&&File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(old),"cancelled prepared job leaves installation intact");
await updates.Download("bbbbbbbb",CancellationToken.None);await Wait("ready");backend.FailNextStart=true;await updates.Apply();for(var n=0;updates.Status().Job?.State=="applying"&&n<200;n++)await Task.Delay(30);Check(updates.Status().Job is{State:"failed",Phase:"rolled-back"}&&File.ReadAllBytes(Path.Combine(game,"REServer.exe")).SequenceEqual(old)&&backend.Running,"startup failure restores runtime and restarts old server");
await updates.Download("cccccccc",CancellationToken.None);for(var n=0;updates.Status().Job?.State=="downloading"&&n<100;n++)await Task.Delay(30);Check(updates.Status().Job?.Error=="serverUpdate.versionUnavailable","unpublished historical version not fabricated");
await updates.Cancel();handler.Offline=true;try{await updates.Check(CancellationToken.None);throw new Exception("offline accepted");}catch(InvalidOperationException e){Check(e.Message=="serverUpdate.network","network failure reported without modifying server");}
Console.WriteLine($"{count} server update checks passed");
sealed class Handler(byte[] bytes,string hash):HttpMessageHandler {public bool Corrupt,Offline;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){if(Offline)throw new HttpRequestException();var path=req.RequestUri!.AbsolutePath;HttpContent content=path.EndsWith("version.json")?new StringContent("{\"commit\":\"bbbbbbbb\"}"):path.EndsWith("contents.json")?new StringContent(JsonSerializer.Serialize(new Dictionary<string,string>{{"REServer.exe",hash},{"data/ugc/.gitkeep",hash}})):new ByteArrayContent(Corrupt?[1,2,3]:bytes);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});}}
sealed class Backend:IServerBackend {public bool Running=true,FailNextStart;public ServerStatus Status()=>new(Running?"running":"stopped",Running?123:null,"","keep-running");public Task StartAsync(){if(FailNextStart){FailNextStart=false;throw new InvalidOperationException("serverUpdate.startup");}Running=true;return Task.CompletedTask;}public Task StopAsync(bool force=false){Running=false;return Task.CompletedTask;}public Task SendAsync(string c)=>Task.CompletedTask;public string[] ConsoleSnapshot()=>[];public string[] LogSnapshot()=>[];public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
