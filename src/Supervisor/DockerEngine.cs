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
}
