using ReplayPanel.Shared;
using System.Text.Json;
namespace ReplayPanel.Web;
public sealed record BundleInfo(string Name,string State,string? Author=null,string? Description=null,string? Version=null);
public sealed record BundleList(bool Ok,BundleInfo[] Bundles,bool CanControl,DateTimeOffset CheckedAt,string? Error=null);
public sealed class BundleManager(PipeClient pipe)
{
    public Task<Reply> Command(Request request,CancellationToken ct=default)=>pipe.Call(request,ct);
    private async Task<BundleList> Read(Request request,CancellationToken ct)
    {
        var reply=await pipe.Call(request,ct);
        if(!reply.Ok) return new(false,[],false,DateTimeOffset.UtcNow,reply.Error);
        return JsonSerializer.Deserialize<BundleList>(reply.Lines![0],PanelConfig.Json)!;
    }
    public Task<BundleList> List(CancellationToken ct)=>Read(new("bundles"),ct);
    public Task<BundleList> Action(string name,string action,CancellationToken ct)=>Read(new("bundle-action",JsonSerializer.Serialize(new[]{name,action})),ct);
}
