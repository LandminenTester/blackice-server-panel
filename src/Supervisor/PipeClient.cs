using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
// In-process adapter lets bundle protocol share the supervisor's backend and command serialization.
public sealed class PipeClient(IServerBackend backend)
{
    public bool BridgeConnected=>backend is BridgeBackend bridge&&bridge.Connected;
    public Task<BridgeStream> Execute(string command,CancellationToken ct)=>backend is BridgeBackend bridge?bridge.Execute(command,ct):throw new InvalidOperationException("bridge.disconnected");
    public Task<BridgeStream> Stream(long cursor,CancellationToken ct)=>backend is BridgeBackend bridge?bridge.Stream(cursor,ct):throw new InvalidOperationException("bridge.disconnected");
    public Task<Reply> Call(Request request,CancellationToken ct=default)=>Handle(request);
    private async Task<Reply> Handle(Request request)
    {
        switch(request.Action) {
            case "status":return new(true,Status:backend.Status());
            case "console":return new(true,Lines:backend.ConsoleSnapshot());
            case "logs":return new(true,Lines:backend.LogSnapshot());
            case "command":if(BridgeConnected){var result=await Execute(request.Command??"",CancellationToken.None);return new(true,Lines:result.Lines.Where(x=>x.Source=="console").Select(x=>x.Text).ToArray());}await backend.SendAsync(request.Command??"");return new(true);
            case "start":await backend.StartAsync();break;
            case "stop":await backend.StopAsync();break;
            case "force-stop":await backend.StopAsync(true);break;
            case "restart":await backend.StopAsync();await backend.StartAsync();break;
            default:throw new InvalidOperationException("request.invalid");
        }
        return new(true,Status:backend.Status());
    }
}
