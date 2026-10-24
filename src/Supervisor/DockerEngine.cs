using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed class DockerEngine
{
    // Preserve dashboard-managed settings; the vendor entrypoint rewrites them
    // from environment variables on every start, discarding panel edits.
    public static readonly string[] GameEntrypoint=["/bin/sh","-c", "set -eu; mkdir -p /srv/data/ugc; if [ ! -x /srv/REServer ]; then cp -a /opt/replay/seed/. /srv/; chmod +x /srv/REServer; fi; if [ ! -f /srv/cfg/server.cfg ] && [ -f /srv/cfg/server.cfg.example ]; then cp /srv/cfg/server.cfg.example /srv/cfg/server.cfg; fi; mkdir -p /srv/settings; if [ ! -f /srv/settings/server.settings.json ]; then printf '%s\\n' '{\"auto_update\":false,\"channel\":\"alpha\",\"restart_after_update\":\"auto\"}' > /srv/settings/server.settings.json; fi; cd /srv; exec stdbuf -o0 ./REServer --bindaddr \"[::]:${GAME_PORT:-20770}\" --voip-addr \":::${VOIP_PORT:-20771}\" \"$@\"", "panel-game"];
    private readonly HttpClient http;
    internal HttpClient Http=>http;
    private readonly PanelConfig config;
    public DockerEngine(PanelConfig config)
    {
        this.config=config;
        var handler=new SocketsHttpHandler {ConnectCallback=async (_,ct)=>{var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);await socket.ConnectAsync(new UnixDomainSocketEndPoint(config.DockerSocket),ct);return new NetworkStream(socket,true);}};
        http=new HttpClient(handler){BaseAddress=new Uri("http://docker/v1.45/"),Timeout=TimeSpan.FromMinutes(30)};
    }
    public async Task<JsonElement> Inspect(CancellationToken ct=default)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(config.ContainerId,"^[a-f0-9]{64}$"))throw new InvalidOperationException("docker.identity");
        using var response=await http.GetAsync("containers/"+config.ContainerId+"/json",ct);response.EnsureSuccessStatusCode();
        var data=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if(!data.GetProperty("Config").GetProperty("Labels").TryGetProperty("replay.panel.instance",out var label) || label.GetString()!=config.InstanceId)throw new InvalidOperationException("docker.identity");
        return data;
    }
    public async Task<string> CreateServer(string target,CancellationToken ct)
    {
        using(var pull=await http.PostAsync("images/create?fromImage=ghcr.io%2Freplayre%2Fserver&tag=latest",null,ct)) {pull.EnsureSuccessStatusCode();var result=await pull.Content.ReadAsStringAsync(ct);if(result.Contains("errorDetail"))throw new InvalidOperationException("install.failed");}
        // REPLAY_PANEL_HOST_SERVER is the daemon-side mount path, independent of the supervisor container path.
        var host=Environment.GetEnvironmentVariable("REPLAY_PANEL_HOST_SERVER") ?? target;
        var body=new {Image="ghcr.io/replayre/server",Entrypoint=GameEntrypoint,Tty=true,OpenStdin=true,StdinOnce=false,
            Env=new[]{"AUTO_UPDATE=false","GAME_PORT=20770","VOIP_PORT=20771"},
            Labels=new Dictionary<string,string>{{"replay.panel.instance",config.InstanceId}},
            ExposedPorts=new Dictionary<string,object>{{"20770/tcp",new{}},{"20770/udp",new{}},{"20771/tcp",new{}},{"20771/udp",new{}}},
            HostConfig=new { Binds=new[]{host+":/srv"}, RestartPolicy=new {Name="no"},
                PortBindings=new Dictionary<string,object>{{"20770/tcp",new[]{new{HostPort=config.GamePort.ToString()}}},{"20770/udp",new[]{new{HostPort=config.GamePort.ToString()}}},{"20771/tcp",new[]{new{HostPort=config.VoicePort.ToString()}}},{"20771/udp",new[]{new{HostPort=config.VoicePort.ToString()}}}}}};
        using var response=await http.PostAsJsonAsync("containers/create?name=replay-server-"+config.InstanceId,body,ct);response.EnsureSuccessStatusCode();
        var data=await response.Content.ReadFromJsonAsync<JsonElement>(ct);return data.GetProperty("Id").GetString()!;
    }
    public async Task InitializeFiles(string target,CancellationToken ct)
    {
        await Inspect(ct);
        using var response=await http.GetAsync("containers/"+config.ContainerId+"/archive?path=%2Fopt%2Freplay%2Fseed%2Fcfg%2Fserver.cfg.example",ct);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var tar=new System.Formats.Tar.TarReader(stream);
        var entry=await tar.GetNextEntryAsync(false,ct)??throw new InvalidOperationException("server.files");
        using var reader=new StreamReader(entry.DataStream!);
        Directory.CreateDirectory(Path.Combine(target,"cfg"));
        await File.WriteAllTextAsync(Path.Combine(target,"cfg","server.cfg"),await reader.ReadToEndAsync(ct)+"\nsv_lan true\n",ct);
    }
    public async Task EnsureWritableServer(CancellationToken ct=default)
    {
        var owned=await Inspect(ct);
        var mount=owned.GetProperty("Mounts").EnumerateArray().Single(x=>x.GetProperty("Destination").GetString()=="/srv");
        if(!mount.GetProperty("RW").GetBoolean())throw new InvalidOperationException("path.readonly");
        string image;
        if(File.Exists("/.dockerenv")) {
            var self=Environment.GetEnvironmentVariable("HOSTNAME")??"";
            if(!System.Text.RegularExpressions.Regex.IsMatch(self,"^[a-f0-9]{12,64}$"))throw new InvalidOperationException("docker.identity");
            using var response=await http.GetAsync("containers/"+self+"/json",ct);response.EnsureSuccessStatusCode();
            var own=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var configPath=Environment.GetEnvironmentVariable("REPLAY_PANEL_CONFIG");
            if(string.IsNullOrEmpty(configPath)||!own.GetProperty("Config").GetProperty("Env").EnumerateArray().Any(x=>x.GetString()=="REPLAY_PANEL_CONFIG="+configPath))throw new InvalidOperationException("docker.identity");
            image=own.GetProperty("Image").GetString()!;
        } else {
            image="ghcr.io/landminentester/blackice-server-panel:"+PanelUpdates.RunningVersion;
            using var cached=await http.GetAsync("images/"+Uri.EscapeDataString(image)+"/json",ct);
            if(cached.StatusCode==System.Net.HttpStatusCode.NotFound){
                using var pull=await http.PostAsync("images/create?fromImage="+Uri.EscapeDataString(image),null,ct);pull.EnsureSuccessStatusCode();
                if((await pull.Content.ReadAsStringAsync(ct)).Contains("errorDetail"))throw new InvalidOperationException("path.readonly");
            }else cached.EnsureSuccessStatusCode();
        }
        var body=new{Image=image,User="0:0",Entrypoint=new[]{"/app/docker-permissions.sh"},
            Labels=new Dictionary<string,string>{{"replay.panel.instance",config.InstanceId},{"replay.panel.permissions-helper","true"}},
            HostConfig=new{NetworkMode="none",ReadonlyRootfs=true,CapDrop=new[]{"ALL"},CapAdd=new[]{"FOWNER","DAC_OVERRIDE"},SecurityOpt=new[]{"no-new-privileges:true"},Binds=new[]{mount.GetProperty("Source").GetString()+":/server"}}};
        using var create=await http.PostAsJsonAsync("containers/create",body,ct);create.EnsureSuccessStatusCode();
        var id=(await create.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("Id").GetString()!;
        try {
            using var start=await http.PostAsync("containers/"+id+"/start",null,ct);start.EnsureSuccessStatusCode();
            using var wait=await http.PostAsync("containers/"+id+"/wait?condition=not-running",null,ct);wait.EnsureSuccessStatusCode();
            if((await wait.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("StatusCode").GetInt32()!=0)throw new InvalidOperationException("path.readonly");
        }finally{using var remove=await http.DeleteAsync("containers/"+id+"?force=true",CancellationToken.None);remove.EnsureSuccessStatusCode();}
    }
    public async Task Remove() { await Inspect();using var response=await http.DeleteAsync("containers/"+config.ContainerId);response.EnsureSuccessStatusCode(); }
    public async Task Action(string action,CancellationToken ct=default)
    {
        await Inspect(ct);
        using var response=await http.PostAsync("containers/"+config.ContainerId+"/"+action,null,ct);response.EnsureSuccessStatusCode();
    }
    public async Task<Stream> Attach(CancellationToken ct)
    {
        await Inspect(ct);
        var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(config.DockerSocket),ct);
        var stream=new NetworkStream(socket,true);
        var request=System.Text.Encoding.ASCII.GetBytes("POST /v1.45/containers/"+config.ContainerId+"/attach?stream=1&stdin=1&stdout=1&stderr=1 HTTP/1.1\r\nHost: docker\r\nConnection: Upgrade\r\nUpgrade: tcp\r\nContent-Length: 0\r\n\r\n");
        await stream.WriteAsync(request,ct);
        var header=new List<byte>();var one=new byte[1];
        while(header.Count<16384) {if(await stream.ReadAsync(one,ct)==0)throw new IOException("docker.attach");header.Add(one[0]);if(header.Count>=4 && header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
        if(!System.Text.Encoding.ASCII.GetString(header.ToArray()).StartsWith("HTTP/1.1 101")) {stream.Dispose();throw new IOException("docker.attach");}
        return stream;
    }
    public async Task<string[]> Logs(CancellationToken ct=default)
    {
        await Inspect(ct);
        return (await http.GetStringAsync("containers/"+config.ContainerId+"/logs?stdout=1&stderr=1&tail=1500",ct)).Split('\n');
    }
    string BridgeNetwork=>"blackice-bridge-"+config.InstanceId;
    public async Task<string> PrepareBridgeNetwork(string configPath,CancellationToken ct=default) {
        var self=Environment.GetEnvironmentVariable("HOSTNAME")??"";
        if(!System.Text.RegularExpressions.Regex.IsMatch(self,"^[a-f0-9]{12,64}$"))throw new InvalidOperationException("bridge.dockerHost");
        using(var check=await http.GetAsync("networks/"+BridgeNetwork,ct)) {
            if(check.StatusCode==System.Net.HttpStatusCode.NotFound) {using var create=await http.PostAsJsonAsync("networks/create",new{Name=BridgeNetwork,Driver="bridge",Internal=true,Labels=new Dictionary<string,string>{{"replay.panel.instance",config.InstanceId}}},ct);create.EnsureSuccessStatusCode();}
            else {check.EnsureSuccessStatusCode();var net=await check.Content.ReadFromJsonAsync<JsonElement>(ct);if(!net.GetProperty("Internal").GetBoolean()||!net.GetProperty("Labels").TryGetProperty("replay.panel.instance",out var label)||label.GetString()!=config.InstanceId)throw new InvalidOperationException("docker.identity");}
        }
        using var own=await http.GetAsync("containers/"+self+"/json",ct);own.EnsureSuccessStatusCode();var data=await own.Content.ReadFromJsonAsync<JsonElement>(ct);
        // Inspect/connect only this Supervisor container, never arbitrary containers.
        if(data.GetProperty("Config").GetProperty("Env").EnumerateArray().All(x=>x.GetString()!="REPLAY_PANEL_CONFIG="+configPath))throw new InvalidOperationException("docker.identity");
        if(!data.GetProperty("NetworkSettings").GetProperty("Networks").TryGetProperty(BridgeNetwork,out _)) {using var connect=await http.PostAsJsonAsync("networks/"+BridgeNetwork+"/connect",new{Container=data.GetProperty("Id").GetString()},ct);connect.EnsureSuccessStatusCode();}
        using var again=await http.GetAsync("containers/"+self+"/json",ct);again.EnsureSuccessStatusCode();data=await again.Content.ReadFromJsonAsync<JsonElement>(ct);
        return data.GetProperty("NetworkSettings").GetProperty("Networks").GetProperty(BridgeNetwork).GetProperty("IPAddress").GetString()!;
    }
    public async Task ConfigureBridge(bool enabled,string endpoint,string token,string statePath,CancellationToken ct=default) {
        var old=await Inspect(ct);if(old.GetProperty("State").GetProperty("Running").GetBoolean())throw new InvalidOperationException("server.running");
        var oldConfig=old.GetProperty("Config");var previous=oldConfig.TryGetProperty("Cmd",out var cmd)&&cmd.ValueKind==JsonValueKind.Array?cmd.EnumerateArray().Select(x=>x.GetString()!).ToArray():[];
        var desired=new McpBridgeLaunchPolicy().Configure(previous,enabled,endpoint,token);
        var previousEntry=oldConfig.GetProperty("Entrypoint");
        if(desired.SequenceEqual(previous)&&previousEntry.ValueKind==JsonValueKind.Array&&previousEntry.EnumerateArray().Select(x=>x.GetString()).SequenceEqual(GameEntrypoint))return;
        var body=System.Text.Json.Nodes.JsonNode.Parse(oldConfig.GetRawText())!.AsObject();body["Cmd"]=JsonSerializer.SerializeToNode(desired);body["Entrypoint"]=JsonSerializer.SerializeToNode(GameEntrypoint);body.Remove("Hostname");
        body["HostConfig"]=System.Text.Json.Nodes.JsonNode.Parse(old.GetProperty("HostConfig").GetRawText());
        var endpoints=new Dictionary<string,object>();
        foreach(var network in old.GetProperty("NetworkSettings").GetProperty("Networks").EnumerateObject()){
            if(network.Name==BridgeNetwork&&!enabled)continue;
            var aliases=network.Value.TryGetProperty("Aliases",out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray().Select(x=>x.GetString()).Where(x=>x is not null&&!x.StartsWith(old.GetProperty("Id").GetString()![..12],StringComparison.Ordinal)).ToArray():[];
            endpoints[network.Name]=new{Aliases=aliases};
        }
        if(enabled)endpoints.TryAdd(BridgeNetwork,new{});
        if(endpoints.Count>0){body["HostConfig"]!["NetworkMode"]=endpoints.Keys.First();body["NetworkingConfig"]=JsonSerializer.SerializeToNode(new{EndpointsConfig=endpoints});}
        var backup=Path.Combine(statePath,"docker-before-bridge-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff")+".json");await File.WriteAllTextAsync(backup,old.GetRawText(),ct);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(backup,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var oldId=config.ContainerId;var name=old.GetProperty("Name").GetString()!.TrimStart('/');
        using var rename=await http.PostAsync("containers/"+oldId+"/rename?name="+Uri.EscapeDataString(name+"-previous-"+Guid.NewGuid().ToString("N")[..8]),null,ct);rename.EnsureSuccessStatusCode();
        try {using var create=await http.PostAsJsonAsync("containers/create?name="+Uri.EscapeDataString(name),body,ct);create.EnsureSuccessStatusCode();config.ContainerId=(await create.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("Id").GetString()!;}
        catch {using var restore=await http.PostAsync("containers/"+oldId+"/rename?name="+Uri.EscapeDataString(name),null,CancellationToken.None);restore.EnsureSuccessStatusCode();throw;}
    }
    public async Task<bool> OwnsBridge(BridgeIdentity identity,System.Net.IPAddress remote,CancellationToken ct) {
        var data=await Inspect(ct);if(!data.GetProperty("State").GetProperty("Running").GetBoolean()||identity.Pid!=1||identity.Workdir!="/srv")return false;
        return data.GetProperty("NetworkSettings").GetProperty("Networks").TryGetProperty(BridgeNetwork,out var net)&&net.GetProperty("IPAddress").GetString()==remote.ToString();
    }
    public async Task InstallRuntime(Dictionary<string,string> files,string privateDirectory,CancellationToken ct=default) {
        if(OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Docker runtime updates require Linux Supervisor");
        var owned=await Inspect(ct);if(owned.GetProperty("State").GetProperty("Running").GetBoolean())throw new InvalidOperationException("server.running");
        if(files.Keys.Any(x=>!ServerUpdates.ManagedPath(x)))throw new InvalidOperationException("serverUpdate.path");
        var archive=Path.Combine(privateDirectory,"runtime-"+Guid.NewGuid().ToString("N")+".tar");
        try {
            await using(var output=File.Create(archive)){using var tar=new System.Formats.Tar.TarWriter(output,leaveOpen:true);foreach(var file in files){await using var input=File.OpenRead(file.Value);var entry=new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile,file.Key){DataStream=input,Uid=10001,Gid=10001,Mode=File.GetUnixFileMode(file.Value)&(UnixFileMode)511};await tar.WriteEntryAsync(entry,ct);}}
            await using var stream=File.OpenRead(archive);using var content=new StreamContent(stream);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");
            using var reply=await http.PutAsync("containers/"+config.ContainerId+"/archive?path=%2Fsrv&noOverwriteDirNonDir=true",content,ct);reply.EnsureSuccessStatusCode();
        }finally{File.Delete(archive);}
    }
    public async Task RemoveRuntime(string[] files,CancellationToken ct=default) {
        if(files.Length==0)return;if(files.Any(x=>!ServerUpdates.ManagedPath(x)))throw new InvalidOperationException("serverUpdate.path");var owned=await Inspect(ct);
        if(owned.GetProperty("State").GetProperty("Running").GetBoolean())throw new InvalidOperationException("server.running");
        var mount=owned.GetProperty("Mounts").EnumerateArray().Single(x=>x.GetProperty("Destination").GetString()=="/srv");
        var body=new{Image=owned.GetProperty("Image").GetString(),User="0",Entrypoint=new[]{"/bin/rm"},Cmd=new[]{"-f","--"}.Concat(files.Select(x=>"/srv/"+x)).ToArray(),Labels=new Dictionary<string,string>{{"replay.panel.instance",config.InstanceId},{"replay.panel.update-helper","true"}},HostConfig=new{NetworkMode="none",Binds=new[]{mount.GetProperty("Source").GetString()+":/srv"}}};
        using var create=await http.PostAsJsonAsync("containers/create",body,ct);create.EnsureSuccessStatusCode();var id=(await create.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("Id").GetString()!;
        try {using var start=await http.PostAsync("containers/"+id+"/start",null,ct);start.EnsureSuccessStatusCode();using var wait=await http.PostAsync("containers/"+id+"/wait?condition=not-running",null,ct);wait.EnsureSuccessStatusCode();if((await wait.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("StatusCode").GetInt32()!=0)throw new InvalidOperationException("serverUpdate.recovery");}
        finally{using var remove=await http.DeleteAsync("containers/"+id+"?force=true",CancellationToken.None);remove.EnsureSuccessStatusCode();}
    }
}
