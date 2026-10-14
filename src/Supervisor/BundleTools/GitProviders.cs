using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;
public sealed record RepoInfo(string CloneUrl,string WebUrl,string DefaultBranch);
public sealed record RepoOwner(string Id,string Name,bool Personal);
public sealed record RemoteRepo(string Name,string FullName,string CloneUrl,string WebUrl,string DefaultBranch,bool Private);
public static class GitProviders
{
 public static readonly string[] Types=["github","gitlab","gitea"];
 public static string NormalizeBase(string type,string baseUrl){
  if(!Types.Contains(type)||!Uri.TryCreate((baseUrl??"").Trim().TrimEnd('/'),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.IsLoopback)throw new InvalidOperationException("bundleTools.account");
  if(System.Net.IPAddress.TryParse(uri.Host,out var ip)&&!OutboundHttp.PublicAddress(ip))throw new InvalidOperationException("bundleTools.account");
  return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
 }
 public static bool Matches(GitAccount account,string url)=>Uri.TryCreate(url,UriKind.Absolute,out var target)&&Uri.TryCreate(account.BaseUrl,UriKind.Absolute,out var origin)&&target.Scheme=="https"&&target.UserInfo.Length==0&&target.Query.Length==0&&target.Fragment.Length==0&&target.GetLeftPart(UriPartial.Authority).Equals(origin.GetLeftPart(UriPartial.Authority),StringComparison.OrdinalIgnoreCase)&&(target.AbsolutePath==origin.AbsolutePath||target.AbsolutePath.StartsWith(origin.AbsolutePath.TrimEnd('/')+"/",StringComparison.Ordinal));
 public static string DefaultBase(string type)=>type switch{"github"=>"https://github.com","gitlab"=>"https://gitlab.com",_=>"https://codeberg.org"};
 public static string ApiBase(GitAccount a)=>a.Type switch{"github"=>new Uri(a.BaseUrl).Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)?"https://api.github.com":a.BaseUrl.TrimEnd('/')+"/api/v3","gitlab"=>a.BaseUrl.TrimEnd('/')+"/api/v4",_=>a.BaseUrl.TrimEnd('/')+"/api/v1"};
 static string Text(JsonElement e,string key,string fallback="")=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
 static string E(string value)=>Uri.EscapeDataString(value);
 static async Task<JsonDocument> Send(HttpClient http,GitAccount account,string token,HttpMethod method,string path,CancellationToken ct,object? body=null){
  NormalizeBase(account.Type,account.BaseUrl);
  using var request=new HttpRequestMessage(method,ApiBase(account)+path);request.Headers.UserAgent.ParseAdd("BlackICE-Panel");request.Headers.Accept.ParseAdd("application/json");
  if(account.Type=="gitlab")request.Headers.Add("PRIVATE-TOKEN",token);else request.Headers.Authorization=new AuthenticationHeaderValue(account.Type=="github"?"Bearer":"token",token);
  if(account.Type=="github")request.Headers.Add("X-GitHub-Api-Version","2022-11-28");
  if(body is not null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
  using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)throw new InvalidOperationException("bundleTools.tokenRejected");
  if(response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.BadRequest)throw new InvalidOperationException("bundleTools.providerConflict");
  if(!response.IsSuccessStatusCode)throw new InvalidOperationException("bundleTools.providerFailed");
  if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return JsonDocument.Parse("{}");
  await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var bytes=new byte[16384];int count;
  while((count=await stream.ReadAsync(bytes,ct))>0){if(buffer.Length+count>2*1024*1024)throw new InvalidOperationException("bundleTools.providerFailed");buffer.Write(bytes,0,count);}
  try{return JsonDocument.Parse(buffer.ToArray());}catch(JsonException){throw new InvalidOperationException("bundleTools.providerFailed");}
 }
 public static async Task<string> TestAsync(HttpClient http,GitAccount a,string token,CancellationToken ct){using var doc=await Send(http,a,token,HttpMethod.Get,"/user",ct);return Text(doc.RootElement,a.Type=="gitlab"?"username":"login");}
 public static async Task<RepoOwner[]> OwnersAsync(HttpClient http,GitAccount a,string token,CancellationToken ct,bool writable=true){
  var user=await TestAsync(http,a,token,ct);var list=new List<RepoOwner>{new("",user,true)};
  var route=a.Type=="gitlab"?"/groups?min_access_level="+(writable?"40":"10")+"&per_page=100":a.Type=="github"?"/user/orgs?per_page=100":"/user/orgs?limit=100";
  using var doc=await Send(http,a,token,HttpMethod.Get,route,ct);
  foreach(var org in doc.RootElement.EnumerateArray().Take(100)){var id=a.Type=="gitlab"?org.GetProperty("id").ToString():Text(org,a.Type=="github"?"login":"username",Text(org,"name"));var name=a.Type=="gitlab"?Text(org,"full_path",Text(org,"name")):id;if(id.Length>0)list.Add(new(id,name,false));}
  return [..list];
 }
 public static async Task<object> RepositoriesAsync(HttpClient http,GitAccount a,string token,string query,int page,string owner,CancellationToken ct){
  if(page is <1 or >100||query.Length>100||owner.Length>200||query.Any(char.IsControl))throw new InvalidOperationException("bundleTools.invalid");
  string path;
  if(a.Type=="github"){
   var owners=await OwnersAsync(http,a,token,ct);var chosen=owners.FirstOrDefault(x=>x.Id==owner)??throw new InvalidOperationException("bundleTools.owner");
   path="/search/repositories?per_page=50&page="+page+"&q="+E((query.Length>0?query+" in:name ":"")+"user:"+chosen.Name+" fork:true")+"&sort=updated";
  }else if(a.Type=="gitlab"){
   if(owner.Length>0){if(!(await OwnersAsync(http,a,token,ct,false)).Any(o=>o.Id==owner))throw new InvalidOperationException("bundleTools.owner");path="/groups/"+E(owner)+"/projects?include_subgroups=true&with_shared=false&simple=true&per_page=50&page="+page+"&search="+E(query);}
   else path="/projects?membership=true&simple=true&per_page=50&page="+page+"&search="+E(query);
  }
  else path="/user/repos?limit=50&page="+page;
  using var doc=await Send(http,a,token,HttpMethod.Get,path,ct);var array=a.Type=="github"?doc.RootElement.GetProperty("items"):doc.RootElement;var repos=new List<RemoteRepo>();
  foreach(var item in array.EnumerateArray()){
   var clone=Text(item,a.Type=="gitlab"?"http_url_to_repo":"clone_url");var web=Text(item,a.Type=="gitlab"?"web_url":"html_url");if(!Matches(a,clone)||!Matches(a,web))continue;
   var name=Text(item,"name");var full=Text(item,a.Type=="gitlab"?"path_with_namespace":"full_name",name);if(a.Type=="gitea"&&(!full.Contains(query,StringComparison.OrdinalIgnoreCase)||(owner.Length>0&&!full.StartsWith(owner+"/",StringComparison.OrdinalIgnoreCase))))continue;
   repos.Add(new(name,full,clone,web,Text(item,"default_branch","main"),a.Type=="gitlab"?Text(item,"visibility")!="public":item.TryGetProperty("private",out var p)&&p.ValueKind==JsonValueKind.True));
  }
  return new{repositories=repos,page,hasMore=array.GetArrayLength()==50};
 }
 public static async Task<RepoInfo> CreateRepositoryAsync(HttpClient http,GitAccount a,string token,string name,string description,bool isPrivate,CancellationToken ct,string? owner=null){
  if(!string.IsNullOrEmpty(owner)&&!(await OwnersAsync(http,a,token,ct)).Any(x=>x.Id==owner&&!x.Personal))throw new InvalidOperationException("bundleTools.owner");
  var body=new Dictionary<string,object>{{"name",name},{"description",description}};
  if(a.Type=="gitlab"){body["path"]=name;body["visibility"]=isPrivate?"private":"public";body["initialize_with_readme"]=false;if(!string.IsNullOrEmpty(owner))body["namespace_id"]=long.Parse(owner!);}
  else{body["private"]=isPrivate;body["auto_init"]=false;if(a.Type=="gitea")body["default_branch"]="main";}
  var path=a.Type=="gitlab"?"/projects":!string.IsNullOrEmpty(owner)?"/orgs/"+E(owner!)+"/repos":"/user/repos";
  using var doc=await Send(http,a,token,HttpMethod.Post,path,ct,body);var r=doc.RootElement;var clone=Text(r,a.Type=="gitlab"?"http_url_to_repo":"clone_url");var web=Text(r,a.Type=="gitlab"?"web_url":"html_url");
  if(!Matches(a,clone)||!Matches(a,web))throw new InvalidOperationException("bundleTools.providerFailed");return new(clone,web,Text(r,"default_branch","main"));
 }
 public static Task<string> ReviewAsync(HttpClient http,GitAccount a,string token,string remote,string source,string target,string title,CancellationToken ct)=>MergeRequestAsync(http,a,token,remote,source,target,title,false,"","",ct);
 public static async Task<string> MergeRequestAsync(HttpClient http,GitAccount a,string token,string remote,string source,string target,string title,bool merge,string sourceCommit,string targetCommit,CancellationToken ct,Action<string>? created=null){
  if(!Matches(a,remote))throw new InvalidOperationException("bundleTools.accountOrigin");GitService.ValidateRef(source);GitService.ValidateRef(target);if(source==target||title.Length is <1 or >200||title.Any(char.IsControl))throw new InvalidOperationException("bundleTools.invalid");
  var relative=new Uri(remote).AbsolutePath[new Uri(a.BaseUrl).AbsolutePath.TrimEnd('/').Length..].Trim('/');if(relative.EndsWith(".git"))relative=relative[..^4];
  var project=a.Type=="gitlab"?E(relative):string.Join('/',relative.Split('/').Select(E));
  var path=a.Type=="gitlab"?"/projects/"+project+"/merge_requests":"/repos/"+project+"/pulls";
  object body=a.Type=="gitlab"?new{source_branch=source,target_branch=target,title,remove_source_branch=false}:new{head=source,@base=target,title};
  using var doc=await Send(http,a,token,HttpMethod.Post,path,ct,body);var url=Text(doc.RootElement,a.Type=="gitlab"?"web_url":"html_url");if(!Matches(a,url))throw new InvalidOperationException("bundleTools.providerFailed");created?.Invoke(url);
  if(merge){
   // A provider-side review object preserves branch protections and required CI/reviews.
   var id=doc.RootElement.GetProperty(a.Type=="gitlab"?"iid":"number").GetInt64();
   var root=a.Type=="gitlab"?"/projects/"+project:"/repos/"+project;
   using var branch=await Send(http,a,token,HttpMethod.Get,root+"/"+(a.Type=="gitlab"?"repository/branches/":"branches/")+E(target),ct);
   var actual=Text(branch.RootElement.GetProperty("commit"),a.Type=="gitlab"?"id":"sha");
   if(actual!=targetCommit)throw new InvalidOperationException("bundleTools.branchChanged");
   var mergePath=path+"/"+id+"/merge";
   object payload=a.Type=="gitlab"?new{sha=sourceCommit,should_remove_source_branch=false}:a.Type=="github"?new{sha=sourceCommit,merge_method="merge"}:(object)new{head_commit_id=sourceCommit,Do="merge",delete_branch_after_merge=false};
   using var result=await Send(http,a,token,a.Type=="gitea"?HttpMethod.Post:HttpMethod.Put,mergePath,ct,payload);
   if(a.Type=="github"&&(!result.RootElement.TryGetProperty("merged",out var done)||done.ValueKind!=JsonValueKind.True))throw new InvalidOperationException("bundleTools.mergeUnconfirmed");
   if(a.Type=="gitlab"&&Text(result.RootElement,"state")!="merged")throw new InvalidOperationException("bundleTools.mergeUnconfirmed");
   if(a.Type=="gitea"){using var verify=await Send(http,a,token,HttpMethod.Get,path+"/"+id,ct);if(!verify.RootElement.TryGetProperty("merged",out var giteaDone)||giteaDone.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("bundleTools.mergeUnconfirmed");}
  }
  return url;
 }
}
