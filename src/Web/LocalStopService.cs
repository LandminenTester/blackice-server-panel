using System.IO.Pipes;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;
public sealed class LocalStopService(PanelConfig config, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(config.PipeName + ".web", PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(3000);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                if (await reader.ReadLineAsync(timeout.Token) == "shutdown")
                {
                    try { await writer.WriteLineAsync("ok"); }
                    finally { lifetime.StopApplication(); }
                    return;
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
}
