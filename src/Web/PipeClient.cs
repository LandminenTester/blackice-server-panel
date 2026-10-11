using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;
public sealed class PipeClient(PanelConfig config)
{
    public async Task<Reply> Call(Request request, CancellationToken ct = default)
    {
        try {
            using var pipe = await Transport.Connect(config.PipeName, ct);
            using var reader = new StreamReader(pipe, leaveOpen:true);
            using var writer = new StreamWriter(pipe, leaveOpen:true) { AutoFlush=true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, PanelConfig.Json));
            var line = await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(config.StopTimeoutSeconds + 15), ct);
            return line is null ? new(false, "connection.closed") : JsonSerializer.Deserialize<Reply>(line, PanelConfig.Json) ?? new(false, "connection.invalid");
        } catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException) { return new(false, "connection.unavailable"); }
    }
}
