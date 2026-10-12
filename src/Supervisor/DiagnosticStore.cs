using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

public sealed class DiagnosticStore {
 readonly object gate=new();readonly string directory;readonly Func<BridgeSettings> settings;readonly Queue<DiagnosticLine> recent=new();
 long sequence,totalBytes;int segment;string? active;DateTimeOffset cleaned=DateTimeOffset.MinValue;
 public DiagnosticStore(string directory,Func<BridgeSettings> settings){this.directory=Path.GetFullPath(directory);this.settings=settings;Directory.CreateDirectory(directory);
  if(!OperatingSystem.IsWindows())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  Cleanup();foreach(var file in Files()){foreach(var text in File.ReadLines(file)){try{var item=JsonSerializer.Deserialize<DiagnosticLine>(text,PanelConfig.Json);if(item is not null){sequence=Math.Max(sequence,item.Seq);recent.Enqueue(item);while(recent.Count>8192)recent.Dequeue();}}catch(JsonException){}}}
 }
 string[] Files()=>Directory.GetFiles(directory,"diagnostic-*.jsonl").Order(StringComparer.Ordinal).ToArray();
 public long Cursor {get{lock(gate)return sequence;}}
 public DiagnosticLine Append(string session,long? sourceSeq,string source,string module,string text){lock(gate){
  var item=new DiagnosticLine(++sequence,session,sourceSeq,source,module[..Math.Min(module.Length,256)],text[..Math.Min(text.Length,16384)],DateTimeOffset.UtcNow);recent.Enqueue(item);while(recent.Count>8192)recent.Dequeue();
  if(active is null||!File.Exists(active)||new FileInfo(active).Length>4*1024*1024||!Path.GetFileName(active).Contains(DateTime.UtcNow.ToString("yyyyMMdd")))active=Path.Combine(directory,$"diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{segment++:D4}.jsonl");
  var encoded=JsonSerializer.Serialize(item,PanelConfig.Json)+"\n";File.AppendAllText(active,encoded);totalBytes+=System.Text.Encoding.UTF8.GetByteCount(encoded);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(active,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  if(DateTimeOffset.UtcNow-cleaned>TimeSpan.FromSeconds(10)||totalBytes>settings().MaxMegabytes*1024L*1024L)Cleanup();return item;
 }}
 public DiagnosticPage Read(long cursor,string source="all",string? module=null,string? search=null,int limit=500,bool history=false,string[]? sessions=null){lock(gate){
  if(cursor<0||source is not ("all" or "console" or "log" or "panel")||limit is <1 or >500||module?.Length>256||search?.Length>256)throw new InvalidOperationException("diagnostics.invalid");
  var items=history?History():recent.ToArray().AsEnumerable();long scanned=cursor;long first=0;var result=new List<DiagnosticLine>();
  foreach(var item in items){if(first==0)first=item.Seq;if(item.Seq<=cursor)continue;scanned=item.Seq;
   if((sessions is null||sessions.Contains(item.StreamId))&&(source=="all"||item.Source==source)&&(string.IsNullOrEmpty(module)||item.Module==module)&&(string.IsNullOrEmpty(search)||item.Text.Contains(search,StringComparison.OrdinalIgnoreCase)))result.Add(item);
   if(result.Count>=limit)break;
  }
  return new(scanned, cursor>0&&first>cursor+1?first-cursor-1:0,result.ToArray());
 }}
 IEnumerable<DiagnosticLine> History(){foreach(var file in Files())foreach(var text in File.ReadLines(file)){DiagnosticLine? item=null;try{item=JsonSerializer.Deserialize<DiagnosticLine>(text,PanelConfig.Json);}catch(JsonException){}if(item is not null)yield return item;}}
 public string[] Snapshot(string source,string? session=null){lock(gate)return recent.Where(x=>x.Source==source&&(session is null||x.StreamId==session)).TakeLast(1500).Select(x=>x.Text).ToArray();}
 public void Cleanup(){lock(gate){var config=settings();var files=Files();long total=files.Sum(x=>new FileInfo(x).Length);foreach(var file in files){
   if(File.GetLastWriteTimeUtc(file)>=DateTime.UtcNow.AddDays(-config.RetentionDays)&&total<=config.MaxMegabytes*1024L*1024L)continue;
   var length=new FileInfo(file).Length;File.Delete(file);total-=length;if(file==active)active=null;
  }totalBytes=total;cleaned=DateTimeOffset.UtcNow;}}
}
