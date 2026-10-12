// Adapted from replayre/blackice-mcp protocol.py, agent.py and hub.py.
// Upstream 7515a36f1d34d774f36dd00241b666bce8d1d1cf, MIT (see licenses/blackice-mcp-MIT.txt).
// Changes: async .NET transport, bounded framing, mandatory token/ownership checks and cancellation.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ReplayPanel.Supervisor;

public sealed record BridgeIdentity(string Realm,int Slot,int Pid,string Workdir,string BuildCommit,int RuntimeStage,string Logfile);
public sealed record BridgeWireLine(long Seq,string Source,string Module,string Text);
public sealed record BridgeStream(long Cursor,long Dropped,BridgeWireLine[] Lines);
public sealed class BridgeConnection : IAsyncDisposable {
 readonly TcpClient client; readonly Stream stream; readonly SemaphoreSlim write=new(1);
 readonly ConcurrentDictionary<long,TaskCompletionSource<JsonElement>> pending=new(); readonly CancellationTokenSource stop=new();
 long nextId; Task? reader; readonly Queue<long> ticks=new(); readonly object stateLock=new();
 public BridgeIdentity Identity {get;} public string SessionId {get;}=Guid.NewGuid().ToString("N");
 public DateTimeOffset LastSeen {get;private set;}=DateTimeOffset.UtcNow;
 public string Phase {get;private set;}="unknown"; public string Detail {get;private set;}="";
 public long Tick {get;private set;} public bool Closed=>stop.IsCancellationRequested;
 public string Health {get {lock(stateLock)return Closed?"dead":DateTimeOffset.UtcNow-LastSeen>TimeSpan.FromSeconds(30)?"frozen":ticks.Count==3&&ticks.Distinct().Count()==1?"stalled":"alive";}}
 public BridgeConnection(TcpClient client,BridgeIdentity identity){this.client=client;stream=client.GetStream();Identity=identity;}
 public void Begin(IAsyncEnumerator<string> frames)=>reader=Read(frames);
 async Task Read(IAsyncEnumerator<string> frames){try {while(await frames.MoveNextAsync()){
   using var doc=JsonDocument.Parse(frames.Current);var data=doc.RootElement;
   LastSeen=DateTimeOffset.UtcNow;
   if(data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("id",out var id)&&id.TryGetInt64(out var number)&&pending.TryRemove(number,out var waiting))waiting.TrySetResult(data.Clone());
 }}catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or JsonException or InvalidOperationException or DecoderFallbackException){}
 finally {stop.Cancel();client.Dispose();foreach(var item in pending.Values)item.TrySetException(new InvalidOperationException("bridge.disconnected"));pending.Clear();await frames.DisposeAsync();}}
 public async Task<JsonElement> Call(string method,object? parameters=null,CancellationToken ct=default,TimeSpan? timeout=null){
  if(Closed)throw new InvalidOperationException("bridge.disconnected");
  var id=Interlocked.Increment(ref nextId);var result=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=result;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct,stop.Token);deadline.CancelAfter(timeout??TimeSpan.FromSeconds(20));
  try {var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,method,@params=parameters})+"\n");await write.WaitAsync(deadline.Token);
   try{await stream.WriteAsync(bytes,deadline.Token);await stream.FlushAsync(deadline.Token);}finally{write.Release();}
   var data=await result.Task.WaitAsync(deadline.Token);
   if(data.TryGetProperty("error",out _))throw new InvalidOperationException("bridge.rejected");
   if(!data.TryGetProperty("result",out var reply))throw new InvalidOperationException("bridge.protocol");return reply.Clone();
  }catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new InvalidOperationException(Closed?"bridge.disconnected":"bridge.unconfirmed");}
  finally{pending.TryRemove(id,out _);}
 }
 public async Task Ping(CancellationToken ct){var data=await Call("ping",ct:ct,timeout:TimeSpan.FromSeconds(5));lock(stateLock){
  if(data.TryGetProperty("tick",out var tick)&&tick.TryGetInt64(out var n)){Tick=n;ticks.Enqueue(n);while(ticks.Count>3)ticks.Dequeue();}
  if(data.TryGetProperty("status",out var status)){Phase=status.TryGetProperty("phase",out var phase)?phase.GetString()??"unknown":"unknown";Detail=status.TryGetProperty("detail",out var detail)?detail.GetString()??"":"";}
 }}
 public static BridgeStream ParseStream(JsonElement data){
  var cursor=data.GetProperty("cursor").GetInt64();var dropped=data.TryGetProperty("dropped",out var d)?d.GetInt64():0;
  if(cursor<0||dropped<0)throw new InvalidOperationException("bridge.protocol");
  var lines=data.GetProperty("lines").EnumerateArray().Select(line=>new BridgeWireLine(line.GetProperty("seq").GetInt64(),line.GetProperty("source").GetString()??"",line.GetProperty("module").GetString()??"",line.GetProperty("text").GetString()??"")).ToArray();
  if(lines.Length>8192||lines.Any(x=>x.Seq<0||x.Seq>cursor||x.Source is not ("console" or "log")))throw new InvalidOperationException("bridge.protocol");
  return new(cursor,dropped,lines);
 }
 public async ValueTask DisposeAsync(){stop.Cancel();client.Dispose();if(reader is not null)await reader;}
}
public sealed class BridgeHub : IAsyncDisposable {
 readonly string token;readonly Func<BridgeIdentity,IPAddress,CancellationToken,Task<bool>> validate;
 readonly CancellationTokenSource stop=new();readonly ConcurrentBag<Task> peers=new();TcpListener? listener;Task? accepting;
 public BridgeConnection? Current {get;private set;}
 public int Port=>((IPEndPoint)listener!.LocalEndpoint).Port;
 public BridgeHub(string token,Func<BridgeIdentity,IPAddress,CancellationToken,Task<bool>> validate){this.token=token;this.validate=validate;}
 public void Start(IPAddress address,int port){listener=new(address,port);listener.Start(8);accepting=Accept();}
 async Task Accept(){try {while(!stop.IsCancellationRequested){var client=await listener!.AcceptTcpClientAsync(stop.Token);peers.Add(Register(client));}}catch(Exception e) when(e is OperationCanceledException or SocketException){} }
 async Task Register(TcpClient client){IAsyncEnumerator<string>? frames=null;Task<bool>? first=null;bool handed=false;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(5));
  try {frames=Frames(client.GetStream(),stop.Token).GetAsyncEnumerator();
   first=frames.MoveNextAsync().AsTask();if(!await first.WaitAsync(deadline.Token))return;
   using var doc=JsonDocument.Parse(frames.Current);var hello=doc.RootElement;
   if(hello.GetProperty("method").GetString()!="hello"||!hello.TryGetProperty("token",out var t)||!TokenEquals(t.GetString()??"",token))return;
   var s=hello.GetProperty("state");var identity=new BridgeIdentity(s.GetProperty("realm").GetString()!,s.GetProperty("slot").GetInt32(),s.GetProperty("pid").GetInt32(),s.GetProperty("workdir").GetString()!,s.GetProperty("build_commit").GetString()!,s.GetProperty("runtime_stage").GetInt32(),s.TryGetProperty("logfile",out var log)?log.GetString()??"":"");
   if(identity.Realm!="server"||identity.Pid<=0||!await validate(identity,((IPEndPoint)client.Client.RemoteEndPoint!).Address,deadline.Token))return;
   var previous=Current;var next=new BridgeConnection(client,identity);Current=next;handed=true;next.Begin(frames);if(previous is not null)await previous.DisposeAsync();
  }catch(Exception e) when(e is OperationCanceledException or IOException or SocketException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or DecoderFallbackException){}
  finally{if(!handed){client.Dispose();if(first is not null)try{await first;}catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or InvalidOperationException or DecoderFallbackException){}if(frames is not null)await frames.DisposeAsync();}}
 }
 static bool TokenEquals(string a,string b)=>CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
 public static async IAsyncEnumerable<string> Frames(Stream stream,[EnumeratorCancellation] CancellationToken ct=default){
  const int limit=8*1024*1024;var buffer=new byte[8192];using var line=new MemoryStream();var utf8=new UTF8Encoding(false,true);
  int n;while((n=await stream.ReadAsync(buffer,ct))>0){for(int i=0;i<n;i++){
   if(buffer[i]==10){if(line.Length>0)yield return utf8.GetString(line.GetBuffer(),0,(int)line.Length);line.SetLength(0);}
   else{if(line.Length>=limit)throw new InvalidOperationException("bridge.frame");line.WriteByte(buffer[i]);}
  }}if(line.Length>0)throw new InvalidOperationException("bridge.protocol");
 }
 public async ValueTask DisposeAsync(){stop.Cancel();listener?.Stop();if(accepting is not null)await accepting;if(Current is not null)await Current.DisposeAsync();await Task.WhenAll(peers);stop.Dispose();}
}
