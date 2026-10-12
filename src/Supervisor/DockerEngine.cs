using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public sealed class DockerEngine
{
    private readonly HttpClient http;
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
        var body=new {Image="ghcr.io/replayre/server",Tty=true,OpenStdin=true,StdinOnce=false,
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
        var clean=new List<string>();for(var i=0;i<previous.Length;i++){if(previous[i]=="--mcp")continue;if(previous[i] is "--mcp-addr" or "--mcp-token"){i++;continue;}clean.Add(previous[i]);}
        var desired=enabled?clean.Concat(new LanBridgeLaunchPolicy().Arguments(endpoint,token)).ToArray():clean.ToArray();
        if(desired.SequenceEqual(previous))return;
        var body=System.Text.Json.Nodes.JsonNode.Parse(oldConfig.GetRawText())!.AsObject();body["Cmd"]=JsonSerializer.SerializeToNode(desired);body.Remove("Hostname");
        body["HostConfig"]=System.Text.Json.Nodes.JsonNode.Parse(old.GetProperty("HostConfig").GetRawText());
        if(enabled){body["HostConfig"]!["NetworkMode"]=BridgeNetwork;body["NetworkingConfig"]=JsonSerializer.SerializeToNode(new{EndpointsConfig=new Dictionary<string,object>{{BridgeNetwork,new{}}}});}
        else body["HostConfig"]!["NetworkMode"]="bridge";
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
}
