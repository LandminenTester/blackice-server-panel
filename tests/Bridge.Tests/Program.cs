using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;

var count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
async Task Reject(Func<Task> call,string name){try{await call();throw new Exception("accepted: "+name);}catch(InvalidOperationException){Check(true,name);}}
var identity=new BridgeIdentity("server",0,1234,"/srv/test","abc123",1,"");
await using var hub=new BridgeHub("test-secret",(i,ip,ct)=>Task.FromResult(i.Pid==1234&&i.Workdir=="/srv/test"&&IPAddress.IsLoopback(ip)));
hub.Start(IPAddress.Loopback,0);
await using(var bad=await Fake.Connect(hub.Port,"wrong",identity)){await Task.Delay(100);Check(hub.Current is null,"wrong token cannot register");}
await using(var bad=await Fake.Connect(hub.Port,"test-secret",identity with{Pid=999})){await Task.Delay(100);Check(hub.Current is null,"wrong process cannot register");}
await using(var bad=await Fake.Connect(hub.Port,"test-secret",identity with{Realm="client"})){await Task.Delay(100);Check(hub.Current is null,"client realm cannot register");}
await using var fake=await Fake.Connect(hub.Port,"test-secret",identity,true);
for(var n=0;hub.Current is null&&n<100;n++)await Task.Delay(20);
var agent=hub.Current??throw new Exception("no registration");
Check(agent.Identity==identity,"fragmented handshake keeps process identity");
var results=await Task.WhenAll(Enumerable.Range(0,12).Select(n=>agent.Call("echo",new{text="Grüße 🌆 "+n})));
Check(results.Select(x=>x.GetProperty("text").GetString()).SequenceEqual(Enumerable.Range(0,12).Select(n=>"Grüße 🌆 "+n)),"parallel requests and Unicode correlate correctly");
await Reject(()=>agent.Call("never",timeout:TimeSpan.FromMilliseconds(100)),"missing reply is unconfirmed");
Check(fake.NeverCalls==1,"timeout never retries a mutation");
await Reject(()=>agent.Call("reject"),"server rejection is not success");
await agent.Ping(CancellationToken.None);await agent.Ping(CancellationToken.None);await agent.Ping(CancellationToken.None);
Check(agent.Health=="stalled"&&agent.Phase=="active"&&agent.Tick==7,"unchanged ticks are stalled, phase is preserved");
var session=agent.SessionId;
await using var replacement=await Fake.Connect(hub.Port,"test-secret",identity);
for(var n=0;hub.Current?.SessionId==session&&n<100;n++)await Task.Delay(20);
Check(hub.Current?.SessionId!=session&&agent.Closed,"reconnection has a new stream identity");
await Reject(async()=>{await foreach(var _ in BridgeHub.Frames(new MemoryStream(Encoding.UTF8.GetBytes("{partial")))){}},"truncated frame rejected");
await Reject(async()=>{await foreach(var _ in BridgeHub.Frames(new MemoryStream(new byte[8*1024*1024+1]))){}},"oversized frame rejected");
await Reject(()=>Task.FromResult(BridgeConnection.ParseStream(JsonSerializer.SerializeToElement(new{cursor=-1,dropped=0,lines=Array.Empty<object>()}))),"negative source cursor rejected");
await Reject(()=>Task.FromResult(BridgeConnection.ParseStream(JsonSerializer.SerializeToElement(new{cursor=1,dropped=0,lines=new[]{new{seq=1,source="secret",module="",text="x"}}}))),"unknown stream source rejected");
await Reject(()=>{BridgeBackend.ValidateCommand("exit");return Task.CompletedTask;},"bridge cannot bypass server stop control");
await Reject(()=>{BridgeBackend.ValidateCommand("echo x; exit");return Task.CompletedTask;},"bridge cannot bypass console validation");
var root=Path.Combine(Path.GetTempPath(),"blackice-diagnostics-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var settings=new BridgeSettings(false,20774,7,10);var store=new DiagnosticStore(root,()=>settings);
store.Append("first",1,"console","","hello");store.Append("first",2,"log","server_ugc","Started bundle test");store.Append("second",1,"console","","after restart");
var page=store.Read(0);Check(page.Lines.Length==3&&page.Lines[0].Seq<page.Lines[2].Seq,"panel cursor increases across source restarts");
Check(store.Read(0,"log","server_ugc","Started").Lines.Length==1,"source, module and search filters agree");
Check(store.Read(page.Cursor).Lines.Length==0,"cursor does not repeat entries");
Check(new DiagnosticStore(root,()=>settings).Read(0,history:true).Lines.Length==3,"history survives store restart");
for(var n=0;n<900;n++)store.Append("volume",n,"log","",new string('x',16384));
Check(Directory.GetFiles(root).Sum(x=>new FileInfo(x).Length)<=10L*1024*1024,"disk history stays below configured byte limit");
foreach(var file in Directory.GetFiles(root))File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-8));store.Cleanup();
Check(Directory.GetFiles(root).Length==0,"expired history removed");
await Reject(()=>Task.FromResult(store.Read(-1)),"negative panel cursor rejected");
await Reject(()=>{BridgeBackend.ValidateSettings(new(true,80));return Task.CompletedTask;},"invalid bridge port rejected");
Check(new LanBridgeLaunchPolicy().Arguments("127.0.0.1:20774","private").SequenceEqual(new[]{"--mcp","--mcp-addr","127.0.0.1:20774","--mcp-token","private"}),"only known upstream launch flags emitted");
Console.WriteLine($"{count} bridge checks passed");

sealed class Fake : IAsyncDisposable {
 readonly TcpClient client=new();readonly SemaphoreSlim writing=new(1);Task? reading;public int NeverCalls;
 public static async Task<Fake> Connect(int port,string token,BridgeIdentity identity,bool fragment=false){var fake=new Fake();await fake.client.ConnectAsync(IPAddress.Loopback,port);
  var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{method="hello",token,state=new{realm=identity.Realm,slot=identity.Slot,pid=identity.Pid,workdir=identity.Workdir,build_commit=identity.BuildCommit,runtime_stage=identity.RuntimeStage,logfile=identity.Logfile}})+"\n");
  if(fragment){foreach(var b in bytes)await fake.client.GetStream().WriteAsync(new[]{b});}else await fake.client.GetStream().WriteAsync(bytes);fake.reading=fake.Read();return fake;
 }
 async Task Read(){try{await foreach(var frame in BridgeHub.Frames(client.GetStream())){using var doc=JsonDocument.Parse(frame);var data=doc.RootElement;var id=data.GetProperty("id").GetInt64();var method=data.GetProperty("method").GetString();if(method=="never"){Interlocked.Increment(ref NeverCalls);continue;}
  object result=method=="ping"?new{tick=7,status=new{phase="active",detail=""}}:data.GetProperty("params").Clone();
  var bytes=Encoding.UTF8.GetBytes(method=="reject"?JsonSerializer.Serialize(new{id,error="rejected"})+"\n":JsonSerializer.Serialize(new{id,result})+"\n");await writing.WaitAsync();try{await client.GetStream().WriteAsync(bytes);}finally{writing.Release();}
 }}catch(Exception e) when(e is IOException or ObjectDisposedException or SocketException){} }
 public async ValueTask DisposeAsync(){client.Dispose();if(reading is not null)await reading;writing.Dispose();}
}
