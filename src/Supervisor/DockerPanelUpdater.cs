using System.Net.Http.Json;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;

public sealed record DockerPanelUpdate(string Id,string ConfigPath,string SupervisorId,string WebId,string? Previous,string PreviousVersion,string Target);

// Docker keeps its image, mounts, networks, labels and deployment specification.
// Both entrypoints select a verified, persistent release from the shared state.
public sealed class DockerPanelUpdater(PanelConfig config,string configPath)
{
    readonly HttpClient http=new DockerEngine(config).Http;
    public static bool Enabled=>!OperatingSystem.IsWindows()&&File.Exists("/.dockerenv")&&Environment.GetEnvironmentVariable("REPLAY_PANEL_DOCKER_UPDATES")=="1"&&File.Exists("/app/docker-entrypoint.sh");
    static bool IsId(string? id,int length)=>id is not null&&id.Length==length&&id.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    async Task<JsonElement> Inspect(string id,CancellationToken ct=default){if(!IsId(id,64))throw new InvalidOperationException("docker.identity");using var response=await http.GetAsync("containers/"+id+"/json",ct);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>(ct);}
    static string Id(JsonElement container)=>container.GetProperty("Id").GetString()!;
    static bool HasEnv(JsonElement container,string value)=>container.GetProperty("Config").GetProperty("Env").EnumerateArray().Any(x=>x.GetString()==value);
    static JsonElement Mount(JsonElement container,string path)=>container.GetProperty("Mounts").EnumerateArray().Where(x=>path==x.GetProperty("Destination").GetString()||path.StartsWith(x.GetProperty("Destination").GetString()!.TrimEnd('/')+"/",StringComparison.Ordinal)).OrderByDescending(x=>x.GetProperty("Destination").GetString()!.Length).First();
    static string Bind(JsonElement mount)=> (mount.GetProperty("Type").GetString()=="volume"?mount.GetProperty("Name").GetString():mount.GetProperty("Source").GetString())+":"+mount.GetProperty("Destination").GetString();
    static string SourcePath(JsonElement mount,string path)=>mount.GetProperty("Source").GetString()+path[mount.GetProperty("Destination").GetString()!.Length..];
    static void ValidateRole(JsonElement container,string role){
        var settings=container.GetProperty("Config");var entry=settings.GetProperty("Entrypoint");
        if(entry.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("update.deployment");
        var command=entry.EnumerateArray().Select(x=>x.GetString()).Concat(settings.TryGetProperty("Cmd",out var cmd)&&cmd.ValueKind==JsonValueKind.Array?cmd.EnumerateArray().Select(x=>x.GetString()):[]).Take(2).ToArray();
        if(command.Length!=2||command[0]!="/app/docker-entrypoint.sh"||command[1]!=role)throw new InvalidOperationException("update.deployment");
    }
    async Task<(JsonElement Supervisor,JsonElement Web)> FindPair(CancellationToken ct=default){
        if(!Enabled)throw new InvalidOperationException("update.deployment");
        var self=Environment.GetEnvironmentVariable("HOSTNAME")??"";
        if(!System.Text.RegularExpressions.Regex.IsMatch(self,"^[a-f0-9]{12,64}$"))throw new InvalidOperationException("docker.identity");
        using var response=await http.GetAsync("containers/"+self+"/json",ct);response.EnsureSuccessStatusCode();var supervisor=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        ValidateRole(supervisor,"supervisor");
        if(!HasEnv(supervisor,"REPLAY_PANEL_CONFIG="+configPath))throw new InvalidOperationException("docker.identity");
        var labels=supervisor.GetProperty("Config").GetProperty("Labels");
        if(!labels.TryGetProperty("com.docker.compose.project",out var project)||string.IsNullOrEmpty(project.GetString()))throw new InvalidOperationException("update.deployment");
        var filter=JsonSerializer.Serialize(new{label=new[]{"com.docker.compose.project="+project.GetString()}});
        using var listing=await http.GetAsync("containers/json?all=1&filters="+Uri.EscapeDataString(filter),ct);listing.EnsureSuccessStatusCode();
        var peers=await listing.Content.ReadFromJsonAsync<JsonElement>(ct);var matches=new List<JsonElement>();
        var source=SourcePath(Mount(supervisor,configPath),configPath);
        foreach(var peer in peers.EnumerateArray()){
            if(Id(peer)==Id(supervisor))continue;
            var peerLabels=peer.GetProperty("Labels");
            if(!peerLabels.TryGetProperty("replay.panel.role",out var role)||role.GetString()!="web")continue;
            var web=await Inspect(Id(peer),ct);ValidateRole(web,"web");
            if(!HasEnv(web,"REPLAY_PANEL_CONFIG="+configPath)||SourcePath(Mount(web,configPath),configPath)!=source||web.GetProperty("Image").GetString()!=supervisor.GetProperty("Image").GetString())throw new InvalidOperationException("docker.identity");
            if(web.GetProperty("Mounts").EnumerateArray().Any(x=>x.GetProperty("Destination").GetString()==config.DockerSocket))throw new InvalidOperationException("docker.identity");
            matches.Add(web);
        }
        if(matches.Count!=1)throw new InvalidOperationException("update.deployment");
        return(supervisor,matches[0]);
    }
    public async Task Apply(string id,string target,CancellationToken ct=default){
        if(!IsId(id,32)||!PanelVersion.TryParse(target,out _))throw new InvalidOperationException("update.invalid");
        var pair=await FindPair(ct);var state=Path.GetDirectoryName(configPath)!;
        var marker=Path.Combine(state,"panel-active-runtime");var previous=File.Exists(marker)?File.ReadAllText(marker).Trim():null;
        if(previous is not null&&!IsId(previous,32))throw new InvalidOperationException("update.invalid");
        var path=Path.Combine(state,"panel-updates",id,"manifest.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new DockerPanelUpdate(id,configPath,Id(pair.Supervisor),Id(pair.Web),previous,PanelUpdates.RunningVersion,target),PanelConfig.Json));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var runtime=Environment.GetEnvironmentVariable("REPLAY_PANEL_RUNTIME")??"/run/replay-panel";
        var mounts=new[]{Mount(pair.Supervisor,configPath),Mount(pair.Supervisor,runtime),Mount(pair.Supervisor,config.DockerSocket)}.DistinctBy(x=>x.GetProperty("Destination").GetString()).Select(Bind).ToArray();
        var helperExecutable=previous is null?"/app/artifacts/supervisor/Supervisor":Path.Combine(state,"panel-updates",previous,"stage","artifacts","supervisor","Supervisor");
        var body=new{Image=pair.Supervisor.GetProperty("Image").GetString(),User=pair.Supervisor.GetProperty("Config").GetProperty("User").GetString(),
            Entrypoint=new[]{helperExecutable,"--apply-docker-panel-update",path},
            Env=new[]{"REPLAY_PANEL_CONFIG="+configPath,"REPLAY_PANEL_RUNTIME="+runtime,"REPLAY_PANEL_DOCKER_UPDATES=1","DOTNET_RUNNING_IN_CONTAINER=true"},
            Labels=new Dictionary<string,string>{{"replay.panel.instance",config.InstanceId},{"replay.panel.update-helper","true"}},
            HostConfig=new{AutoRemove=true,NetworkMode=pair.Supervisor.GetProperty("HostConfig").GetProperty("NetworkMode").GetString(),Binds=mounts,CapDrop=new[]{"ALL"},SecurityOpt=new[]{"no-new-privileges:true"}}};
        using var create=await http.PostAsJsonAsync("containers/create",body,ct);create.EnsureSuccessStatusCode();var helperId=(await create.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("Id").GetString()!;
        try{using var start=await http.PostAsync("containers/"+helperId+"/start",null,ct);start.EnsureSuccessStatusCode();}
        catch{using var remove=await http.DeleteAsync("containers/"+helperId+"?force=true",CancellationToken.None);throw;}
    }
    public static async Task Run(string manifestPath){
        var manifest=JsonSerializer.Deserialize<DockerPanelUpdate>(File.ReadAllText(manifestPath),PanelConfig.Json)??throw new InvalidOperationException("update.invalid");
        var state=Path.GetDirectoryName(manifest.ConfigPath)!;
        if(!IsId(manifest.Id,32)||manifest.Previous is not null&&!IsId(manifest.Previous,32)||Path.GetFullPath(manifestPath)!=Path.Combine(state,"panel-updates",manifest.Id,"manifest.json"))throw new InvalidOperationException("update.invalid");
        var config=PanelConfig.Load(manifest.ConfigPath);var updater=new DockerPanelUpdater(config,manifest.ConfigPath);
        var supervisor=await updater.Inspect(manifest.SupervisorId);var web=await updater.Inspect(manifest.WebId);
        ValidateRole(supervisor,"supervisor");ValidateRole(web,"web");
        if(!HasEnv(supervisor,"REPLAY_PANEL_CONFIG="+manifest.ConfigPath)||!HasEnv(web,"REPLAY_PANEL_CONFIG="+manifest.ConfigPath)||SourcePath(Mount(supervisor,manifest.ConfigPath),manifest.ConfigPath)!=SourcePath(Mount(web,manifest.ConfigPath),manifest.ConfigPath))throw new InvalidOperationException("docker.identity");
        var directory=Path.GetDirectoryName(manifestPath)!;var stage=Path.Combine(directory,"stage");
        PanelUpdates.ValidatePackage(stage,manifest.Target,false);
        using var log=new StreamWriter(Path.Combine(directory,"apply.log"),append:true){AutoFlush=true};
        Console.SetOut(log);Console.SetError(log);
        var jobFile=Path.Combine(state,"panel-update.json");var marker=Path.Combine(state,"panel-active-runtime");
        void Save(string status,string phase,string? error=null){var tmp=jobFile+".worker.tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(new UpdateJob(manifest.Id,status,phase,Error:error),PanelConfig.Json));File.Move(tmp,jobFile,true);}
        async Task Policy(JsonElement container,object policy){using var response=await updater.http.PostAsJsonAsync("containers/"+Id(container)+"/update",new{RestartPolicy=policy});response.EnsureSuccessStatusCode();}
        async Task Kill(JsonElement container){var current=await updater.Inspect(Id(container));if(current.GetProperty("State").GetProperty("Running").GetBoolean()){using var response=await updater.http.PostAsync("containers/"+Id(container)+"/kill?signal=KILL",null);response.EnsureSuccessStatusCode();}using var wait=await updater.http.PostAsync("containers/"+Id(container)+"/wait?condition=not-running",null);wait.EnsureSuccessStatusCode();}
        async Task Start(JsonElement container){using var response=await updater.http.PostAsync("containers/"+Id(container)+"/start",null);response.EnsureSuccessStatusCode();}
        void Select(string? id){if(id is null)File.Delete(marker);else{var temp=marker+".tmp";File.WriteAllText(temp,id);File.Move(temp,marker,true);}}
        async Task<bool> Healthy(string version){
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(2)};
            for(var n=0;n<60;n++){
                try {
                    var boot=await ControlConsole.Call(config,new("bootstrap")).WaitAsync(TimeSpan.FromSeconds(3));using var data=JsonDocument.Parse(boot.Lines![0]);
                    if(data.RootElement.GetProperty("version").GetString()==version){
                        var currentWeb=await updater.Inspect(manifest.WebId);
                        foreach(var network in currentWeb.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()){
                            if(!supervisor.GetProperty("NetworkSettings").GetProperty("Networks").TryGetProperty(network.Name,out _))continue;
                            var address=network.Value.GetProperty("IPAddress").GetString();if(string.IsNullOrEmpty(address))continue;
                            using var request=new HttpRequestMessage(HttpMethod.Get,"http://"+address+":"+new Uri(config.Url).Port+"/api/session");request.Headers.Host=config.AllowedHosts.FirstOrDefault()??new Uri(config.Url).Authority;
                            using var response=await client.SendAsync(request);if(response.IsSuccessStatusCode)return true;
                        }
                    }
                }catch(Exception e)when(e is IOException or HttpRequestException or System.Net.Sockets.SocketException or JsonException or InvalidOperationException or OperationCanceledException or KeyNotFoundException or TimeoutException){Console.WriteLine(e.GetType().Name);}
                await Task.Delay(1000);
            }
            return false;
        }
        var changed=false;
        try{
            await Task.Delay(2000);
            // SIGKILL avoids old versions' shutdown hooks stopping the game. The
            // helper holds no game mount or game ID and never controls the game.
            await Policy(web,new{Name="no"});await Policy(supervisor,new{Name="no"});
            changed=true;Save("applying","restart");await Kill(web);await Kill(supervisor);
            Select(manifest.Id);await Start(supervisor);await Start(web);
            if(!await Healthy(manifest.Target))throw new InvalidOperationException("update.health");
            Save("completed","completed");Console.WriteLine("Docker panel runtime updated to "+manifest.Target);
        }catch(Exception e){
            Console.WriteLine(e);
            if(changed){try{await Kill(web);await Kill(supervisor);Select(manifest.Previous);await Start(supervisor);await Start(web);Save("failed","rollback",await Healthy(manifest.PreviousVersion)?"update.restored":"update.rollback");}catch(Exception recovery){Console.WriteLine(recovery);Save("failed","rollback","update.rollback");}}
            else Save("failed","failed","update.failed");
        }finally{
            await Policy(supervisor,supervisor.GetProperty("HostConfig").GetProperty("RestartPolicy"));
            await Policy(web,web.GetProperty("HostConfig").GetProperty("RestartPolicy"));
        }
    }
}
