using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
var root=Path.Combine(Path.GetTempPath(),"panel-update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,".local"));int count=0;
void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidOperationException or InvalidDataException or IOException){Check(true,label);return;}throw new Exception(label);}
try {
 var windows=OperatingSystem.IsWindows();var asset=windows?"replay-server-panel-win-x64.zip":"replay-server-panel-linux-x64.tar.gz";var version="999.0.0";
 var payloads=new Dictionary<string,byte[]>{{windows?"Start.exe":"Start",windows?[0x4d,0x5a,0,0]:[0x7f,0x45,0x4c,0x46]}, {"VERSION",System.Text.Encoding.UTF8.GetBytes(version)}, {"artifacts/supervisor/"+(windows?"Supervisor.exe":"Supervisor"),windows?[0x4d,0x5a,0,0]:[0x7f,0x45,0x4c,0x46]}, {"artifacts/web/"+(windows?"Web.exe":"Web"),windows?[0x4d,0x5a,0,0]:[0x7f,0x45,0x4c,0x46]}, {"artifacts/web/wwwroot/index.html",[1]}, {"artifacts/supervisor/Shared.dll",[1]}, {"artifacts/web/Shared.dll",[1]}, {"artifacts/supervisor/Supervisor.dll",[1]}, {"artifacts/web/Web.dll",[1]}, {".local/panel.json",System.Text.Encoding.UTF8.GetBytes("must-not-install")}};
 if(!windows)payloads.Add("artifacts/supervisor/replay-terminal",[0x7f,0x45,0x4c,0x46]);
 byte[] Archive(Dictionary<string,byte[]> files){using var buffer=new MemoryStream();if(windows){using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))foreach(var item in files){using var writer=zip.CreateEntry(item.Key).Open();writer.Write(item.Value);}}else{using(var gzip=new GZipStream(buffer,CompressionLevel.Fastest,true))using(var tar=new TarWriter(gzip,true))foreach(var item in files){tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile,"./"+item.Key){DataStream=new MemoryStream(item.Value)});}}return buffer.ToArray();}
 var bytes=Archive(payloads);var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 string Metadata(string digest,string url,bool prerelease=false)=>JsonSerializer.Serialize(new{tag_name="v"+version,draft=false,prerelease,assets=new[]{new{name=asset,digest="sha256:"+digest,size=bytes.Length,browser_download_url=url}}});
 var url="https://github.com/"+PanelUpdates.Repository+"/releases/download/v"+version+"/"+asset;var metadata=Metadata(hash,url);
 Check(PanelUpdates.ParseRelease(metadata,windows).Version==version,"stable official platform asset selected");
 Reject(()=>PanelUpdates.ParseRelease(Metadata(hash,"https://evil.invalid/file"),windows),"foreign download URL rejected");
 Reject(()=>PanelUpdates.ParseRelease(Metadata("bad",url),windows),"missing trustworthy digest rejected");
 Reject(()=>PanelUpdates.ParseRelease(Metadata(hash,url,true),windows),"prerelease rejected");
 var stage=Path.Combine(root,"stage");var archive=Path.Combine(root,asset);File.WriteAllBytes(archive,bytes);PanelUpdates.Extract(archive,stage,windows);PanelUpdates.ValidatePackage(stage,version,windows);
 Check(!File.Exists(Path.Combine(stage,".local","panel.json")),"archive cannot replace private configuration");
 Reject(()=>PanelUpdates.ValidatePackage(stage,"998.0.0",windows),"wrong package version rejected");
 var bad=Archive(new(){{"../escape",[1]}});File.WriteAllBytes(archive,bad);Reject(()=>PanelUpdates.Extract(archive,Path.Combine(root,"bad-stage"),windows),"archive traversal rejected");Check(!File.Exists(Path.Combine(root,"escape")),"archive never writes outside staging");
 File.WriteAllBytes(Path.Combine(root,windows?"Start.exe":"Start"),[1]);var path=Path.Combine(root,".local","panel.json");PanelConfig.Save(path,new());
 using var controller=new FileStream(Path.Combine(root,".local","controller.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.None);
 var handler=new FakeHandler(metadata,bytes);using var manager=new PanelUpdates(path,new HttpClient(handler),root);
 Check((await manager.Check()).Available,"newer release reported available");await manager.Download();
 for(var i=0;i<200&&manager.Status().Job?.State is "downloading" or "verifying";i++)await Task.Delay(10);
 Check(manager.Status().Job?.State=="ready","verified package reaches ready state");await manager.Cancel();Check(manager.Status().Job?.State=="cancelled","ready job can be cancelled without changing binaries");
 handler.Bytes=(byte[])bytes.Clone();handler.Bytes[0]^=0xff;await manager.Download();for(var i=0;i<200&&manager.Status().Job?.State is "downloading" or "verifying";i++)await Task.Delay(10);
 Check(manager.Status().Job?.State=="failed"&&manager.Status().Job?.Error=="update.integrity","corrupted download rejected by checksum");Check(File.ReadAllBytes(Path.Combine(root,windows?"Start.exe":"Start")).SequenceEqual(new byte[]{1}),"failed download leaves current launcher intact");
 handler.Fail=true;await manager.Download();for(var i=0;i<200&&manager.Status().Job?.State is "downloading" or "verifying";i++)await Task.Delay(10);Check(manager.Status().Job?.State=="failed","network failure leaves retryable failed job");
 Console.WriteLine(count+" update checks passed");
}finally{Directory.Delete(root,true);}
sealed class FakeHandler(string metadata,byte[] bytes):HttpMessageHandler{public byte[] Bytes=bytes;public bool Fail;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){if(request.RequestUri!.Host=="api.github.com")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(metadata)});if(Fail)throw new HttpRequestException("test failure");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Bytes)});}}
