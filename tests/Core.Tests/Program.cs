using System.IO.Compression;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
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
 Console.WriteLine(passed+" core checks passed");
}finally{Directory.Delete(root,true);}
