using ReplayPanel.Shared;
namespace ReplayPanel.Web;
public sealed class LocalStopService(PanelConfig config, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Transport.Listen(config.PipeName + ".web", async (pipe, ct) => {
            using var reader = new StreamReader(pipe, leaveOpen:true); using var writer = new StreamWriter(pipe, leaveOpen:true) { AutoFlush=true };
            if (await reader.ReadLineAsync(ct) == "shutdown") { try { await writer.WriteLineAsync("ok"); } finally { lifetime.StopApplication(); } }
        }, stoppingToken); } catch (OperationCanceledException) {}
    }
}
