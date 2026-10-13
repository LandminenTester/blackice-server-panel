using System.IO.Compression;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
using ReplayPanel.Supervisor.BundleTools;
var root=Path.Combine(Path.GetTempPath(),"panel-core-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed=0;
void Check(bool result,string label){if(!result)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
void Reject(Action test,string label){try{test();}catch(Exception e)when(e is InvalidOperationException or InvalidDataException or IOException){Check(true,label);return;}throw new Exception(label);}
try {
 var configPath=Path.Combine(root,"panel.json");
 var legacy=new {serverRoot=root,passwordHash=Convert.ToBase64String(new byte[32]),passwordSalt=Convert.ToBase64String(new byte[16]),shutdownMode="stop-server",pipeName="fixture-pipe",url="http://127.0.0.1:20772"};
 File.WriteAllText(configPath,JsonSerializer.Serialize(legacy));
 var config=PanelConfig.Load(configPath);
 Check(config.Username=="admin"&&config.PasswordHash==legacy.passwordHash&&config.ServerRoot==root&&config.ShutdownMode=="stop-server","legacy migration preserves account hash, path and shutdown behavior");
 Check(File.Exists(configPath+".v1.bak"),"legacy configuration backed up");
 PanelConfig.Load(configPath);Check(File.Exists(configPath+".v1.bak"),"migration is idempotent");
 config.SetPassword("long-test-password");Check(config.VerifyPassword("long-test-password")&&!config.VerifyPassword("different-password"),"salted authentication verifies only matching password");

 var legacyUser=Accounts.Authenticate(config,"ADMIN","long-test-password");Check(legacyUser is not null&&legacyUser.Permissions.Contains("users.manage"),"legacy admin receives all account permissions");
 Accounts.Save(config,configPath,new("reader","first-reader-password",["console.read"],Create:true));
 var reader=Accounts.Authenticate(config,"reader","first-reader-password")!;
 Check(PanelConfig.Load(configPath).Users!.Any(u=>u.Username=="reader"),"accounts persist across configuration reload");
 Check(!File.ReadAllText(configPath).Contains("first-reader-password"),"account password never persisted in clear text");
 Reject(()=>Accounts.Save(config,configPath,new("reader","short7!",["console.read"])),"password below 8 characters rejected");
 Reject(()=>Accounts.Save(config,configPath,new("reader",null,["invalid.permission"])),"unknown permission rejected");
 Reject(()=>Accounts.Save(config,configPath,new("admin",null,[],false)),"last user manager cannot be disabled");
 Accounts.Save(config,configPath,new("reader","changed-reader-password",["bundles.read"]));
 Check(Accounts.Session(config,"reader",reader.Stamp) is null&&Accounts.Authenticate(config,"reader","first-reader-password") is null,"password change revokes old password and session");
 var updated=Accounts.Authenticate(config,"reader","changed-reader-password")!;Accounts.Save(config,configPath,new("reader",null,["bundles.read"],false));
 Check(Accounts.Session(config,"reader",updated.Stamp) is null&&Accounts.Authenticate(config,"reader","changed-reader-password") is null,"disabled account cannot authenticate or retain session");
 Check(BundleManager.TryStructured(["Running (0):","NAME  VERSION  CLIENT  SERVER  UI","Available, not running (0):","NAME  VERSION  CLIENT  SERVER  UI"],out var empty)&&empty.Count==0,"current native empty bundle tables accepted");
 Check(BundleManager.TryStructured(["Running (1):","NAME VERSION CLIENT SERVER UI","running-bundle 1.0 yes yes no","Available, not running (1):","NAME VERSION CLIENT SERVER UI","stopped-bundle 1.0 yes yes no"],out var table)&&table["running-bundle"]=="running"&&table["stopped-bundle"]=="stopped","structured tables provide authoritative running and stopped states");
 Check(!BundleManager.TryStructured(["Running (1):","NAME VERSION CLIENT SERVER UI","Available, not running (0):","NAME VERSION CLIENT SERVER UI"],out _),"incomplete native table is not reported as success");
 Check(!BundleManager.TryStructured(["Running (0):","NAME VERSION CLIENT SERVER UI","Available, not running (0):","NAME VERSION CLIENT SERVER UI","unrecognized footer"],out _),"unknown native table output rejected");
 var staging=Path.Combine(root,"staging");Directory.CreateDirectory(staging);
 var bad=Path.Combine(root,"bad.zip");
 File.WriteAllText(bad,"not a zip");Reject(()=>ArchiveInstaller.Extract(bad,staging),"corrupt ZIP rejected");
 File.Delete(bad);
 using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("../escape.txt").Open());writer.Write("bad");}
 Reject(()=>ArchiveInstaller.Extract(bad,staging),"ZIP path traversal rejected");Check(!File.Exists(Path.Combine(root,"escape.txt")),"ZIP cannot write outside staging");
 File.Delete(bad);
 using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create)){var entry=archive.CreateEntry("reserver/link");entry.ExternalAttributes=unchecked((int)0xA1FF0000);using var writer=new StreamWriter(entry.Open());writer.Write("/etc/passwd");}
 Reject(()=>ArchiveInstaller.Extract(bad,staging),"ZIP symbolic links rejected");
 File.Delete(bad);
 using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("reserver/cfg/example").Open());writer.Write("safe");}
 ArchiveInstaller.Extract(bad,staging);Check(File.ReadAllText(Path.Combine(staging,"cfg","example"))=="safe","official wrapper is normalized");
 Reject(()=>ArchiveInstaller.Extract(bad,staging),"existing extracted files cannot be overwritten");
 Reject(()=>SetupService.Validate("relative","windows"),"relative host paths rejected");
 var cfgRoot=Path.Combine(root,"cfgtest");Directory.CreateDirectory(Path.Combine(cfgRoot,"cfg"));
 File.WriteAllText(Path.Combine(cfgRoot,"cfg","server.cfg"),"# keep me\r\nsv_bindaddr \"[::]:20770\"\r\n#sv_lan false\r\nsv_name \"Old\"\r\nextra_setting 5\r\n");
 ServerConfig.Write(cfgRoot,new("New name","A description","ep1","DEU","[::]:20770","",true));
 var cfgText=File.ReadAllText(Path.Combine(cfgRoot,"cfg","server.cfg"));
 Check(cfgText.Contains("# keep me")&&cfgText.Contains("extra_setting 5")&&cfgText.Contains("sv_name \"New name\"")&&cfgText.Contains("sv_lan true")&&cfgText.Contains("sv_region \"DEU\"")&&cfgText.Contains("\r\n"),"server config edit keeps comments, unknown keys and line endings");
 var cfgView=ServerConfig.Read(cfgRoot,false);Check(cfgView.Values.Name=="New name"&&cfgView.Values.Region=="DEU"&&cfgView.Values.Lan&&File.Exists(Path.Combine(cfgRoot,"cfg","server.cfg.panel.bak")),"server config read-back and backup");
 Reject(()=>ServerConfig.Write(cfgRoot,new("x","","ep1","de","","",false)),"invalid region rejected");
 Reject(()=>ServerConfig.Write(cfgRoot,new("quo\"te","","ep1","DEU","","",false)),"quotes in server name rejected");
 ServerConfig.Write(cfgRoot,new("New name","A description","ep1","DEU","[::]:20770","",true,"[::]:20771",true,true,"alpha",true,"auto"));
 var settingsText=File.ReadAllText(Path.Combine(cfgRoot,"settings","server.settings.json"));
 Check(settingsText.Contains("\"auto_update\": true")&&File.ReadAllText(Path.Combine(cfgRoot,"cfg","server.cfg")).Contains("sv_privacy true")&&ServerConfig.Read(cfgRoot,false).Values.AutoUpdate,"server settings json and extra cfg keys written");
 var location=new PanelConfig{ServerRoot=Path.Combine(root,"gone"),PasswordHash="present",Backend=OperatingSystem.IsWindows()?"windows":"linux"};
 Check(ServerLocation.Issue(location)=="serverPath.missing","missing server directory is diagnosed");
 location.ServerRoot=cfgRoot;Check(ServerLocation.Issue(location)=="serverPath.files","missing executable is diagnosed");
 var nativeBackend=location.Backend;location.Backend="docker";Check(ServerLocation.Issue(location) is null,"Docker mount may await first-start runtime seeding");location.Backend=nativeBackend;
 File.WriteAllText(Path.Combine(cfgRoot,OperatingSystem.IsWindows()?"REServer.exe":"REServer"),"fixture");
 Check(ServerLocation.Issue(location) is null,"available runtime clears path diagnosis");
 Reject(()=>ServerLocation.Validate(location,cfgRoot,new("external",1,cfgRoot,"keep-running")),"external process cannot be rebound");
 location.Backend="docker";Reject(()=>ServerLocation.Validate(location,cfgRoot,new("stopped",null,cfgRoot,"keep-running")),"Docker mount rebinding requires deployment");
 // ---- bundle tools (experimental): layout, secrets, redaction, settings
 Check(BundleLayout.ValidName("my-bundle_1.0")&&!BundleLayout.ValidName("../x")&&!BundleLayout.ValidName(".hidden")&&!BundleLayout.ValidName("a b")&&!BundleLayout.ValidName("x/y")&&!BundleLayout.ValidName(""),"bundle names follow the same rules as the bundle manager");
 var btRoot=Path.Combine(root,"bundle-tools");Directory.CreateDirectory(btRoot);
 var btConfig=new PanelConfig{ServerRoot=Path.Combine(btRoot,"server"),PasswordHash="x"};Directory.CreateDirectory(Path.Combine(btConfig.ServerRoot,"data","ugc"));
 Check(BundleLayout.BundleDir(btConfig,"demo")==Path.Combine(Path.GetFullPath(btConfig.ServerRoot),"data","ugc","demo")&&BundleLayout.SourceDir(btConfig,"demo").EndsWith("demo"+Path.DirectorySeparatorChar+"panel_src"),"bundle and source folders resolve inside data/ugc");
 Reject(()=>BundleLayout.BundleDir(btConfig,"..\escape"),"bundle path traversal rejected");
 var secrets=new SecretStore(btRoot);var sealedToken=secrets.Protect("ghp_exampleSecretToken");
 Check(sealedToken!="ghp_exampleSecretToken"&&!sealedToken.Contains("ghp_")&&secrets.Unprotect(sealedToken)=="ghp_exampleSecretToken"&&new SecretStore(btRoot).Unprotect(sealedToken)=="ghp_exampleSecretToken","tokens are encrypted at rest and survive a restart");
 var tampered=Convert.FromBase64String(sealedToken);tampered[^1]^=1;Reject(()=>secrets.Unprotect(Convert.ToBase64String(tampered)),"tampered secret rejected");
 var redactor=new Redactor();redactor.Add("ghp_exampleSecretToken");
 Check(!redactor.Redact("push https://user:hunter2@example.com/r.git with ghp_exampleSecretToken").Contains("hunter2")&&!redactor.Redact("push with ghp_exampleSecretToken").Contains("ghp_example")&&!redactor.Redact("AUTHORIZATION: basic dXNlcjp0b2tlbjEyMzQ1Ng==").Contains("dXNlcjp0b2tlbjEyMzQ1Ng"),"logs never contain tokens or credentials");
 var btConfigPath=Path.Combine(btRoot,"panel.json");PanelConfig.Save(btConfigPath,btConfig);
 using(var btService=new BundleToolsService(btConfig,btConfigPath,()=>null)){
  Check(!btConfig.BundleTools.Enabled,"bundle tools are off by default");
  Reject(()=>btService.RequireEnabled(),"bundle tool actions are refused while the feature is off");
  btService.ApplySettings("{\"enabled\":true,\"keepBackups\":5,\"commitName\":\"Panel Bot\",\"commitEmail\":\"bot@example.com\"}");
  Check(btConfig.BundleTools.Enabled&&btConfig.BundleTools.KeepBackups==5&&PanelConfig.Load(btConfigPath).BundleTools.CommitName=="Panel Bot","bundle tool settings are validated and persisted");
  Reject(()=>btService.ApplySettings("{\"keepBackups\":99}"),"out-of-range bundle tool setting rejected");
  Reject(()=>btService.ApplySettings("{\"commitEmail\":\"not-an-email\"}"),"invalid commit email rejected");
  btService.RequireEnabled();
  var busyGate=new TaskCompletionSource();var first=btService.Jobs.Start("test","a",async _=>await busyGate.Task);
  Reject(()=>btService.Jobs.Start("test","b",_=>Task.CompletedTask),"only one bundle job runs at a time");
  busyGate.SetResult();for(var i=0;i<100&&btService.Jobs.Get(first.Id)!.State=="running";i++)await Task.Delay(20);
  Check(btService.Jobs.Get(first.Id)!.State=="succeeded"&&btService.Jobs.Tail(first.Id).Length>0,"finished jobs keep a log");
 }
 // ---- bundle ZIP structure check and install
 byte[] BundleZipBytes(Dictionary<string,string> files){using var buffer=new MemoryStream();using(var zip=new System.IO.Compression.ZipArchive(buffer,System.IO.Compression.ZipArchiveMode.Create,true))foreach(var (path,content) in files){var entry=zip.CreateEntry(path);using var writer=new StreamWriter(entry.Open());writer.Write(content);}return buffer.ToArray();}
 const string manifest="[General]\ndescription = \"Demo\"\nauthor = \"tester\"\nversion = \"1.2.3\"\n[Scripting]\nclient_blob = \"cl.wasm\"\nserver_blob = \"sv.wasm\"\n[ReUi]\ninitial_page = \"index.html\"\ncontent_root = \"ui/\"\nfiles = [\n  \"ui/index.html\",\n  \"ui/assets/**\",\n]\n[Meta]\nconfig_version = \"alpha_playtest\"\n";
 var goodZip=Path.Combine(btRoot,"good.zip");File.WriteAllBytes(goodZip,BundleZipBytes(new(){["config.toml"]=manifest,["sv.wasm"]="\0asm",["cl.wasm"]="\0asm",["ui/index.html"]="<html>",["ui/assets/app.js"]="x",["vfs/seed.txt"]="seed"}));
 var goodReport=BundleZip.Inspect(goodZip,"My Bundle.zip",1L<<30);
 Check(goodReport.Ok&&goodReport.Name=="My-Bundle"&&goodReport.Meta!.Author=="tester"&&goodReport.Meta.Version=="1.2.3"&&goodReport.Meta.HasUi&&goodReport.Root=="","a complete bundle ZIP passes the structure check");
 var wrappedZip=Path.Combine(btRoot,"wrapped.zip");File.WriteAllBytes(wrappedZip,BundleZipBytes(new(){["inner-bundle/config.toml"]=manifest,["inner-bundle/sv.wasm"]="a",["inner-bundle/cl.wasm"]="b",["inner-bundle/ui/index.html"]="<html>",["inner-bundle/ui/assets/a.js"]="x"}));
 var wrappedReport=BundleZip.Inspect(wrappedZip,"whatever.zip",1L<<30);Check(wrappedReport.Ok&&wrappedReport.Root=="inner-bundle/"&&wrappedReport.Name=="inner-bundle","a ZIP wrapped in one folder is unwrapped");
 var missingBlob=Path.Combine(btRoot,"missing.zip");File.WriteAllBytes(missingBlob,BundleZipBytes(new(){["config.toml"]=manifest,["sv.wasm"]="a",["ui/index.html"]="<html>",["ui/assets/a.js"]="x"}));
 var missingReport=BundleZip.Inspect(missingBlob,"m.zip",1L<<30);Check(!missingReport.Ok&&missingReport.Checks.Any(c=>c.Id=="blob.client_blob"&&c.Status=="fail"),"a referenced blob that is missing fails the check");
 var noManifest=Path.Combine(btRoot,"nomanifest.zip");File.WriteAllBytes(noManifest,BundleZipBytes(new(){["readme.txt"]="x",["a/b.txt"]="y"}));
 Check(!BundleZip.Inspect(noManifest,"n.zip",1L<<30).Ok,"a ZIP without config.toml fails the check");
 var traversal=Path.Combine(btRoot,"traversal.zip");File.WriteAllBytes(traversal,BundleZipBytes(new(){["config.toml"]=manifest,["../evil.txt"]="x"}));
 Check(!BundleZip.Inspect(traversal,"t.zip",1L<<30).Ok,"path traversal in a ZIP is refused");
 File.WriteAllText(Path.Combine(btRoot,"notzip.zip"),"hello");Check(!BundleZip.Inspect(Path.Combine(btRoot,"notzip.zip"),"x.zip",1L<<30).Ok,"a file that is not a ZIP is refused");
 Check(!BundleZip.Inspect(goodZip,"x.zip",10).Ok,"an oversized upload is refused");
 {
  var toml=TomlDoc.Parse("# c\n[General]\ndescription = \"A \\\"quoted\\\" # not a comment\" # trailing\nauthor = 'literal'\n[Scripting]\nenable_vfs = true\n[ReUi]\nfiles = [\"a\", 'b',\n \"c\"]\n");
  Check(toml.String("General","description")=="A \"quoted\" # not a comment"&&toml.String("General","author")=="literal"&&toml.Bool("Scripting","enable_vfs")==true&&toml.Array("ReUi","files").SequenceEqual(["a","b","c"]),"the manifest reader handles quotes, comments, booleans and multi-line arrays");
 }
 using(var uploadService=new BundleToolsService(btConfig,btConfigPath,()=>null)){
  uploadService.ApplySettings("{\"enabled\":true,\"uploadEnabled\":true,\"keepBackups\":2}");
  var uploads=BundleLayout.Uploads(btConfigPath);Directory.CreateDirectory(uploads);
  string Stash(string source){var id=Guid.NewGuid().ToString("N");File.Copy(source,Path.Combine(uploads,id+".zip"));return id+".zip";}
  async Task<BundleJob> Finished(BundleJob job){for(var i=0;i<200&&uploadService.Jobs.Get(job.Id)!.State=="running";i++)await Task.Delay(25);return uploadService.Jobs.Get(job.Id)!;}
  var firstFile=Stash(goodZip);
  var inspected=System.Text.Json.JsonSerializer.Serialize(uploadService.UploadInspect(System.Text.Json.JsonSerializer.Serialize(new{file=firstFile,originalName="demo.zip"})));
  Check(inspected.Contains("\"exists\":false"),"inspection reports whether the bundle already exists");
  var install=await Finished(uploadService.UploadInstall(System.Text.Json.JsonSerializer.Serialize(new{file=firstFile,name="demo",replace=false})));
  var demoDir=BundleLayout.BundleDir(btConfig,"demo");
  Check(install.State=="succeeded"&&File.Exists(Path.Combine(demoDir,"config.toml"))&&File.Exists(Path.Combine(demoDir,"sv.wasm"))&&File.Exists(Path.Combine(demoDir,"ui","index.html"))&&File.Exists(Path.Combine(demoDir,"vfs","seed.txt")),"an uploaded bundle is installed into data/ugc");
  Directory.CreateDirectory(Path.Combine(demoDir,"panel_src"));File.WriteAllText(Path.Combine(demoDir,"panel_src","keep.cs"),"// source");File.WriteAllText(Path.Combine(demoDir,"vfs","seed.txt"),"runtime data");File.WriteAllText(Path.Combine(demoDir,"stale.txt"),"old build artifact");
  var conflict=await Finished(uploadService.UploadInstall(System.Text.Json.JsonSerializer.Serialize(new{file=Stash(goodZip),name="demo",replace=false})));
  Check(conflict.State=="failed"&&conflict.Error=="bundleTools.exists","installing over an existing bundle needs an explicit replace");
  var replaced=await Finished(uploadService.UploadInstall(System.Text.Json.JsonSerializer.Serialize(new{file=Stash(goodZip),name="demo",replace=true})));
  Check(replaced.State=="succeeded"&&!File.Exists(Path.Combine(demoDir,"stale.txt"))&&File.Exists(Path.Combine(demoDir,"panel_src","keep.cs"))&&File.ReadAllText(Path.Combine(demoDir,"vfs","seed.txt"))=="runtime data","replacing keeps panel_src and runtime vfs data and removes stale artifacts");
  Check(BundleSwap.LatestBackup(BundleLayout.Backups(btConfigPath),"demo") is {} backupDir&&File.Exists(Path.Combine(backupDir,"stale.txt")),"the previous bundle files are backed up before a replace");
  var badInstall=await Finished(uploadService.UploadInstall(System.Text.Json.JsonSerializer.Serialize(new{file=Stash(missingBlob),name="other",replace=false})));
  Check(badInstall.State=="failed"&&!Directory.Exists(BundleLayout.BundleDir(btConfig,"other")),"an invalid ZIP installs nothing");
  Reject(()=>uploadService.UploadInstall(System.Text.Json.JsonSerializer.Serialize(new{file="../../etc/passwd",name="x",replace=false})),"upload ids cannot point outside the upload folder");
 }
 Console.WriteLine(passed+" core checks passed");
}finally{Directory.Delete(root,true);}
