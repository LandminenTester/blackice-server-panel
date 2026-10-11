using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
// In-process adapter lets bundle protocol share the supervisor's backend and command serialization.
public sealed class PipeClient(IServerBackend backend)
{
    public Task<Reply> Call(Request request,CancellationToken ct=default)=>Handle(request);
    private async Task<Reply> Handle(Request request)
    {
        switch(request.Action) {
            case "status":return new(true,Status:backend.Status());
            case "console":return new(true,Lines:backend.ConsoleSnapshot());
            case "logs":return new(true,Lines:backend.LogSnapshot());
            case "command":await backend.SendAsync(request.Command??"");return new(true);
            case "start":await backend.StartAsync();break;
            case "stop":await backend.StopAsync();break;
            case "force-stop":await backend.StopAsync(true);break;
            case "restart":await backend.StopAsync();await backend.StartAsync();break;
            default:throw new InvalidOperationException("request.invalid");
        }
        return new(true,Status:backend.Status());
    }
}
