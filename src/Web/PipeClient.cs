using System.IO.Pipes;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;
public sealed class PipeClient(PanelConfig config)
{
    public async Task<Reply> Call(Request request, CancellationToken ct = default)
    {
        using var pipe = new NamedPipeClientStream(".", config.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(3000, ct); }
        catch (Exception e) when (e is TimeoutException || e is IOException) { return new(false, Error: "Supervisor nicht erreichbar. scripts/start.ps1 ausführen."); }
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, PanelConfig.Json));
        var line = await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(config.StopTimeoutSeconds + 10), ct);
        return line is null ? new(false, Error: "Supervisor hat die Verbindung geschlossen.") :
            JsonSerializer.Deserialize<Reply>(line, PanelConfig.Json) ?? new(false, Error: "Ungültige Supervisor-Antwort.");
    }
}
